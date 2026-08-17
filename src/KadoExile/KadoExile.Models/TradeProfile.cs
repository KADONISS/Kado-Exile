using System;

namespace KadoExile.Models;

public sealed record TradeProfile(Guid Id, string Name, string PoeSessionId, string TradeUrl, string ItemRegex, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
	public TradeProfile Snapshot()
	{
		return this with { };
	}
}
