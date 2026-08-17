using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KadoExile.Models;

namespace KadoExile.Services;

public sealed class PoeSessionProfileStore
{
	private sealed record SessionProfileDocument(int Version, Guid? ActiveProfileId, List<StoredSessionProfile>? Profiles);

	private sealed record StoredSessionProfile(Guid Id, string Name, string ProtectedPoeSessionId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

	private readonly string _path;

	private readonly string _backupPath;

	private readonly ISecretProtector _protector;

	private readonly JsonSerializerOptions _json = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private List<PoeSessionProfile> _profiles = new List<PoeSessionProfile>();

	public IReadOnlyList<PoeSessionProfile> Profiles => _profiles;

	public Guid? ActiveProfileId { get; private set; }

	public int ProfilesRequiringPoeSessionReentry { get; private set; }

	public PoeSessionProfile ActiveProfile => _profiles.FirstOrDefault(delegate(PoeSessionProfile profile)
	{
		Guid id = profile.Id;
		Guid? activeProfileId = ActiveProfileId;
		return id == activeProfileId;
	}) ?? _profiles.First();

	public PoeSessionProfileStore(string configDirectory, ISecretProtector protector)
	{
		Directory.CreateDirectory(configDirectory);
		_path = Path.Combine(configDirectory, "session-profiles.json");
		_backupPath = _path + ".bak";
		_protector = protector;
	}

	public void LoadOrSeed(IEnumerable<TradeProfile> legacyProfiles, Guid? legacyActiveId)
	{
		ProfilesRequiringPoeSessionReentry = 0;
		SessionProfileDocument document = TryRead(_path) ?? TryRead(_backupPath);
		List<StoredSessionProfile> list = document?.Profiles;
		if (list != null && list.Count > 0)
		{
			_profiles = document.Profiles.Select(FromStored).ToList();
			ActiveProfileId = (_profiles.Any(delegate(PoeSessionProfile profile)
			{
				Guid id = profile.Id;
				Guid? activeProfileId = document.ActiveProfileId;
				return id == activeProfileId;
			}) ? document.ActiveProfileId : new Guid?(_profiles[0].Id));
			return;
		}
		DateTimeOffset now = DateTimeOffset.UtcNow;
		_profiles = (from @group in legacyProfiles.GroupBy<TradeProfile, string>((TradeProfile profile) => profile.Name, StringComparer.OrdinalIgnoreCase)
			select @group.First() into profile
			select new PoeSessionProfile((profile.Id == Guid.Empty) ? Guid.NewGuid() : profile.Id, NormalizeSeedName(profile.Name), profile.PoeSessionId, (profile.CreatedAt == default(DateTimeOffset)) ? now : profile.CreatedAt, now)).ToList();
		if (_profiles.Count == 0)
		{
			_profiles.Add(new PoeSessionProfile(Guid.NewGuid(), "Default", string.Empty, now, now));
		}
		ActiveProfileId = (_profiles.Any(delegate(PoeSessionProfile profile)
		{
			Guid id = profile.Id;
			Guid? guid = legacyActiveId;
			return id == guid;
		}) ? legacyActiveId : new Guid?(_profiles[0].Id));
		Save();
	}

	public PoeSessionProfile Select(Guid id)
	{
		PoeSessionProfile result = Find(id);
		ActiveProfileId = id;
		Save();
		return result;
	}

	public PoeSessionProfile Create(string name, string poeSessionId)
	{
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		PoeSessionProfile poeSessionProfile = new PoeSessionProfile(NewId(), ValidateName(name, null), poeSessionId, utcNow, utcNow);
		_profiles.Add(poeSessionProfile);
		ActiveProfileId = poeSessionProfile.Id;
		Save();
		return poeSessionProfile;
	}

	public PoeSessionProfile Update(Guid id, string name, string poeSessionId)
	{
		PoeSessionProfile poeSessionProfile = Find(id)with
		{
			Name = ValidateName(name, id),
			PoeSessionId = poeSessionId,
			UpdatedAt = DateTimeOffset.UtcNow
		};
		_profiles[_profiles.FindIndex((PoeSessionProfile profile) => profile.Id == id)] = poeSessionProfile;
		Save();
		return poeSessionProfile;
	}

	public PoeSessionProfile Delete(Guid id)
	{
		if (_profiles.Count <= 1)
		{
			throw new InvalidOperationException("At least one POESESSID profile must remain.");
		}
		PoeSessionProfile poeSessionProfile = Find(id);
		_profiles.Remove(poeSessionProfile);
		if (ActiveProfileId == id)
		{
			ActiveProfileId = _profiles[0].Id;
		}
		Save();
		return poeSessionProfile;
	}

	private PoeSessionProfile Find(Guid id)
	{
		return _profiles.FirstOrDefault((PoeSessionProfile profile) => profile.Id == id) ?? throw new KeyNotFoundException("POESESSID profile was not found.");
	}

	private string ValidateName(string name, Guid? currentId)
	{
		string normalized = name.Trim();
		if (normalized.Length == 0)
		{
			throw new ArgumentException("Profile name is empty.", "name");
		}
		if (_profiles.Any(delegate(PoeSessionProfile profile)
		{
			Guid id = profile.Id;
			Guid? guid = currentId;
			return id != guid && profile.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase);
		}))
		{
			throw new ArgumentException("Profile name already exists.", "name");
		}
		return normalized;
	}

	private Guid NewId()
	{
		Guid id;
		do
		{
			id = Guid.NewGuid();
		}
		while (_profiles.Any((PoeSessionProfile profile) => profile.Id == id));
		return id;
	}

	private PoeSessionProfile FromStored(StoredSessionProfile profile)
	{
		string poeSessionId;
		try
		{
			poeSessionId = _protector.Unprotect(profile.ProtectedPoeSessionId);
		}
		catch
		{
			poeSessionId = string.Empty;
			ProfilesRequiringPoeSessionReentry++;
		}
		return new PoeSessionProfile((profile.Id == Guid.Empty) ? Guid.NewGuid() : profile.Id, NormalizeSeedName(profile.Name), poeSessionId, profile.CreatedAt, profile.UpdatedAt);
	}

	private void Save()
	{
		SessionProfileDocument value = new SessionProfileDocument(1, ActiveProfileId, _profiles.Select((PoeSessionProfile profile) => new StoredSessionProfile(profile.Id, profile.Name, _protector.Protect(profile.PoeSessionId), profile.CreatedAt, profile.UpdatedAt)).ToList());
		ProfileStorage.WriteAtomic(_path, JsonSerializer.Serialize(value, _json), _backupPath);
	}

	private SessionProfileDocument? TryRead(string path)
	{
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			return JsonSerializer.Deserialize<SessionProfileDocument>(File.ReadAllText(path), _json);
		}
		catch
		{
			return null;
		}
	}

	private static string NormalizeSeedName(string? name)
	{
		if (!string.IsNullOrWhiteSpace(name))
		{
			return name.Trim();
		}
		return "Recovered session";
	}
}
