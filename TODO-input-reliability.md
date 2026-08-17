# TODO: Correct regex input and true background input

## Confirmed causes

- KadoTool's working regex path saves the current clipboard, writes the complete
  expression, sends `Ctrl+F` then `Ctrl+V`, and restores the clipboard. It does
  not type the expression one character at a time.
- Kado-Exile currently sends `TEXT` to the injected hook, which forwards every
  UTF-16 character as a separate `WM_CHAR`. This is not equivalent to a paste
  and is unreliable for quotes, backslashes, Unicode, keyboard layouts, focus
  changes, and PoE's custom text control.
- Kado-Exile does not explicitly replace existing search text. If `Ctrl+F` does
  not select all text in the current game state, the new expression is appended.
- A successful named-pipe acknowledgement only means that the hook accepted a
  command. It does not prove that PoE consumed the keyboard or mouse input.
- Runtime logs show unrelated items such as `Coral Reef Chart`,
  `Saltwater Voyage`, and `Aquatic Trek` all reported as
  `rawHighlight=True` for the expression `Chart "Crossing" "L: 83" "8 chaos"`.
  The current `ExileAPI.isHighlighted` mapping is therefore not yet proven to
  represent the merchant search highlight and must not be the sole buy filter.
- The current virtual mouse only hooks `GetCursorPos`, `GetAsyncKeyState`, and
  `GetKeyState`, then queues `WM_MOUSEMOVE`/button messages. PoE may consume raw
  input, DirectInput, or an internal cursor state instead. This is not yet a
  verified equivalent of DreamPoeBot's virtual cursor.

## P0 - Make regex entry deterministic

- [x] Capture the exact KadoTool sequence in an integration test:
  `Ctrl+F -> atomic paste`, without `Enter`.
- [ ] Add a dedicated hook command such as `SET_SEARCH_TEXT`; do not implement it
  as repeated `WM_CHAR` messages.
- [ ] Implement target-scoped clipboard virtualization in the injected process:
  intercept the clipboard read used by the target PoE process, expose the
  requested expression only during the virtual `Ctrl+V`, then remove the
  virtual clipboard state. Do not overwrite the user's global clipboard.
- [ ] If clipboard virtualization is not compatible with the current PoE build,
  use a verified ExileAPI/game text-control API. Do not fall back silently to
  physical keyboard input or global clipboard mutation.
- [x] Use the sequence `Ctrl+F -> wait for search focus -> Ctrl+A -> virtual
  Ctrl+V`. Never send `Enter`.
- [ ] Read the merchant search text from ExileAPI memory if available. Otherwise,
  verify that the highlighted item set changes and remains stable for multiple
  reads before buying.
- [ ] Validate the exact ExileAPI highlight fields against known matching and
  non-matching merchant items with search empty/on/off. Document field polarity
  and window-specific behavior for `OfflineMerchantPanel`, `PurchaseWindow`,
  and `PurchaseWindowHideout`.
- [x] Reject the unsafe state where every merchant slot reports highlighted.
  Never interpret `rawHighlight=True` alone as proof that search input worked.
- [ ] Abort the current hideout when focus/search verification fails. Log
  `SEARCH_INPUT_REJECTED`; do not click any merchant item.
- [x] Log expression length and SHA-256 prefix, command path, and verification
  verification result. Avoid logging the raw expression by default.
- [ ] Test empty existing text, pre-existing text, quotes, backslashes, spaces,
  Vietnamese/Unicode, long expressions, repeated paste, and a covered window.

## P0 - Identify the input path PoE actually consumes

- [ ] Add counters in the injected hook for each candidate input API and thread
  (implemented for `GetCursorPos`, `GetMessagePos`, `GetCursorInfo`,
  `GetPhysicalCursorPos`, `WindowFromPoint`, `GetAsyncKeyState`, and
  `GetKeyState`; raw input and DirectInput remain):
  `GetCursorPos`, `GetPhysicalCursorPos`, `GetAsyncKeyState`, `GetKeyState`,
  `GetRawInputData`, `GetRawInputBuffer`, and DirectInput
  `GetDeviceState`/`GetDeviceData` where applicable.
