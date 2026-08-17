using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KadoExile.Core;
using SharpDX;

namespace KadoExile.Services;

public sealed class QuickMoveService(Func<MerchantSnapshot> getSnapshot, Func<Vector2> getWindowOffset, InputController input, KadoLogger log)
{
	public async Task<int> MoveToInventoryAsync(string regexPattern, bool useRegexFallback, int clickDelayMilliseconds, int verificationDelayMilliseconds, CancellationToken cancellationToken)
	{
		Regex regex = (string.IsNullOrWhiteSpace(regexPattern) ? null : ItemRegexMatcher.Compile(regexPattern));
		int moved = 0;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			MerchantSnapshot merchantSnapshot = getSnapshot();
			if (!merchantSnapshot.IsOpen || !merchantSnapshot.InventoryVisible)
			{
				log.Info("MOVE", "aborted: merchant/source or inventory panel is closed");
				return moved;
			}
			if (merchantSnapshot.OccupiedInventoryCells >= 60)
			{
				log.Info("MOVE", "aborted: inventory is full");
				return moved;
			}
			MerchantItemSnapshot[] array = merchantSnapshot.Items.Where((MerchantItemSnapshot item) => item.IsHighlighted).ToArray();
			MerchantItemSnapshot[] source = ((array.Length != 0) ? array : ((useRegexFallback && regex != null) ? merchantSnapshot.Items.Where((MerchantItemSnapshot item) => ItemRegexMatcher.IsMatch(regex, item)).ToArray() : Array.Empty<MerchantItemSnapshot>()));
			MerchantItemSnapshot target = source.FirstOrDefault();
			if ((object)target == null)
			{
				log.Info("MOVE", $"completed: moved={moved}, no highlighted/regex-matched items remain");
				return moved;
			}
			int beforeCells = merchantSnapshot.OccupiedInventoryCells;
			int beforeItems = merchantSnapshot.InventoryItemCount;
			int beforeStacks = merchantSnapshot.InventoryStackTotal;
			log.Info("MOVE_SELECT", $"window={merchantSnapshot.Window}, item=\"{target.Name}\", highlighted={target.IsHighlighted}, client=({target.Center.X:0.0},{target.Center.Y:0.0})");
			await input.CtrlClickAsync(target.Center, getWindowOffset(), clickDelayMilliseconds, cancellationToken);
			await Task.Delay(verificationDelayMilliseconds, cancellationToken);
			MerchantSnapshot merchantSnapshot2 = getSnapshot();
			bool flag = merchantSnapshot2.OccupiedInventoryCells > beforeCells || merchantSnapshot2.InventoryItemCount > beforeItems || merchantSnapshot2.InventoryStackTotal > beforeStacks || merchantSnapshot2.Items.All((MerchantItemSnapshot item) => item.ElementAddress != target.ElementAddress);
			log.Info("MOVE_RESULT", $"item=\"{target.Name}\", success={flag}, cells={beforeCells}->{merchantSnapshot2.OccupiedInventoryCells}, items={beforeItems}->{merchantSnapshot2.InventoryItemCount}, stacks={beforeStacks}->{merchantSnapshot2.InventoryStackTotal}");
			if (!flag)
			{
				break;
			}
			moved++;
		}
		return moved;
	}
}
