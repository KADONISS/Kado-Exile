namespace KadoExile.Core;

public sealed record InventorySafetyCheck(AutomationStopReason StopReason, string Reason, decimal? AvailableCurrency, decimal? RequiredCurrency)
{
	public bool ShouldStop => StopReason != AutomationStopReason.None;
}