- [x] Log current-hook counter deltas as `INPUT_PATH_PROBE`, including PID,
  HWND, process start time, thread ID, API, and count. Do not treat message queue
  success as consumption.
- [ ] Run probe sessions with PoE focused, unfocused, covered, and with two PoE
  processes. Determine which API/path changes when the game itself moves and
  clicks the cursor.
- [ ] Compare the result with DreamPoeBot's `HookManager.WriteVector2i`, virtual
  key-state storage, and `PostMouseMovement` behavior. Reproduce the consumed
  state path, not only its public method names.

## P0 - Implement a real virtual cursor on the consumed path

- [ ] Store virtual cursor position and button/modifier state per PID and HWND.
  Never share state between PoE processes.
- [ ] Hook the API/path confirmed by the probe and return virtual state only to
  the intended game threads while an input command is active.
- [ ] Keep the real OS cursor unchanged. Do not call `SetCursorPos`, `SendInput`,
  `mouse_event`, `keybd_event`, foreground activation, or a global-input fallback.
- [ ] Preserve `Ctrl` and left-button state for the full click interval, then
  release both in `finally`, pipe disconnect, plugin stop, and hook shutdown.
- [ ] Emit `INPUT_CONSUMED` only when the relevant hook callback counter advances
  while the matching command ID is active.
- [ ] If no consumption is observed before timeout, stop the operation completely
  with `BACKGROUND_INPUT_NOT_CONSUMED`; do not travel to the next hideout.

## P1 - Objective acceptance tests

- [ ] Add an out-of-process cursor invariant test (runtime logging now records
  `GetPhysicalCursorPos` before/after each input sequence; the 100-action harness
  remains): record physical
  `GetCursorPos` before and after at least 100 moves/Ctrl-clicks and require the
  position to remain unchanged.
- [ ] Record foreground HWND before and after the same test and require it to
  remain unchanged.
- [ ] Confirm item transfer from ExileAPI inventory/merchant snapshots, not from
  pipe ACK or `PostMessage` success.
- [ ] Repeat with the PoE window fully covered by another window.
- [ ] Repeat with two PoE processes and prove commands, coordinates, key state,
  and logs stay isolated by PID/HWND.
- [ ] Test forced pipe loss, stale HWND, PID reuse, hook unload, plugin reload,
  and Stop during held Ctrl/LMB. Every case must release state and stop safely.

## Required logs

- `SEARCH_INPUT_BEGIN command=... length=... hash=...`
- `SEARCH_FOCUS_CONFIRMED command=...`
- `SEARCH_INPUT_VERIFIED command=... highlighted=...`
- `SEARCH_INPUT_REJECTED command=... reason=...`
- `INPUT_PATH_PROBE command=... api=... thread=... delta=...`
- `VIRTUAL_MOVE command=... client=(x,y)`
- `VIRTUAL_CTRL_CLICK command=... ctrl=true lmb=true`
- `INPUT_CONSUMED command=... api=...`
- `BACKGROUND_INPUT_NOT_CONSUMED command=...`
- `PHYSICAL_CURSOR_INVARIANT before=(x,y) after=(x,y) pass=true|false`

## Definition of done

- The exact regex stored in the selected Trade profile is present in PoE's
  merchant search without missing, appended, or transformed characters.
- No merchant click begins until search input and stable highlights are verified.
- The physical cursor and foreground window do not change during automation.
- Buying works while PoE is covered and remains isolated across multiple PoE
  processes.
- Pipe ACK alone can never mark an input or purchase as successful.
- Any hook/input verification failure stops the bot and produces enough logs to
  identify the failed command and input path.
