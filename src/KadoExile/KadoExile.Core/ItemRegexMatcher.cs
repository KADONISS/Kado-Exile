using System;
using System.Text.RegularExpressions;

namespace KadoExile.Core;

public static class ItemRegexMatcher
{
	public static Regex Compile(string pattern)
	{
		if (string.IsNullOrWhiteSpace(pattern))
		{
			throw new ArgumentException("Item regex is empty.", "pattern");
		}
		return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250L));
	}

	public static bool IsMatch(Regex regex, MerchantItemSnapshot item)
	{
		return regex.IsMatch(item.SearchText);
	}
}
