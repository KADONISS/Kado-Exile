using KadoExile.Models;
using KadoExile.Services;
using Xunit;

namespace KadoExile.Tests;

public sealed class SplitProfileStoreTests
{
    [Fact]
    public void MigratesCombinedProfilesIntoIndependentStores()
    {
        WithDirectory(directory =>
        {
            var legacy = Legacy("Maps", "secret", "query-one", "Crossing");
            var sessions = new PoeSessionProfileStore(directory, new PrefixProtector());
            var trades = new TradeSearchProfileStore(directory);

            sessions.LoadOrSeed([legacy], legacy.Id);
            trades.LoadOrSeed([legacy], legacy.Id);

            Assert.Single(sessions.Profiles);
            Assert.Single(trades.Profiles);
            Assert.Equal("secret", sessions.ActiveProfile.PoeSessionId);
            Assert.Equal("query-one", new Uri(trades.ActiveProfile.TradeUrl).Segments.Last().Trim('/'));
            Assert.Equal("Crossing", trades.ActiveProfile.ItemRegex);
            Assert.DoesNotContain("secret", File.ReadAllText(Path.Combine(directory, "session-profiles.json")));
        });
    }

    [Fact]
    public void SessionAndTradeSelectionsAreIndependentAfterRestart()
    {
        WithDirectory(directory =>
        {
            var legacy = Legacy("Default", "one", "query-one", "One");
            var sessions = new PoeSessionProfileStore(directory, new PrefixProtector());
            var trades = new TradeSearchProfileStore(directory);
            sessions.LoadOrSeed([legacy], legacy.Id);
            trades.LoadOrSeed([legacy], legacy.Id);
            var secondSession = sessions.Create("Account two", "two");
            var secondTrade = trades.Create("Search two", TradeUrl("query-two"), "Two");
            sessions.Select(legacy.Id);
            trades.Select(secondTrade.Id);

            var reloadedSessions = new PoeSessionProfileStore(directory, new PrefixProtector());
            var reloadedTrades = new TradeSearchProfileStore(directory);
            reloadedSessions.LoadOrSeed([], null);
            reloadedTrades.LoadOrSeed([], null);

            Assert.Equal(legacy.Id, reloadedSessions.ActiveProfileId);
            Assert.Equal(secondTrade.Id, reloadedTrades.ActiveProfileId);
            Assert.Contains(reloadedSessions.Profiles, profile => profile.Id == secondSession.Id);
        });
    }

    private static TradeProfile Legacy(string name, string secret, string query, string regex)
    {
        var now = DateTimeOffset.UtcNow;
        return new TradeProfile(Guid.NewGuid(), name, secret, TradeUrl(query), regex, now, now);
    }

    private static string TradeUrl(string query) =>
        $"https://www.pathofexile.com/trade/search/Allflame/{query}";

    private static void WithDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "KadoExileSplitProfileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class PrefixProtector : ISecretProtector
    {
        public string Protect(string value) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));
        public string Unprotect(string value) =>
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }
}
