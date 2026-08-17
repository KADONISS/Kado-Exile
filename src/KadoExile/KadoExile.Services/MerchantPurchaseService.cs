using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KadoExile.Core;
using SharpDX;

namespace KadoExile.Services;

public sealed class MerchantPurchaseService(Func<MerchantSnapshot> getSnapshot, Func<Vector2> getWindowOffset, InputController input, KadoLogger log)
{
	public async Task<PurchaseResult> BuyAsync(string regexPattern, int desiredQuantity, bool requireHighlight, decimal? unitPrice, string? currency, TimeSpan merchantTimeout, int clickDelayMilliseconds, int verificationDelayMilliseconds, int recoveryDelayMilliseconds, CancellationToken cancellationToken)
	{
		Regex regex = ((requireHighlight || string.IsNullOrWhiteSpace(regexPattern)) ? null : ItemRegexMatcher.Compile(regexPattern));
		DateTimeOffset deadline = DateTimeOffset.UtcNow + merchantTimeout;
		int purchased = 0;
		MerchantWindowKind lastWindow = MerchantWindowKind.None;
		while (purchased < desiredQuantity)
		{
			cancellationToken.ThrowIfCancellationRequested();
			MerchantSnapshot snapshot = getSnapshot();
			if (!snapshot.IsOpen)
			{
				if (DateTimeOffset.UtcNow >= deadline)
				{
					return new PurchaseResult(desiredQuantity, purchased, "Merchant timeout", lastWindow);
				}
				await Task.Delay(100, cancellationToken);
				continue;
			}
			lastWindow = snapshot.Window;
			log.Detail("WINDOW", $"open={snapshot.Window}, address=0x{snapshot.WindowAddress:X}, items={snapshot.Items.Count}, inventory={snapshot.OccupiedInventoryCells}/60");
			InventorySafetyCheck inventorySafetyCheck = InventorySafetyEvaluator.Evaluate(snapshot, unitPrice, currency);
			if (inventorySafetyCheck.ShouldStop)
			{
				string eventName = ((inventorySafetyCheck.StopReason == AutomationStopReason.InventoryFull) ? "INVENTORY_FULL" : "CURRENCY_EXHAUSTED");
				log.Info(eventName, $"window={snapshot.Window}, occupied={snapshot.OccupiedInventoryCells}/60, currency={currency ?? "unknown"}, available={inventorySafetyCheck.AvailableCurrency?.ToString() ?? "unknown"}, required={inventorySafetyCheck.RequiredCurrency?.ToString() ?? "unknown"}, purchased={purchased}");
				return new PurchaseResult(desiredQuantity, purchased, inventorySafetyCheck.Reason, lastWindow, inventorySafetyCheck.StopReason);
			}
			MerchantItemSnapshot target = MerchantItemSelector.SelectNext(snapshot.Items, requireHighlight, regex);
			if ((object)target == null)
			{
				return new PurchaseResult(desiredQuantity, purchased, (purchased > 0) ? "No more matching items" : "No matching highlighted item", lastWindow);
			}
			string value = ((regex == null) ? "not-evaluated" : ItemRegexMatcher.IsMatch(regex, target).ToString());
			log.Info("SELECT", $"window={snapshot.Window}, item=\"{target.Name}\", address=0x{target.ElementAddress:X}, inventoryId={target.InventoryId}, rawHighlight={target.RawHighlight}, saturated={target.IsSaturated}, active={target.IsActive}, highlighted={target.IsHighlighted}, search=\"{regexPattern}\", matchSource={(requireHighlight ? "ExileAPI.isHighlighted" : "memory-text")}, memoryTextMatched={value}");
			log.Info("CLICK", $"client=({target.Center.X:0.0},{target.Center.Y:0.0}), screenOffset=({getWindowOffset().X:0.0},{getWindowOffset().Y:0.0})");
			int beforeCells = snapshot.OccupiedInventoryCells;
			int beforeItems = snapshot.InventoryItemCount;
			int beforeStacks = snapshot.InventoryStackTotal;
			await input.CtrlClickAsync(target.Center, getWindowOffset(), clickDelayMilliseconds, cancellationToken);
			await Task.Delay(verificationDelayMilliseconds, cancellationToken);
			MerchantSnapshot merchantSnapshot = getSnapshot();
			bool sourceChanged;
			bool inventoryChanged;
			bool flag = IsConfirmed(snapshot, merchantSnapshot, target, beforeCells, beforeItems, beforeStacks, out sourceChanged, out inventoryChanged);
			log.Info("RESULT", $"item=\"{target.Name}\", success={flag}, sourceChanged={sourceChanged}, cells={beforeCells}->{merchantSnapshot.OccupiedInventoryCells}, items={beforeItems}->{merchantSnapshot.InventoryItemCount}, stacks={beforeStacks}->{merchantSnapshot.InventoryStackTotal}, window={merchantSnapshot.Window}");
			if (!flag)
			{
				await Task.Delay(Math.Max(0, recoveryDelayMilliseconds), cancellationToken);
				MerchantSnapshot merchantSnapshot2 = getSnapshot();
				flag = IsConfirmed(snapshot, merchantSnapshot2, target, beforeCells, beforeItems, beforeStacks, out sourceChanged, out inventoryChanged);
				log.Info("RESULT_RECHECK", $"item=\"{target.Name}\", success={flag}, sourceChanged={sourceChanged}, cells={beforeCells}->{merchantSnapshot2.OccupiedInventoryCells}, items={beforeItems}->{merchantSnapshot2.InventoryItemCount}, stacks={beforeStacks}->{merchantSnapshot2.InventoryStackTotal}, window={merchantSnapshot2.Window}");
			}
			if (!flag)
			{
				return new PurchaseResult(desiredQuantity, purchased, "Click was not confirmed by memory", lastWindow);
			}
			purchased++;
			deadline = DateTimeOffset.UtcNow + merchantTimeout;
		}
		return new PurchaseResult(desiredQuantity, purchased, "Completed", lastWindow);
	}

	private static bool IsConfirmed(MerchantSnapshot before, MerchantSnapshot after, MerchantItemSnapshot target, int beforeCells, int beforeItems, int beforeStacks, out bool sourceChanged, out bool inventoryChanged)
	{
		sourceChanged = after.Window != before.Window || after.Items.All((MerchantItemSnapshot item) => item.ElementAddress != target.ElementAddress);
		bool flag = after.OccupiedInventoryCells > beforeCells;
		inventoryChanged = flag || after.InventoryItemCount > beforeItems || after.InventoryStackTotal > beforeStacks;
		return sourceChanged | inventoryChanged;
	}
}
