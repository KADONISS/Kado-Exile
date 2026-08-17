using KadoExile.Models;

namespace KadoExile.Services;

public static class ProfileValidator
{
	public static bool TryValidate(TradeProfile profile, out string error)
	{
		if (string.IsNullOrWhiteSpace(profile.Name))
		{
			error = "Profile name is empty.";
			return false;
		}
		if (string.IsNullOrWhiteSpace(profile.PoeSessionId))
		{
			error = "POESESSID is empty.";
			return false;
		}
		if (!PoeTradeClient.TryParseTradeUrl(profile.TradeUrl, out TradeSearchContext _, out error))
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(profile.ItemRegex))
		{
			error = "Item search expression is empty.";
			return false;
		}
		error = string.Empty;
		return true;
	}
}
