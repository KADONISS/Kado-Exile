using System;
using System.Linq;
using SharpDX;

namespace KadoExile.Core;

public sealed class MerchantStabilityTracker(int requiredReads)
{
	private readonly int _requiredReads = Math.Max(2, requiredReads);

	private string? _previousSignature;

	public int StableReads { get; private set; }

	public bool Observe(MerchantSnapshot snapshot)
	{
		if (!IsUsable(snapshot))
		{
			Reset();
			return false;
		}
		string text = CreateSignature(snapshot);
		StableReads = ((!(text == _previousSignature)) ? 1 : (StableReads + 1));
		_previousSignature = text;
		return StableReads >= _requiredReads;
	}

	public void Reset()
	{
		_previousSignature = null;
		StableReads = 0;
	}

	private static bool IsUsable(MerchantSnapshot snapshot)
	{
		if (snapshot.IsOpen && snapshot.Items.Count > 0)
		{
			return snapshot.Items.All(delegate(MerchantItemSnapshot item)
			{
				if (item.ElementAddress != 0L)
				{
					SharpDX.RectangleF clientRect = item.ClientRect;
					if (float.IsFinite(clientRect.X))
					{
						if (float.IsFinite(clientRect.Y))
						{
							if (clientRect.Width > 0f)
							{
								return clientRect.Height > 0f;
							}
						}
					}
				}
				return false;
			});
		}
		return false;
	}

	private static string CreateSignature(MerchantSnapshot snapshot)
	{
		return $"{snapshot.Window}:{snapshot.WindowAddress:X}:" + string.Join(',', from item in snapshot.Items
			orderby item.ElementAddress
			select item.ElementAddress);
	}
}
