using System;

namespace KadoExile.Models;

public sealed record PoeSessionProfile(Guid Id, string Name, string PoeSessionId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
	public PoeSessionProfile Snapshot()
	{
		return this with { };
	}
}
