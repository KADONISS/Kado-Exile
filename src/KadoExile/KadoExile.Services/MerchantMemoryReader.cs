using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using KadoExile.Core;

namespace KadoExile.Services;

public sealed class MerchantMemoryReader(GameController gameController)
{
	public MerchantSnapshot Capture()
	{
		IngameState ingameState = gameController.Game.IngameState;
		IngameUIElements ingameUi = ingameState.IngameUi;
		(int, int, int, bool, bool, IReadOnlyDictionary<string, int>) tuple = ReadPlayerInventory(ingameState);
		if (TryReadOfflineMerchant(ingameUi.OfflineMerchantPanel, out IReadOnlyList<NormalInventoryItem> items))
		{
			return CreateSnapshot(MerchantWindowKind.OfflineMerchantPanel, ((RemoteMemoryObject)ingameUi.OfflineMerchantPanel).Address, items, tuple);
		}
		PurchaseWindow purchaseWindow = ingameUi.PurchaseWindow;
		if (TryReadPurchaseWindow(purchaseWindow, out IReadOnlyList<NormalInventoryItem> items2))
		{
			return CreateSnapshot(MerchantWindowKind.PurchaseWindow, ((RemoteMemoryObject)purchaseWindow).Address, items2, tuple);
		}
		PurchaseWindow purchaseWindowHideout = ingameUi.PurchaseWindowHideout;
		if (TryReadPurchaseWindow(purchaseWindowHideout, out IReadOnlyList<NormalInventoryItem> items3))
		{
			return CreateSnapshot(MerchantWindowKind.PurchaseWindowHideout, ((RemoteMemoryObject)purchaseWindowHideout).Address, items3, tuple);
		}
		return new MerchantSnapshot(DateTimeOffset.UtcNow, MerchantWindowKind.None, 0L,
			Array.Empty<MerchantItemSnapshot>(), tuple.Item1, tuple.Item2, tuple.Item3,
			tuple.Item4, tuple.Item5, tuple.Item6);
	}

	private static bool TryReadOfflineMerchant(StashElement panel, out IReadOnlyList<NormalInventoryItem> items)
	{
		items = Array.Empty<NormalInventoryItem>();
		if (panel != null && ((Element)panel).IsVisible)
		{
			Inventory visibleStash = panel.VisibleStash;
			if (visibleStash != null && ((Element)visibleStash).IsVisible)
			{
				items = visibleStash.VisibleInventoryItems?.ToArray() ?? Array.Empty<NormalInventoryItem>();
				return true;
			}
		}
		return false;
	}

	private static bool TryReadPurchaseWindow(PurchaseWindow window, out IReadOnlyList<NormalInventoryItem> items)
	{
		items = Array.Empty<NormalInventoryItem>();
		if (window != null && ((Element)window).IsVisible)
		{
			VendorStashTabContainer tabContainer = window.TabContainer;
			if (tabContainer != null)
			{
				Inventory visibleStash = ((StashTabContainer)tabContainer).VisibleStash;
				if (visibleStash != null && ((Element)visibleStash).IsVisible)
				{
					items = visibleStash.VisibleInventoryItems?.ToArray() ?? Array.Empty<NormalInventoryItem>();
					return true;
				}
			}
		}
		return false;
	}

	private static MerchantSnapshot CreateSnapshot(MerchantWindowKind kind, long windowAddress, IReadOnlyList<NormalInventoryItem> sourceItems, (int OccupiedCells, int ItemCount, int StackTotal, bool Visible, bool Available, IReadOnlyDictionary<string, int> CurrencyAmounts) inventoryState)
	{
		MerchantItemSnapshot[] items = (from item in sourceItems.Where(delegate(NormalInventoryItem item)
			{
				if (item != null && ((Element)item).IsValid && ((Element)item).IsVisible)
				{
					Entity item2 = item.Item;
					if (item2 != null)
					{
						return item2.IsValid;
					}
					return false;
				}
				return false;
			})
			select new MerchantItemSnapshot(((RemoteMemoryObject)item).Address, item.Item.InventoryId, GetItemName(item.Item), BuildSearchText(item.Item), ((Element)item).isHighlighted, ((Element)item).IsSaturated, ((Element)item).IsActive, ((Element)item).isHighlighted, ((Element)item).GetClientRectCache)).ToArray();
		return new MerchantSnapshot(DateTimeOffset.UtcNow, kind, windowAddress, items, inventoryState.OccupiedCells, inventoryState.ItemCount, inventoryState.StackTotal, inventoryState.Visible, inventoryState.Available, inventoryState.CurrencyAmounts);
	}

