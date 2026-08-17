using KadoExile.Core;
using Xunit;

namespace KadoExile.Tests;

public sealed class InventorySafetyEvaluatorTests
{
    [Fact]
    public void InventoryMemoryUnavailableDoesNotStopAutomation()
    {
        var check = InventorySafetyEvaluator.Evaluate(
            Snapshot(60, false, EmptyCurrencies()),
            8,
            "chaos");

        Assert.Equal(AutomationStopReason.None, check.StopReason);
    }

    [Theory]
    [InlineData(60, AutomationStopReason.InventoryFull)]
    [InlineData(59, AutomationStopReason.None)]
    public void StopsOnlyWhenEveryInventoryCellIsOccupied(
        int occupiedCells,
        AutomationStopReason expected)
    {
        var check = InventorySafetyEvaluator.Evaluate(
            Snapshot(occupiedCells, true, new Dictionary<string, int>
            {
                ["chaos orb"] = 100
            }),
            8,
            "chaos");

        Assert.Equal(expected, check.StopReason);
    }

    [Theory]
    [InlineData(8, AutomationStopReason.None)]
    [InlineData(7, AutomationStopReason.None)]
    public void CurrencyBalanceNeverStopsAutomation(
        int balance,
        AutomationStopReason expected)
    {
        var check = InventorySafetyEvaluator.Evaluate(
            Snapshot(10, true, new Dictionary<string, int>
            {
                ["chaos orb"] = balance
            }),
            8,
            "chaos");

        Assert.Equal(expected, check.StopReason);
        Assert.Equal(balance, check.AvailableCurrency);
    }

    [Fact]
    public void MissingKnownCurrencyDoesNotStopAutomation()
    {
        var check = InventorySafetyEvaluator.Evaluate(
            Snapshot(10, true, EmptyCurrencies()),
            1,
            "divine");

        Assert.Equal(AutomationStopReason.None, check.StopReason);
        Assert.Equal(0, check.AvailableCurrency);
    }

    [Fact]
    public void UnknownCurrencyDoesNotCauseFalseStop()
    {
        var check = InventorySafetyEvaluator.Evaluate(
            Snapshot(10, true, EmptyCurrencies()),
            1,
            "future-currency-id");

        Assert.Equal(AutomationStopReason.None, check.StopReason);
    }

    [Theory]
    [InlineData("chaos", "chaos orb")]
    [InlineData("DIVINE", "divine orb")]
    [InlineData("orb of fusing", "orb of fusing")]
    public void ResolvesTradeCurrencyIdsToInventoryNames(string id, string expected)
    {
        Assert.True(InventorySafetyEvaluator.TryResolveCurrencyName(id, out var name));
        Assert.Equal(expected, name);
    }

    private static MerchantSnapshot Snapshot(
        int occupiedCells,
        bool memoryAvailable,
        IReadOnlyDictionary<string, int> currencies) =>
        MerchantSnapshot.Closed with
        {
            CapturedAt = DateTimeOffset.UtcNow,
            OccupiedInventoryCells = occupiedCells,
            InventoryMemoryAvailable = memoryAvailable,
            CurrencyAmounts = currencies
        };

    private static IReadOnlyDictionary<string, int> EmptyCurrencies() =>
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}
