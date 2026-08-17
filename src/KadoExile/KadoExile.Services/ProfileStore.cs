using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using KadoExile.Models;

namespace KadoExile.Services;

public sealed class ProfileStore
{
	private sealed record ProfileDocument(int Version, Guid? ActiveProfileId, List<StoredTradeProfile>? Profiles);

	private sealed record StoredTradeProfile(Guid Id, string Name, string ProtectedPoeSessionId, string? TradeUrl, string? ItemRegex, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

	private readonly string _path;

	private readonly string _backupPath;

	private readonly ISecretProtector _protector;

	private readonly JsonSerializerOptions _json = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private List<TradeProfile> _profiles = new List<TradeProfile>();

	public IReadOnlyList<TradeProfile> Profiles => _profiles;

	public Guid? ActiveProfileId { get; private set; }

	public bool RecoveredFromBackup { get; private set; }

	public int ProfilesRequiringPoeSessionReentry { get; private set; }

	public TradeProfile ActiveProfile => _profiles.FirstOrDefault(delegate(TradeProfile profile)
	{
		Guid id = profile.Id;
		Guid? activeProfileId = ActiveProfileId;
		return id == activeProfileId;
	}) ?? _profiles.FirstOrDefault() ?? throw new InvalidOperationException("No trade profiles are available.");

	public ProfileStore(string configDirectory, ISecretProtector protector)
	{
		Directory.CreateDirectory(configDirectory);
		_path = Path.Combine(configDirectory, "profiles.json");
		_backupPath = _path + ".bak";
		_protector = protector;
	}

	public bool TryGetActiveProfile(out TradeProfile? profile)
	{
		profile = _profiles.FirstOrDefault(delegate(TradeProfile item)
		{
			Guid id = item.Id;
			Guid? activeProfileId = ActiveProfileId;
			return id == activeProfileId;
		}) ?? _profiles.FirstOrDefault();
		return (object)profile != null;
	}

	public void LoadOrMigrate(TradeProfile legacyProfile)
	{
		RecoveredFromBackup = false;
		ProfilesRequiringPoeSessionReentry = 0;
		ProfileDocument document = null;
		if (File.Exists(_path))
		{
			if (!TryReadDocument(_path, out document) && File.Exists(_backupPath) && TryReadDocument(_backupPath, out document))
			{
				RecoveredFromBackup = true;
			}
		}
		else if (File.Exists(_backupPath) && TryReadDocument(_backupPath, out document))
		{
			RecoveredFromBackup = true;
		}
		List<StoredTradeProfile> list = document?.Profiles;
		if (list == null || list.Count <= 0)
		{
			DateTimeOffset utcNow = DateTimeOffset.UtcNow;
			TradeProfile tradeProfile = legacyProfile with
			{
				Id = ((legacyProfile.Id == Guid.Empty) ? Guid.NewGuid() : legacyProfile.Id),
				Name = "Default",
				CreatedAt = ((legacyProfile.CreatedAt == default(DateTimeOffset)) ? utcNow : legacyProfile.CreatedAt),
				UpdatedAt = utcNow
			};
			int num = 1;
			List<TradeProfile> list2 = new List<TradeProfile>(num);
			CollectionsMarshal.SetCount(list2, num);
			Span<TradeProfile> span = CollectionsMarshal.AsSpan(list2);
			int index = 0;
			span[index] = tradeProfile;
			_profiles = list2;
			ActiveProfileId = tradeProfile.Id;
			Save();
		}
		else
		{
			_profiles = document.Profiles.Select(FromStored).ToList();
			ActiveProfileId = (_profiles.Any(delegate(TradeProfile profile)
			{
				Guid id = profile.Id;
				Guid? activeProfileId = document.ActiveProfileId;
				return id == activeProfileId;
			}) ? document.ActiveProfileId : new Guid?(_profiles[0].Id));
			if (RecoveredFromBackup)
			{
				File.Copy(_backupPath, _path, overwrite: true);
			}
		}
	}

	public TradeProfile Select(Guid id)
	{
		TradeProfile tradeProfile = Find(id);
		ActiveProfileId = tradeProfile.Id;
		Save();
		return tradeProfile;
	}

	public TradeProfile Create(string name, string poeSessionId, string tradeUrl, string itemRegex)
	{
		string name2 = ValidateUniqueName(name, null);
		int count = _profiles.Count;
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		Guid id;
		do
		{
			id = Guid.NewGuid();
		}
		while (_profiles.Any((TradeProfile profile) => profile.Id == id));
		TradeProfile tradeProfile = new TradeProfile(id, name2, poeSessionId, tradeUrl, itemRegex, utcNow, utcNow);
		_profiles.Add(tradeProfile);
		if (_profiles.Count != count + 1)
		{
			throw new InvalidOperationException("Profile creation did not increase the profile count.");
		}
		ActiveProfileId = tradeProfile.Id;
		Save();
		return tradeProfile;
	}

	public TradeProfile Update(Guid id, string name, string poeSessionId, string tradeUrl, string itemRegex)
	{
		TradeProfile tradeProfile = Find(id);
		string name2 = ValidateUniqueName(name, id);
		TradeProfile tradeProfile2 = tradeProfile with
		{
			Name = name2,
			PoeSessionId = poeSessionId,
			TradeUrl = tradeUrl,
			ItemRegex = itemRegex,
			UpdatedAt = DateTimeOffset.UtcNow
		};
		Replace(tradeProfile2);
		Save();
		return tradeProfile2;
	}

	public TradeProfile Rename(Guid id, string name)
	{
		TradeProfile tradeProfile = Find(id)with
		{
			Name = ValidateUniqueName(name, id),
			UpdatedAt = DateTimeOffset.UtcNow
		};
		Replace(tradeProfile);
		Save();
		return tradeProfile;
	}

	public TradeProfile Duplicate(Guid id, string name)
	{
		TradeProfile tradeProfile = Find(id);
		return Create(name, tradeProfile.PoeSessionId, tradeProfile.TradeUrl, tradeProfile.ItemRegex);
	}

	public TradeProfile Delete(Guid id)
	{
		if (_profiles.Count <= 1)
		{
			throw new InvalidOperationException("At least one profile must remain.");
		}
		TradeProfile tradeProfile = Find(id);
		_profiles.Remove(tradeProfile);
		if (ActiveProfileId == id)
		{
			ActiveProfileId = _profiles[0].Id;
		}
		Save();
		return tradeProfile;
	}

	public void ExportWithoutSecrets(string path)
	{
		var value = _profiles.Select((TradeProfile profile) => new { profile.Id, profile.Name, profile.TradeUrl, profile.ItemRegex, profile.CreatedAt, profile.UpdatedAt });
		WriteAtomic(path, JsonSerializer.Serialize(value, _json), path + ".bak");
	}

	private TradeProfile Find(Guid id)
	{
		return _profiles.FirstOrDefault((TradeProfile profile) => profile.Id == id) ?? throw new KeyNotFoundException("Profile was not found.");
	}

	private void Replace(TradeProfile profile)
	{
		int index = _profiles.FindIndex((TradeProfile item) => item.Id == profile.Id);
		_profiles[index] = profile;
	}

	private string ValidateUniqueName(string name, Guid? currentId)
	{
		string normalized = name.Trim();
		if (normalized.Length == 0)
		{
			throw new ArgumentException("Profile name is empty.", "name");
		}
		if (_profiles.Any(delegate(TradeProfile profile)
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

	private void Save()
	{
		ProfileDocument value = new ProfileDocument(1, ActiveProfileId, _profiles.Select(ToStored).ToList());
		WriteAtomic(_path, JsonSerializer.Serialize(value, _json), _backupPath);
	}

	private ProfileDocument ReadDocument(string path)
	{
		return JsonSerializer.Deserialize<ProfileDocument>(File.ReadAllText(path), _json) ?? throw new InvalidDataException("Profile file is empty.");
	}

	private bool TryReadDocument(string path, out ProfileDocument? document)
	{
		try
		{
			document = ReadDocument(path);
			return document.Profiles != null;
		}
		catch
		{
			document = null;
			return false;
		}
	}

	private StoredTradeProfile ToStored(TradeProfile profile)
	{
		return new StoredTradeProfile(profile.Id, profile.Name, _protector.Protect(profile.PoeSessionId), profile.TradeUrl, profile.ItemRegex, profile.CreatedAt, profile.UpdatedAt);
	}

	private TradeProfile FromStored(StoredTradeProfile profile)
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
		return new TradeProfile((profile.Id == Guid.Empty) ? Guid.NewGuid() : profile.Id, string.IsNullOrWhiteSpace(profile.Name) ? "Recovered profile" : profile.Name, poeSessionId, profile.TradeUrl ?? string.Empty, profile.ItemRegex ?? string.Empty, profile.CreatedAt, profile.UpdatedAt);
	}

	private static void WriteAtomic(string path, string content, string backupPath)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		string text = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(text, content);
			if (File.Exists(path))
			{
				File.Replace(text, path, backupPath, ignoreMetadataErrors: true);
				return;
			}
			File.Move(text, path);
			File.Copy(path, backupPath, overwrite: true);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}
}
