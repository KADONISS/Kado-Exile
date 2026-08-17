using System;
using System.Collections.Generic;

namespace KadoExile.Core;

public sealed record MerchantSnapshot(DateTimeOffset CapturedAt, MerchantWindowKind Window, long WindowAddress, IReadOnlyList<MerchantItemSnapshot> Items, int OccupiedInventoryCells, int InventoryItemCount, int InventoryStackTotal, bool InventoryVisible, bool InventoryMemoryAvailable = false, IReadOnlyDictionary<string, int>? CurrencyAmounts = null)
{
	public static MerchantSnapshot Closed { get; } = new MerchantSnapshot(DateTimeOffset.MinValue, MerchantWindowKind.None, 0L, Array.Empty<MerchantItemSnapshot>(), 0, 0, 0, InventoryVisible: false, InventoryMemoryAvailable: false, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

	public bool IsOpen => Window != MerchantWindowKind.None;
}
