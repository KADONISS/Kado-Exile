using System;

namespace KadoExile.Core;

public sealed record AreaReadinessSnapshot(long Generation, string Name, DateTimeOffset ChangedAt, bool InGame, bool IsLoading, bool PlayerValid)
{
	public bool IsReadyAfter(long previousGeneration, DateTimeOffset now, TimeSpan settleDelay)
	{
		if (Generation > previousGeneration && InGame && !IsLoading && PlayerValid)
		{
			return now - ChangedAt >= settleDelay;
		}
		return false;
	}
}
