using KadoExile.Core;
using SharpDX;
using Xunit;

namespace KadoExile.Tests;

public sealed class ItemRegexMatcherTests
{
    [Fact]
    public void MatchesNameAndItemLevelFromMemoryText()
    {
        var item = new MerchantItemSnapshot(
            1, 2, "The Crossing", "The Crossing\nItem Level: 78\n", true, true, true, true,
            new RectangleF(10, 20, 40, 40));

        Assert.True(ItemRegexMatcher.IsMatch(
            ItemRegexMatcher.Compile("Crossing|Item Level: 7[2-9]"), item));
    }

    [Fact]
    public void RejectsNonMatchingItem()
    {
        var item = new MerchantItemSnapshot(
            1, 2, "The Junction", "The Junction\nItem Level: 68\n", true, true, true, true,
            new RectangleF(10, 20, 40, 40));

        Assert.False(ItemRegexMatcher.IsMatch(
            ItemRegexMatcher.Compile("Crossing|Item Level: 7[2-9]"), item));
    }

    [Fact]
    public void RejectsInvalidRegex()
    {
        Assert.ThrowsAny<ArgumentException>(() => ItemRegexMatcher.Compile("["));
    }
}