	private static (int OccupiedCells, int ItemCount, int StackTotal, bool Visible, bool Available, IReadOnlyDictionary<string, int> CurrencyAmounts) ReadPlayerInventory(IngameState state)
	{
		bool isVisible = ((Element)state.IngameUi.InventoryPanel).IsVisible;
		try
		{
			InventoryHolder? holder = state.ServerData.PlayerInventories?
				.FirstOrDefault(item => item != null && item.TypeId == InventoryNameE.MainInventory1);
			ServerInventory? inventory = holder?.Inventory;
			IList<ServerInventory.InventSlotItem>? list = inventory?.InventorySlotItems;

			// The updated API may return another equipment inventory first, or a corrupt
			// InventoryHolder array while offsets are stale. Never interpret that as zero currency.
			if (holder == null || holder.Address == 0 || inventory == null || inventory.Address == 0 ||
				inventory.Columns != 12 || inventory.Rows != 5 || list == null || list.Count > 60 ||
				inventory.ItemCount < 0 || inventory.ItemCount > 60 ||
				(inventory.ItemCount > 0 && list.Count == 0))
			{
				return UnavailableInventory(isVisible);
			}

			bool[,] occupied = new bool[12, 5];
			Dictionary<string, int> currencyAmounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (ServerInventory.InventSlotItem item in list)
			{
				if (item == null || item.Item == null || !item.Item.IsValid || item.PosX < 0 || item.PosY < 0 ||
					item.SizeX <= 0 || item.SizeY <= 0 || item.PosX + item.SizeX > 12 || item.PosY + item.SizeY > 5)
				{
					return UnavailableInventory(isVisible);
				}

				for (int x = item.PosX; x < item.PosX + item.SizeX; x++)
				{
					for (int y = item.PosY; y < item.PosY + item.SizeY; y++)
					{
						occupied[x, y] = true;
					}
				}

				Stack component = item.Item.GetComponent<Stack>();
				if (component != null)
				{
					string key = InventorySafetyEvaluator.NormalizeItemName(GetItemName(item.Item));
					currencyAmounts[key] = currencyAmounts.GetValueOrDefault(key) + component.Size;
				}
			}

			int stackTotal = list.Sum(item => item.Item.GetComponent<Stack>()?.Size ?? 1);
			return (occupied.Cast<bool>().Count(value => value), list.Count, stackTotal, isVisible, true, currencyAmounts);
		}
		catch
		{
			return UnavailableInventory(isVisible);
		}
	}

	private static (int OccupiedCells, int ItemCount, int StackTotal, bool Visible, bool Available, IReadOnlyDictionary<string, int> CurrencyAmounts) UnavailableInventory(bool isVisible)
	{
		return (0, 0, 0, isVisible, false, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
	}

	private static string GetItemName(Entity entity)
	{
		Mods component = entity.GetComponent<Mods>();
		if (!string.IsNullOrWhiteSpace((component != null) ? component.UniqueName : null))
		{
			return component.UniqueName;
		}
		Base component2 = entity.GetComponent<Base>();
		return ((component2 != null) ? component2.Name : null) ?? entity.RenderName ?? entity.Metadata ?? "Unknown item";
	}

	private static string BuildSearchText(Entity entity)
	{
		StringBuilder builder = new StringBuilder();
		Add(GetItemName(entity));
		Add(entity.RenderName);
		Add(entity.Metadata);
		Mods component = entity.GetComponent<Mods>();
		if (component != null)
		{
			StringBuilder stringBuilder = builder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder);
			handler.AppendLiteral("Item Level: ");
			handler.AppendFormatted(component.ItemLevel);
			stringBuilder.AppendLine(ref handler);
			foreach (string item in component.HumanStats ?? new List<string>())
			{
				Add(item);
			}
			foreach (string item2 in component.HumanCraftedStats ?? new List<string>())
			{
				Add(item2);
			}
			foreach (string item3 in component.HumanImpStats ?? new List<string>())
			{
				Add(item3);
			}
		}
		return builder.ToString();
		void Add(string? value)
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				builder.AppendLine(value);
			}
		}
	}
}
