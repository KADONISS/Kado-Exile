using System;

namespace KadoExile.Models;

public sealed record TradeSearchProfile(Guid Id, string Name, string TradeUrl, string ItemRegex, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
	public TradeSearchProfile Snapshot()
	{
		return this with { };
	}
}
