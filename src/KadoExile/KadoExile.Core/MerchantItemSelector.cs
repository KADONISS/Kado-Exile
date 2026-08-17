using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KadoExile.Core;

public static class MerchantItemSelector
{
	public static MerchantItemSnapshot? SelectNext(IReadOnlyList<MerchantItemSnapshot> items, bool requireHighlight, Regex? memoryTextRegex)
	{
		return items.FirstOrDefault((MerchantItemSnapshot item) => (!requireHighlight) ? (memoryTextRegex == null || ItemRegexMatcher.IsMatch(memoryTextRegex, item)) : item.IsHighlighted);
	}
}
