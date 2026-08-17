using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KadoExile.Models;

namespace KadoExile.Services;

public sealed class TradeSearchProfileStore
{
	private sealed record TradeProfileDocument(int Version, Guid? ActiveProfileId, List<TradeSearchProfile>? Profiles);

	private readonly string _path;

	private readonly string _backupPath;

	private readonly JsonSerializerOptions _json = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private List<TradeSearchProfile> _profiles = new List<TradeSearchProfile>();

	public IReadOnlyList<TradeSearchProfile> Profiles => _profiles;

	public Guid? ActiveProfileId { get; private set; }

	public TradeSearchProfile ActiveProfile => _profiles.FirstOrDefault(delegate(TradeSearchProfile profile)
	{
		Guid id = profile.Id;
		Guid? activeProfileId = ActiveProfileId;
		return id == activeProfileId;
	}) ?? _profiles.First();

	public TradeSearchProfileStore(string configDirectory)
	{
		Directory.CreateDirectory(configDirectory);
		_path = Path.Combine(configDirectory, "trade-profiles.json");
		_backupPath = _path + ".bak";
	}

	public void LoadOrSeed(IEnumerable<TradeProfile> legacyProfiles, Guid? legacyActiveId)
	{
		TradeProfileDocument document = TryRead(_path) ?? TryRead(_backupPath);
		List<TradeSearchProfile> list = document?.Profiles;
		if (list != null && list.Count > 0)
		{
			_profiles = document.Profiles;
			ActiveProfileId = (_profiles.Any(delegate(TradeSearchProfile profile)
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
			select new TradeSearchProfile((profile.Id == Guid.Empty) ? Guid.NewGuid() : profile.Id, NormalizeSeedName(profile.Name), profile.TradeUrl, profile.ItemRegex, (profile.CreatedAt == default(DateTimeOffset)) ? now : profile.CreatedAt, now)).ToList();
		if (_profiles.Count == 0)
		{
			_profiles.Add(new TradeSearchProfile(Guid.NewGuid(), "Default", "https://www.pathofexile.com/trade", string.Empty, now, now));
		}
		ActiveProfileId = (_profiles.Any(delegate(TradeSearchProfile profile)
		{
			Guid id = profile.Id;
			Guid? guid = legacyActiveId;
			return id == guid;
		}) ? legacyActiveId : new Guid?(_profiles[0].Id));
		Save();
	}

	public TradeSearchProfile Select(Guid id)
	{
		TradeSearchProfile result = Find(id);
		ActiveProfileId = id;
		Save();
		return result;
	}

	public TradeSearchProfile Create(string name, string tradeUrl, string itemRegex)
	{
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		TradeSearchProfile tradeSearchProfile = new TradeSearchProfile(NewId(), ValidateName(name, null), tradeUrl, itemRegex, utcNow, utcNow);
		_profiles.Add(tradeSearchProfile);
		ActiveProfileId = tradeSearchProfile.Id;
		Save();
		return tradeSearchProfile;
	}

	public TradeSearchProfile Update(Guid id, string name, string tradeUrl, string itemRegex)
	{
		TradeSearchProfile tradeSearchProfile = Find(id)with
		{
			Name = ValidateName(name, id),
			TradeUrl = tradeUrl,
			ItemRegex = itemRegex,
			UpdatedAt = DateTimeOffset.UtcNow
		};
		_profiles[_profiles.FindIndex((TradeSearchProfile profile) => profile.Id == id)] = tradeSearchProfile;
		Save();
		return tradeSearchProfile;
	}

	public TradeSearchProfile Delete(Guid id)
	{
		if (_profiles.Count <= 1)
		{
			throw new InvalidOperationException("At least one Trade profile must remain.");
		}
		TradeSearchProfile tradeSearchProfile = Find(id);
		_profiles.Remove(tradeSearchProfile);
		if (ActiveProfileId == id)
		{
			ActiveProfileId = _profiles[0].Id;
		}
		Save();
		return tradeSearchProfile;
	}

	private TradeSearchProfile Find(Guid id)
	{
		return _profiles.FirstOrDefault((TradeSearchProfile profile) => profile.Id == id) ?? throw new KeyNotFoundException("Trade profile was not found.");
	}

	private string ValidateName(string name, Guid? currentId)
	{
		string normalized = name.Trim();
		if (normalized.Length == 0)
		{
			throw new ArgumentException("Profile name is empty.", "name");
		}
		if (_profiles.Any(delegate(TradeSearchProfile profile)
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
		while (_profiles.Any((TradeSearchProfile profile) => profile.Id == id));
		return id;
	}

	private void Save()
	{
		TradeProfileDocument value = new TradeProfileDocument(1, ActiveProfileId, _profiles);
		ProfileStorage.WriteAtomic(_path, JsonSerializer.Serialize(value, _json), _backupPath);
	}

	private TradeProfileDocument? TryRead(string path)
	{
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			return JsonSerializer.Deserialize<TradeProfileDocument>(File.ReadAllText(path), _json);
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
		return "Recovered trade";
	}
}
