namespace KadoExile.Models;

public sealed record TradeSearchContext(
	string ApiPrefix,
	string RealmPrefix,
	string League,
	string? QueryId,
	string? EmbeddedQueryJson = null);
