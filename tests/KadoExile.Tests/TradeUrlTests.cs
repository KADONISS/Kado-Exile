using KadoExile.Services;
using Xunit;

namespace KadoExile.Tests;

public sealed class TradeUrlTests
{
    [Fact]
    public void ParsesPoeOneTradeSearch()
    {
        Assert.True(PoeTradeClient.TryParseTradeUrl(
            "https://www.pathofexile.com/trade/search/Keepers/abc123",
            out var context,
            out var error), error);
        Assert.Equal("Keepers", context!.League);
        Assert.Equal("abc123", context.QueryId);
    }

    [Fact]
    public void RejectsNonPoeHost()
    {
        Assert.False(PoeTradeClient.TryParseTradeUrl(
            "https://example.com/trade/search/Keepers/abc123",
            out _,
            out _));
    }

    [Theory]
    [InlineData("www.pathofexile.com/trade/search/Allflame/abc123")]
    [InlineData("\"https://www.pathofexile.com/trade/search/Allflame/abc123\"")]
    [InlineData("\u200Bhttps://www.pathofexile.com/trade/search/Allflame/abc123")]
    public void NormalizesCommonCopiedTradeLinks(string value)
    {
        Assert.True(PoeTradeClient.TryParseTradeUrl(value, out var context, out var error), error);
        Assert.Equal("Allflame", context!.League);
        Assert.Equal("abc123", context.QueryId);
    }
}
