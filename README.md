# Kado-Exile 0.6.0

Kado-Exile is an ExileAPI plugin derived from the KadoTool merchant workflow.
All merchant detection now comes from ExileAPI memory objects; there is no
screen capture, OCR, color scan, grid estimation, or image-based verification.

## Supported windows

- `OfflineMerchantPanel` through `StashElement.VisibleStash`
- `PurchaseWindow` through `VendorStashTabContainer.VisibleStash`
- `PurchaseWindowHideout` through the compatibility property exposed by ExileAPI

Recent ExileAPI builds mark `PurchaseWindowHideout` obsolete because it is
merged with `PurchaseWindow`. Kado-Exile still probes both properties and logs
the concrete window selected by the installed ExileAPI build.

## Install

1. Extract the release into `ExileApi-Compiled-master\Plugins\Compiled\Kado-Exile`.
2. Start ExileAPI and enable `Kado-Exile`.
3. Configure `Item regex`, quantity and hotkeys in the plugin menu.

The original KadoTool Trade workflow is retained through `Trade URL`,
`POESESSID`, listing loading and the website Travel endpoint. Only the merchant
recognition and purchase/move decision path has been replaced with ExileAPI
memory data.

Version 0.6.0 accepts both saved-search ID links and the Path of Exile Trade
links that embed a URL-safe Base64/gzip search query. Embedded query filters and
league are preserved. Trade URLs are limited to 16,384 characters, compressed
queries to 8 KiB, and expanded queries to 1 MiB.

## Operation

- `Start` / `F6`: load listings, Travel, paste regex and buy highlighted items;
  then continue automatically with the next hideout.
- `Stop` / `F7`: stop the complete pipeline immediately.

When the configured quantity is reached, Kado-Exile plays ExileAPI's bundled
`treasure.wav`. `Completion sound` enables or disables it and `Completion
volume` controls playback from 0 to 100 percent. No sound is played when the
available listings end before the requested quantity is reached.

Before each merchant operation, the plugin waits for the new area to finish
loading and for the merchant item list and inventory memory to remain stable
across multiple reads. A failed first click gets a delayed memory recheck and
one same-hideout recovery pass before the listing is skipped.

Version 0.2.0 adds named trade profiles. Each profile keeps its Trade URL,
Path of Exile search expression and DPAPI-encrypted POESESSID together. The
active profile is snapshotted when Start is pressed, profile switching is
locked during automation, and exports omit POESESSID by default.

Version 0.2.1 no longer blocks merchant readiness when player inventory memory
is temporarily unavailable. It also plays completion audio through WinMM with
verified error codes, real 0-100 volume control, configurable WAV/MP3 paths and
an ExileAPI SoundController fallback.

Version 0.2.2 added inventory and currency safety checks. The current 0.6.0
build keeps the full-inventory stop, but intentionally disables the
currency-exhausted stop because some ExileAPI snapshots can report a false
zero balance. The game remains authoritative when a purchase cannot be paid.

Version 0.2.3 recovers profiles copied from another Windows user without
crashing the ExileAPI settings panel. DPAPI-protected POESESSID values that
cannot be decrypted are cleared while profile name, Trade URL and item regex
are preserved. The user must enter POESESSID again before Start is accepted.

Version 0.2.4 separates new-profile creation from editing the active profile.
New profile opens a blank draft with dedicated Create and Cancel actions, while
Save, Rename, Duplicate and Delete are unavailable until creation finishes.
Creating a profile must add a new stable ID and cannot update the old profile.

Version 0.3.0 separates POESESSID profiles from Trade profiles. Session
profiles contain only the encrypted POESESSID; Trade profiles contain only the
Trade URL and game search expression. Existing combined profiles are migrated
to both stores on first startup. Start snapshots the currently selected session
and Trade profiles together for the operation.

`Start` and `Stop` are the only action buttons. Trade URL, POESESSID, regex,
quantity and timing values remain configuration fields rather than actions.

Each Ctrl+click is verified from a new ExileAPI snapshot. A purchase/move only
counts when the source element disappears, the merchant window changes, or the
occupied player-inventory cell count increases.

Version 0.1.3 follows HighlightedItems and uses the vendor item's ExileAPI
`isHighlighted` flag as the authoritative result of the expression pasted into
the game. Presentation flags such as shiny highlight and saturation are logged
for diagnostics but no longer decide which item is purchased. Every highlighted
item in the current merchant is bought before continuing to the next hideout,
unless the configured desired quantity has already been reached.

Logs are written to `Logs\Kado-Exile-yyyyMMdd.log` inside the plugin directory.
They include the open window, selected item, regex result, click coordinates,
inventory counts and the memory-backed verification result.

## Build and release

```powershell
dotnet test .\Kado-Exile.slnx -c Release
.\build-release.ps1
```

The build script's `-SkipInstall` switch creates the release package without
copying the plugin DLL into the local ExileAPI directory.

Set `ExileApiPath` when ExileAPI is installed elsewhere:

```powershell
dotnet build .\Kado-Exile.slnx -c Release -p:ExileApiPath=C:\path\to\ExileApi
```
