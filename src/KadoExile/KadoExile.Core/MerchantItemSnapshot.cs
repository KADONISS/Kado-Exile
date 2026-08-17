using SharpDX;

namespace KadoExile.Core;

public sealed record MerchantItemSnapshot(long ElementAddress, uint InventoryId, string Name, string SearchText, bool RawHighlight, bool IsSaturated, bool IsActive, bool IsHighlighted, SharpDX.RectangleF ClientRect)
{
	public SharpDX.Vector2 Center
	{
		get
		{
			return ClientRect.Center;
		}
	}
}
