using KadoExile.Models;
using KadoExile.Services;
using Xunit;

namespace KadoExile.Tests;

public sealed class ProfileStoreTests
{
    [Fact]
    public void DpapiRoundTripDoesNotExposePlainText()
    {
        const string secret = "test-poesessid-secret";
        var protector = new DpapiSecretProtector();

        var encrypted = protector.Protect(secret);

        Assert.NotEqual(secret, encrypted);
        Assert.Equal(secret, protector.Unprotect(encrypted));
    }

    [Fact]
    public void MigratesLegacySettingsToDefaultAndEncryptsSecretAtRest()
    {
        WithStore((directory, store) =>
        {
            var legacy = Profile("Legacy", "secret-value", "query-one", "Crossing");

            store.LoadOrMigrate(legacy);

            Assert.Single(store.Profiles);
            Assert.Equal("Default", store.ActiveProfile.Name);
            Assert.Equal("secret-value", store.ActiveProfile.PoeSessionId);
            var json = File.ReadAllText(Path.Combine(directory, "profiles.json"));
            Assert.DoesNotContain("secret-value", json, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CreatesUpdatesRenamesDuplicatesSelectsAndDeletesProfiles()
    {
        WithStore((_, store) =>
        {
            store.LoadOrMigrate(Profile("Default", "one", "query-one", "One"));
            var created = store.Create(
                "Maps", "two", TradeUrl("query-two"), "Two");
            var updated = store.Update(
                created.Id, "Maps", "three", TradeUrl("query-three"), "Three");
            var renamed = store.Rename(updated.Id, "Charts");
            var duplicate = store.Duplicate(renamed.Id, "Charts copy");
            store.Select(renamed.Id);

            Assert.Equal("Charts", store.ActiveProfile.Name);
            Assert.Equal("query-three", GetQuery(store.ActiveProfile.TradeUrl));
            Assert.Equal("Three", store.ActiveProfile.ItemRegex);
            Assert.Equal("Charts copy", duplicate.Name);

            store.Delete(renamed.Id);
            Assert.DoesNotContain(store.Profiles, profile => profile.Id == renamed.Id);
            Assert.NotEqual(renamed.Id, store.ActiveProfileId);
        });
    }

    [Fact]
    public void CreateAddsDistinctProfileAndPreservesOriginalAfterRestart()
    {
        WithStore((directory, store) =>
        {
            store.LoadOrMigrate(Profile("Default", "one", "query-one", "One"));
            var original = store.ActiveProfile.Snapshot();

            var created = store.Create(
                "Second", "two", TradeUrl("query-two"), "Two");

            Assert.Equal(2, store.Profiles.Count);
            Assert.NotEqual(original.Id, created.Id);
            Assert.Equal(created.Id, store.ActiveProfileId);
            Assert.Contains(store.Profiles, profile => profile == original);

            var reloaded = new ProfileStore(directory, new DpapiSecretProtector());
            reloaded.LoadOrMigrate(Profile("Legacy", "fallback", "fallback", "Fallback"));

            Assert.Equal(2, reloaded.Profiles.Count);
            Assert.Contains(reloaded.Profiles, profile =>
                profile.Id == original.Id &&
                profile.Name == original.Name &&
                profile.TradeUrl == original.TradeUrl &&
                profile.ItemRegex == original.ItemRegex);
            Assert.Equal(created.Id, reloaded.ActiveProfileId);
        });
    }

    [Fact]
    public void RejectedCreateDoesNotMutateExistingProfile()
    {
        WithStore((_, store) =>
        {
            store.LoadOrMigrate(Profile("Default", "one", "query-one", "One"));
            var original = store.ActiveProfile.Snapshot();

            Assert.Throws<ArgumentException>(() =>
                store.Create("default", "two", TradeUrl("query-two"), "Two"));

            Assert.Single(store.Profiles);
            Assert.Equal(original, store.ActiveProfile);
        });
    }

    [Fact]
    public void RejectsEmptyAndCaseInsensitiveDuplicateNames()
    {
        WithStore((_, store) =>
        {
            store.LoadOrMigrate(Profile("Default", "one", "query-one", "One"));

            Assert.Throws<ArgumentException>(() =>
                store.Create(" ", "two", TradeUrl("query-two"), "Two"));
            Assert.Throws<ArgumentException>(() =>
                store.Create("default", "two", TradeUrl("query-two"), "Two"));
        });
    }

    [Fact]
    public void RecoversLastBackupWhenMainFileIsCorrupt()
    {
        WithStore((directory, store) =>
        {
            var legacy = Profile("Default", "one", "query-one", "One");
            store.LoadOrMigrate(legacy);
            store.Create("Second", "two", TradeUrl("query-two"), "Two");
            File.WriteAllText(Path.Combine(directory, "profiles.json"), "{broken-json");

            var recovered = new ProfileStore(directory, new DpapiSecretProtector());
            recovered.LoadOrMigrate(legacy);

            Assert.True(recovered.RecoveredFromBackup);
            Assert.Single(recovered.Profiles);
            Assert.Equal("Default", recovered.ActiveProfile.Name);
            Assert.DoesNotContain("broken-json",
                File.ReadAllText(Path.Combine(directory, "profiles.json")),
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ExportOmitsPoeSessionIdAndSnapshotDoesNotChangeAfterUpdate()
    {
        WithStore((directory, store) =>
        {
            store.LoadOrMigrate(Profile("Default", "top-secret", "query-one", "One"));
            var snapshot = store.ActiveProfile.Snapshot();
            store.Update(
                snapshot.Id, snapshot.Name, "new-secret", TradeUrl("query-two"), "Two");
            var exportPath = Path.Combine(directory, "export.json");

            store.ExportWithoutSecrets(exportPath);

            Assert.Equal("top-secret", snapshot.PoeSessionId);
            Assert.Equal("One", snapshot.ItemRegex);
            var export = File.ReadAllText(exportPath);
            Assert.DoesNotContain("top-secret", export, StringComparison.Ordinal);
            Assert.DoesNotContain("new-secret", export, StringComparison.Ordinal);
            Assert.DoesNotContain("PoeSessionId", export, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ScrubsPoeSessionIdFromLegacySettingsFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "KadoExileProfileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "KadoExile_settings.json");
        try
        {
            File.WriteAllText(path,
                "{\"TradeUrl\":{\"Value\":\"url\"},\"PoeSessionId\":{\"Value\":\"legacy-secret\"}}");

            Assert.True(LegacySettingsScrubber.ScrubPoeSessionId(path));

            var json = File.ReadAllText(path);
            Assert.DoesNotContain("legacy-secret", json, StringComparison.Ordinal);
            Assert.Contains("PoeSessionId", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void PreservesProfileWhenDpapiCannotBeDecrypted()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "KadoExileProfileTests", Guid.NewGuid().ToString("N"));
        try
        {
            var legacy = Profile("Default", "secret-value", "query-one", "Crossing");
            var writer = new ProfileStore(directory, new PrefixProtector());
            writer.LoadOrMigrate(legacy);

            var reader = new ProfileStore(directory, new UnavailableProtector());
            reader.LoadOrMigrate(Profile("Legacy", "", "fallback-query", "Fallback"));

            Assert.Single(reader.Profiles);
            Assert.Equal("query-one", GetQuery(reader.ActiveProfile.TradeUrl));
            Assert.Equal("Crossing", reader.ActiveProfile.ItemRegex);
            Assert.Empty(reader.ActiveProfile.PoeSessionId);
            Assert.Equal(1, reader.ProfilesRequiringPoeSessionReentry);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void InvalidMainAndBackupFilesCreateSafeDefaultProfile()
    {
        WithStore((directory, _) =>
        {
            File.WriteAllText(Path.Combine(directory, "profiles.json"), "{broken");
            File.WriteAllText(Path.Combine(directory, "profiles.json.bak"), "{also-broken");

            var store = new ProfileStore(directory, new PrefixProtector());
            store.LoadOrMigrate(Profile("Legacy", "secret", "query-fallback", "Fallback"));

            Assert.Single(store.Profiles);
            Assert.Equal("Default", store.ActiveProfile.Name);
            Assert.Equal("query-fallback", GetQuery(store.ActiveProfile.TradeUrl));
        });
    }

    private static void WithStore(Action<string, ProfileStore> test)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "KadoExileProfileTests", Guid.NewGuid().ToString("N"));
        try
        {
            test(directory, new ProfileStore(directory, new DpapiSecretProtector()));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static TradeProfile Profile(
        string name,
        string secret,
        string query,
        string regex)
    {
        var now = DateTimeOffset.UtcNow;
        return new TradeProfile(
            Guid.NewGuid(), name, secret, TradeUrl(query), regex, now, now);
    }

    private static string TradeUrl(string query) =>
        $"https://www.pathofexile.com/trade/search/Allflame/{query}";

    private static string GetQuery(string url) =>
        new Uri(url).Segments.Last().Trim('/');

    private sealed class PrefixProtector : ISecretProtector
    {
        public string Protect(string value) => "protected:" + value;
        public string Unprotect(string value) => value.StartsWith("protected:", StringComparison.Ordinal)
            ? value["protected:".Length..]
            : throw new InvalidOperationException("Cannot decrypt");
    }

    private sealed class UnavailableProtector : ISecretProtector
    {
        public string Protect(string value) => "protected:" + value;
        public string Unprotect(string value) => throw new InvalidOperationException("DPAPI unavailable");
    }
}
