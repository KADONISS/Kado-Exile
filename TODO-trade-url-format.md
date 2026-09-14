# Completed TODO: Support embedded-query Path of Exile Trade URLs

Status: Implemented for v0.6.0.

## Original behavior and root cause

The example link uses the route `/trade/search/{league}/{segment}`, but its
final segment is not a saved-search ID. Decoding the URL-safe Base64 segment
and inflating it as gzip yields a UTF-8 JSON query. The supplied `Allflame`
query includes status, map shape, area level, and price filters.

Before v0.6.0, `PoeTradeClient.TryParseTradeUrl` stored every fourth path
segment as `TradeSearchContext.QueryId`. `LoadListingsAsync` then requested
the saved-search definition at `/api/trade/search/{league}/{queryId}` before
executing it. That flow handled short-ID links but not embedded queries. The
plugin's Trade URL input was limited to 1024 characters.

Trade profiles already store the complete URL as a string, so no profile-file
schema migration is expected.

## P0 — Load embedded-query links

- [x] Extend trade-link parsing to distinguish a legacy saved-search ID from a
  successfully decoded embedded query. Keep the existing PoE 1 short-ID route
  and PoE 2 `trade2` route working.
- [x] Decode the path segment as unpadded URL-safe Base64 (`-` and `_`), inflate
  gzip, decode strict UTF-8, and require a JSON object with a valid trade query
  shape. Return a clear validation error for malformed Base64, gzip, UTF-8, or
  JSON; do not silently reinterpret corrupt payloads as IDs.
- [x] Bound both compressed and expanded payload sizes before parsing so a
  pasted URL cannot trigger unbounded decompression or allocation.
- [x] For an embedded query, skip the saved-search-definition GET and submit
  the decoded query through the existing search execution flow. Preserve every
  filter and the selected league, then use the returned search ID for listing
  fetches. Keep the saved-search GET path for legacy IDs.
- [x] Raise the Trade URL input capacity above its current 1024-character
  limit. Verify that paste, edit, save, reload, and profile switching preserve
  the entire link; show validation feedback rather than silently truncating an
  over-limit URL.

## P1 — Tests and documentation

- [x] Add a parser test using the exact supplied Allflame URL. Assert that its
  decoded query retains `status.option=securable`, `chart_shape=5`,
  `area_level=83`, and `price.min=7`.
- [x] Keep regression tests for existing PoE 1 saved-search IDs, PoE 2 links,
  copied-link normalization, and profile persistence.
- [x] Test invalid/truncated Base64, invalid gzip, invalid UTF-8/JSON, a JSON
  value that is not a query object, and compressed/expanded size limits.
- [x] Test the HTTP flow with a fake handler: embedded links POST the decoded
  query directly, legacy links first GET their saved query, and both fetch
  listings using the returned search ID. No live PoE account is needed for
  these tests.
- [x] Update the README to explain that both saved-ID links and embedded-query
  links are accepted. Keep embedded-query support limited to PoE 1 until the
  PoE 2 URL shape is independently verified.

## Definition of done

- Pasting the supplied link into a Trade profile loads listings without trying
  to GET the compressed segment as a saved-search ID.
- The executed request retains the query's league and filters, and returned
  listing IDs are fetched through the existing listing flow.
- Existing saved-ID links continue to work, and malformed or oversized
  embedded links fail with a readable error before any API request.
- The full link survives the plugin UI and profile save/reload cycle.
