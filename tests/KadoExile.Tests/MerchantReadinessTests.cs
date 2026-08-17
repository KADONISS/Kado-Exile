using KadoExile.Core;
using SharpDX;
using Xunit;

namespace KadoExile.Tests;

public sealed class MerchantReadinessTests
{
    [Fact]
    public void ItemCountChangesResetMerchantStability()
    {
        var tracker = new MerchantStabilityTracker(3);
        var first = Snapshot(40, 1);
        var loaded = Snapshot(140, 1);

        Assert.False(tracker.Observe(first));
        Assert.Equal(1, tracker.StableReads);
        Assert.False(tracker.Observe(loaded));
        Assert.Equal(1, tracker.StableReads);
        Assert.False(tracker.Observe(loaded));
        Assert.Equal(2, tracker.StableReads);
        Assert.True(tracker.Observe(loaded));
    }

    [Fact]
    public void InventoryMemoryUnavailableDoesNotBlockMerchantStability()
    {
        var tracker = new MerchantStabilityTracker(2);
        var unavailable = Snapshot(40, 1) with { InventoryMemoryAvailable = false };

        Assert.False(tracker.Observe(unavailable));
        Assert.True(tracker.Observe(unavailable));
        Assert.Equal(2, tracker.StableReads);
    }

    [Fact]
    public void AreaReadinessRequiresNewGenerationAndSettleDelay()
    {
        var changed = DateTimeOffset.UtcNow;
        var area = new AreaReadinessSnapshot(4, "New Hideout", changed, true, false, true);

        Assert.False(area.IsReadyAfter(4, changed.AddSeconds(2), TimeSpan.Zero));
        Assert.False(area.IsReadyAfter(3, changed.AddMilliseconds(500), TimeSpan.FromSeconds(1)));
        Assert.True(area.IsReadyAfter(3, changed.AddSeconds(1), TimeSpan.FromSeconds(1)));
    }

    private static MerchantSnapshot Snapshot(int count, long addressSeed) => new(
        DateTimeOffset.UtcNow,
        MerchantWindowKind.PurchaseWindow,
        0x100,
        Enumerable.Range(0, count)
            .Select(index => new MerchantItemSnapshot(
                addressSeed + index + 1,
                (uint)index,
                $"Item {index}",
                $"Item {index}",
                false,
                true,
                true,
                false,
                new RectangleF(index % 12 * 50, index / 12 * 50, 40, 40)))
            .ToArray(),
        0,
        0,
        0,
        true,
        true);
}
