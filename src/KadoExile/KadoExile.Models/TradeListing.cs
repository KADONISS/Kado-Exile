namespace KadoExile.Models;

public sealed record TradeListing(string ResultId, string? TravelToken, string? Seller, string? ItemName, decimal? Price, string? Currency, string? Method)
{
	public bool HasTravelToken => !string.IsNullOrWhiteSpace(TravelToken);

	public string DisplayItemName
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(ItemName))
			{
				return ItemName;
			}
			return "PoE Trade listing";
		}
	}

	public string DisplaySeller
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Seller))
			{
				return Seller;
			}
			return "Người bán chưa rõ";
		}
	}

	public string DisplayPrice
	{
		get
		{
			if (Price.HasValue)
			{
				if (string.IsNullOrWhiteSpace(Currency))
				{
					return $"{Price:0.##}";
				}
				return $"{Price:0.##} {Currency}";
			}
			return "Chưa có giá";
		}
	}
}
