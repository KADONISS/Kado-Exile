using System;
using System.Collections.Generic;

namespace KadoExile.Core;

public static class InventorySafetyEvaluator
{
	private static readonly Dictionary<string, string> CurrencyAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["chaos"] = "chaos orb",
		["divine"] = "divine orb",
		["exalted"] = "exalted orb",
		["exalt"] = "exalted orb",
		["alchemy"] = "orb of alchemy",
		["alch"] = "orb of alchemy",
		["fusing"] = "orb of fusing",
		["chance"] = "orb of chance",
		["scouring"] = "orb of scouring",
		["regret"] = "orb of regret",
		["regal"] = "regal orb",
		["vaal"] = "vaal orb",
		["chisel"] = "cartographer's chisel",
		["gcp"] = "gemcutter's prism",
		["mirror"] = "mirror of kalandra"
	};

	public static InventorySafetyCheck Evaluate(MerchantSnapshot snapshot, decimal? unitPrice, string? currency)
	{
		if (!snapshot.InventoryMemoryAvailable)
		{
			return new InventorySafetyCheck(AutomationStopReason.None, "Inventory memory unavailable", null, unitPrice);
		}
		if (snapshot.OccupiedInventoryCells >= 60)
		{
			return new InventorySafetyCheck(AutomationStopReason.InventoryFull, "Player inventory is full", null, null);
		}
		if (!unitPrice.HasValue || !(unitPrice.GetValueOrDefault() > 0m) || string.IsNullOrWhiteSpace(currency))
		{
			return new InventorySafetyCheck(AutomationStopReason.None, "Listing price is unavailable", null, unitPrice);
		}
		if (!TryResolveCurrencyName(currency, out string currencyName))
		{
			return new InventorySafetyCheck(AutomationStopReason.None, "Unsupported currency id: " + currency, null, unitPrice);
		}
		IReadOnlyDictionary<string, int> currencyAmounts = snapshot.CurrencyAmounts;
		int value;
		int num = ((currencyAmounts != null && currencyAmounts.TryGetValue(currencyName, out value)) ? value : 0);
		if ((decimal)num < unitPrice.Value)
		{
			return new InventorySafetyCheck(AutomationStopReason.None, "Currency exhaustion check disabled", num, unitPrice);
		}
		return new InventorySafetyCheck(AutomationStopReason.None, "Inventory and currency available", num, unitPrice);
	}

	public static string NormalizeItemName(string value)
	{
		return string.Join(' ', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
	}

	public static bool TryResolveCurrencyName(string currency, out string currencyName)
	{
		string text = NormalizeItemName(currency);
		if (CurrencyAliases.TryGetValue(text, out currencyName))
		{
			return true;
		}
		if (text.Contains("orb", StringComparison.OrdinalIgnoreCase) || text.Contains("prism", StringComparison.OrdinalIgnoreCase) || text.Contains("chisel", StringComparison.OrdinalIgnoreCase))
		{
			currencyName = text;
			return true;
		}
		currencyName = string.Empty;
		return false;
	}
}
