namespace KadoExile.Core;

public sealed record PurchaseResult(int Requested, int Purchased, string Reason, MerchantWindowKind LastWindow, AutomationStopReason StopReason = AutomationStopReason.None);
