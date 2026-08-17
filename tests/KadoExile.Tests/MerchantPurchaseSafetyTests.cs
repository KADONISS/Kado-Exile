using KadoExile.Core;
using SharpDX;
using Xunit;

namespace KadoExile.Tests;

public sealed class MerchantPurchaseSafetyTests
{
    [Fact]
    public void SelectsHighlightedItemEvenWhenMemoryTextDoesNotMatchGameExpression()
    {
        var unhighlighted = Item(1, "Crossing", false);
        var highlighted = Item(2, "Unrelated memory text", true);

        var selected = MerchantItemSelector.SelectNext(
            [unhighlighted, highlighted],
            true,
            null);

        Assert.Same(highlighted, selected);
    }

    [Fact]
    public void SelectsFirstItemWhenEveryMerchantItemIsHighlighted()
    {
        var items = Enumerable.Range(1, 3)
            .Select(index => Item(index, $"Item {index}", true))
            .ToArray();

        var selected = MerchantItemSelector.SelectNext(items, true, null);

        Assert.Same(items[0], selected);
    }

    [Fact]
    public void NeverSelectsUnhighlightedItemWhenHighlightIsRequired()
    {
        var matchingButUnhighlighted = Item(1, "Crossing", false);

        var selected = MerchantItemSelector.SelectNext(
            [matchingButUnhighlighted],
            true,
            ItemRegexMatcher.Compile("Crossing"));

        Assert.Null(selected);
    }

    private static MerchantItemSnapshot Item(int id, string text, bool highlighted) => new(
        id,
        (uint)id,
        text,
        text,
        highlighted,
        true,
        true,
        highlighted,
        new RectangleF(id * 20, 20, 10, 10));
}
