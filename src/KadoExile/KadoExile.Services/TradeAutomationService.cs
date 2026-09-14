using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using KadoExile.Core;
using KadoExile.Models;

namespace KadoExile.Services;

public sealed class TradeAutomationService(Func<MerchantSnapshot> getSnapshot, Func<AreaReadinessSnapshot> getAreaReadiness, MerchantPurchaseService purchase, InputController input, KadoLogger log)
{
	public async Task<int> RunAsync(string tradeUrl, string poeSessionId, int travelLimit, string regexPattern, int desiredQuantity, bool requireHighlight, TimeSpan merchantTimeout, int hideoutSettleDelayMilliseconds, int merchantStableReads, int highlightStableReads, int recoveryDelayMilliseconds, int clickDelayMilliseconds, int verificationDelayMilliseconds, CancellationToken cancellationToken)
	{
		if (!PoeTradeClient.TryParseTradeUrl(tradeUrl, out TradeSearchContext context, out string error))
		{
			throw new InvalidOperationException(error);
		}
		int purchased;
		using (PoeTradeClient client = new PoeTradeClient(poeSessionId))
		{
			IReadOnlyList<TradeListing> readOnlyList = await client.LoadListingsAsync(context, PoeTradeClient.NormalizeListingLimit(travelLimit), cancellationToken);
			log.Info("TRADE_LOAD", $"league={context.League}, query={context.QueryId ?? "embedded"}, listings={readOnlyList.Count}, limit={travelLimit}");
			purchased = 0;
			AutomationStopReason automationStopReason = AutomationStopReason.None;
			foreach (TradeListing listing in readOnlyList.Where((TradeListing item) => item.HasTravelToken))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (purchased >= desiredQuantity)
				{
					break;
				}
				InventorySafetyCheck inventorySafetyCheck = InventorySafetyEvaluator.Evaluate(getSnapshot(), listing.Price, listing.Currency);
				if (inventorySafetyCheck.ShouldStop)
				{
					_ = inventorySafetyCheck.StopReason;
					LogAutomationStop(inventorySafetyCheck, listing);
					break;
				}
				AreaReadinessSnapshot beforeArea = getAreaReadiness();
				Exception travelError = null;
				try
				{
					await client.SendTravelAsync(listing.TravelToken, cancellationToken);
				}
				catch (ListingUnavailableException ex)
				{
					log.Info("TRAVEL_SKIP", $"listing={listing.ResultId}, seller={listing.Seller}, reason={ex.Message}");
					continue;
				}
				catch (HttpRequestException ex2)
				{
					travelError = ex2;
					log.Info("TRAVEL_HTTP", "listing=" + listing.ResultId + ", status=indeterminate, reason=" + ex2.Message);
				}
				log.Info("TRAVEL", $"listing={listing.ResultId}, seller={listing.Seller}, item=\"{listing.ItemName}\", price={listing.Price} {listing.Currency}");
				try
				{
					await WaitForMerchantReadyAsync(beforeArea.Generation, merchantTimeout, hideoutSettleDelayMilliseconds, merchantStableReads, requireNewArea: true, cancellationToken);
				}
				catch (TimeoutException ex3)
				{
					log.Info("TRAVEL_SKIP", $"listing={listing.ResultId}, reason={ex3.Message}, transport={travelError?.Message ?? "ok"}");
					continue;
				}
				try
				{
					log.Info("REGEX", "pasting=\"" + regexPattern + "\", sequence=Ctrl+F/Ctrl+V/Enter");
					await input.PasteRegexAsync(regexPattern, cancellationToken);
					await WaitForHighlightedItemAsync(merchantTimeout, highlightStableReads, cancellationToken);
				}
				catch (TimeoutException ex4)
				{
					log.Info("LISTING_SKIP", "listing=" + listing.ResultId + ", reason=" + ex4.Message);
					continue;
				}
				int listingPurchased = 0;
				string listingReason = "Not started";
				for (int recoveryAttempt = 0; recoveryAttempt <= 1; recoveryAttempt++)
				{
					PurchaseResult purchaseResult;
					try
					{
						purchaseResult = await purchase.BuyAsync(regexPattern, desiredQuantity - purchased - listingPurchased, requireHighlight, listing.Price, listing.Currency, merchantTimeout, clickDelayMilliseconds, verificationDelayMilliseconds, recoveryDelayMilliseconds, cancellationToken);
					}
					catch (TimeoutException ex5)
					{
						listingReason = ex5.Message;
						log.Info("LISTING_SKIP", "listing=" + listing.ResultId + ", reason=" + ex5.Message);
						break;
					}
					listingPurchased += purchaseResult.Purchased;
					listingReason = purchaseResult.Reason;
					if (purchaseResult.StopReason != AutomationStopReason.None)
					{
						automationStopReason = purchaseResult.StopReason;
						MerchantSnapshot merchantSnapshot = getSnapshot();
						decimal? availableCurrency = null;
						if (merchantSnapshot.CurrencyAmounts != null && listing.Currency != null && InventorySafetyEvaluator.TryResolveCurrencyName(listing.Currency, out string currencyName) && merchantSnapshot.CurrencyAmounts.TryGetValue(currencyName, out var value))
						{
							availableCurrency = value;
						}
						InventorySafetyCheck safety = new InventorySafetyCheck(purchaseResult.StopReason, purchaseResult.Reason, availableCurrency, listing.Price);
						LogAutomationStop(safety, listing);
						break;
					}
					if (purchaseResult.Reason != "Click was not confirmed by memory" || recoveryAttempt >= 1 || purchased + listingPurchased >= desiredQuantity)
					{
						break;
					}
					AreaReadinessSnapshot currentArea = getAreaReadiness();
					log.Info("MERCHANT_RECOVERY", $"listing={listing.ResultId}, area={currentArea.Name}, generation={currentArea.Generation}, purchased={listingPurchased}, delay={recoveryDelayMilliseconds}ms");
					try
					{
						await Task.Delay(recoveryDelayMilliseconds, cancellationToken);
						await WaitForMerchantReadyAsync(currentArea.Generation, merchantTimeout, 0, merchantStableReads, requireNewArea: false, cancellationToken);
						log.Info("REGEX_RETRY", $"listing={listing.ResultId}, pasting=\"{regexPattern}\"");
						await input.PasteRegexAsync(regexPattern, cancellationToken);
						await WaitForHighlightedItemAsync(merchantTimeout, highlightStableReads, cancellationToken);
					}
					catch (TimeoutException ex6)
					{
						listingReason = "Recovery failed: " + ex6.Message;
						log.Info("RECOVERY_FAILED", "listing=" + listing.ResultId + ", reason=" + ex6.Message);
						break;
					}
				}
				purchased += listingPurchased;
				log.Info("LISTING_DONE", $"listing={listing.ResultId}, purchased={listingPurchased}, total={purchased}/{desiredQuantity}, reason={listingReason}");
				if (automationStopReason != AutomationStopReason.None)
				{
					break;
				}
			}
			log.Info("TRADE_DONE", $"purchased={purchased}/{desiredQuantity}");
			return purchased;
		}
		void LogAutomationStop(InventorySafetyCheck inventorySafetyCheck2, TradeListing tradeListing)
		{
			log.Info("AUTOMATION_STOP", $"reason={inventorySafetyCheck2.StopReason}, detail=\"{inventorySafetyCheck2.Reason}\", listing={tradeListing.ResultId}, currency={tradeListing.Currency ?? "unknown"}, available={inventorySafetyCheck2.AvailableCurrency?.ToString() ?? "unknown"}, required={inventorySafetyCheck2.RequiredCurrency?.ToString() ?? "unknown"}, purchased={purchased}/{desiredQuantity}");
		}
	}

	private async Task WaitForHighlightedItemAsync(TimeSpan timeout, int requiredStableReads, CancellationToken cancellationToken)
	{
		DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
		string previousSignature = null;
		int stableReads = 0;
		while (DateTimeOffset.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			MerchantSnapshot merchantSnapshot = getSnapshot();
			MerchantItemSnapshot[] array = (from item in merchantSnapshot.Items
				where item.IsHighlighted
				orderby item.ElementAddress
				select item).ToArray();
			if (merchantSnapshot.IsOpen && array.Length != 0)
			{
				string text = string.Join(',', array.Select((MerchantItemSnapshot item) => item.ElementAddress));
				stableReads = ((!(text == previousSignature)) ? 1 : (stableReads + 1));
				previousSignature = text;
				if (stableReads >= Math.Max(2, requiredStableReads))
				{
					log.Info("HIGHLIGHT", $"window={merchantSnapshot.Window}, highlighted={array.Length}, rawHighlights={merchantSnapshot.Items.Count((MerchantItemSnapshot item) => item.RawHighlight)}, saturated={merchantSnapshot.Items.Count((MerchantItemSnapshot item) => item.IsSaturated)}, items={merchantSnapshot.Items.Count}, stableReads={stableReads}");
					return;
				}
			}
			else
			{
				previousSignature = null;
				stableReads = 0;
			}
			await Task.Delay(100, cancellationToken);
		}
		throw new TimeoutException("No highlighted merchant item appeared after pasting regex.");
	}

	private async Task WaitForMerchantReadyAsync(long expectedGeneration, TimeSpan timeout, int settleDelayMilliseconds, int requiredStableReads, bool requireNewArea, CancellationToken cancellationToken)
	{
		DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
		TimeSpan settleDelay = TimeSpan.FromMilliseconds(Math.Max(0, settleDelayMilliseconds));
		MerchantStabilityTracker stability = new MerchantStabilityTracker(requiredStableReads);
		string previousWaitReason = null;
		while (DateTimeOffset.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			DateTimeOffset utcNow = DateTimeOffset.UtcNow;
			AreaReadinessSnapshot areaReadinessSnapshot = getAreaReadiness();
			if (!(requireNewArea ? (areaReadinessSnapshot.Generation > expectedGeneration) : (areaReadinessSnapshot.Generation == expectedGeneration)))
			{
				stability.Reset();
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(18, 2);
				defaultInterpolatedStringHandler.AppendLiteral("expected=");
				defaultInterpolatedStringHandler.AppendFormatted(requireNewArea ? $">{expectedGeneration}" : ((object)expectedGeneration));
				defaultInterpolatedStringHandler.AppendLiteral(", actual=");
				defaultInterpolatedStringHandler.AppendFormatted(areaReadinessSnapshot.Generation);
				LogWaitReason("area-generation", defaultInterpolatedStringHandler.ToStringAndClear());
				await Task.Delay(100, cancellationToken);
				continue;
			}
			if (!areaReadinessSnapshot.InGame || areaReadinessSnapshot.IsLoading || !areaReadinessSnapshot.PlayerValid || utcNow - areaReadinessSnapshot.ChangedAt < settleDelay)
			{
				stability.Reset();
				double value = Math.Max(0.0, (settleDelay - (utcNow - areaReadinessSnapshot.ChangedAt)).TotalMilliseconds);
				LogWaitReason("area-not-ready", $"area={areaReadinessSnapshot.Name}, inGame={areaReadinessSnapshot.InGame}, loading={areaReadinessSnapshot.IsLoading}, playerValid={areaReadinessSnapshot.PlayerValid}, settleRemaining={value:0}ms");
				await Task.Delay(100, cancellationToken);
				continue;
			}
			MerchantSnapshot merchantSnapshot = getSnapshot();
			bool flag = stability.Observe(merchantSnapshot);
			LogWaitReason("merchant-stabilizing", $"area={areaReadinessSnapshot.Name}, window={merchantSnapshot.Window}, items={merchantSnapshot.Items.Count}, inventoryMemory={merchantSnapshot.InventoryMemoryAvailable}, stableReads={stability.StableReads}/{Math.Max(2, requiredStableReads)}");
			if (flag)
			{
				if (!merchantSnapshot.InventoryMemoryAvailable)
				{
					log.Info("INVENTORY_MEMORY_WARNING", "area=" + areaReadinessSnapshot.Name + ", merchant is stable but player inventory memory is unavailable; purchase verification will use merchant source changes");
				}
				log.Info("MERCHANT_STABLE", $"area={areaReadinessSnapshot.Name}, generation={areaReadinessSnapshot.Generation}, window={merchantSnapshot.Window}, address=0x{merchantSnapshot.WindowAddress:X}, items={merchantSnapshot.Items.Count}, stableReads={stability.StableReads}, elapsedSinceArea={(utcNow - areaReadinessSnapshot.ChangedAt).TotalMilliseconds:0}ms");
				return;
			}
			await Task.Delay(100, cancellationToken);
			void LogWaitReason(string reason, string detail)
			{
				string text = reason + ":" + detail;
				if (!(text == previousWaitReason))
				{
					previousWaitReason = text;
					log.Detail("MERCHANT_WAIT", "reason=" + reason + ", " + detail);
				}
			}
		}
		throw new TimeoutException("Area or merchant did not become stable after Travel.");
	}
}
