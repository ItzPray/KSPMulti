# KSC / Kerbal Konstructs — launch pad coordination (LMP)

This document describes how **Luna Multiplayer** coordinates launch pads when the server enables **launch pad coordination**, for **stock KSP UI** and for **Kerbal Konstructs (KK)** launch site selection (the stack commonly used alongside KSC Enhanced-style packs).

## What LMP actually patches today

| Area | Type / method | File | Purpose |
|------|----------------|------|---------|
| Stock pad list | `UILaunchsiteController` — `resetItems`, `addlaunchPadItem` | `LmpClient/Harmony/LaunchPadStockLaunchSiteUiPatches.cs` | Disable **Launch** on pads another player occupies (server `siteKey`). |
| Stock obstruction test | `LaunchSiteClear.Test` | `LmpClient/Harmony/LaunchSiteClear_Test.cs` | Restore pad blocking when another player occupies the same server `siteKey` (respects overflow+bubble mode). |
| Plan B reserve (stock) | `ShipConstruction.AssembleForLaunch` (prefix) | `LmpClient/Harmony/ShipConstruction_AssembleForLaunch.cs` | Sends **`LaunchPadReserveSiteRequestMsgData`** before assembly so the server can record a short reservation. |
| KK selector | `KerbalKonstructs.UI.LaunchsiteSelectorGUI` — `BuildLaunchsites` (postfix), `SetLaunchsite` (prefix) | `LmpClient/Harmony/KerbalKonstructsLaunchPadHarmony.cs` | Greys KK toggles when blocked; blocks **Set launchsite** when blocked; sends reserve when allowed. Registered from `HarmonyPatcher.Awake` via `KerbalKonstructsLaunchPadHarmony.TryRegister`. |

There are **no** separate `EditorLaunchPadItem` Harmony patches; stock editor behaviour is covered by **`UILaunchsiteController`**, **`LaunchSiteClear`**, and **`ShipConstruction`** as above.

Upstream KK reference (field names): [LaunchsiteSelectorGUI.cs](https://github.com/GER-Space/Kerbal-Konstructs/blob/master/src/UI/LaunchsiteSelectorGUI.cs) — `launchsiteItems`, static `selectedSite`, `LaunchsiteItem.launchsite`.

## Server protocol (authoritative)

- **Occupancy** is derived from PRELAUNCH vessels (plus short-lived **reservations**). See `Server/System/LaunchSite/LaunchSiteOccupancyService.cs`.
- **Snapshots** (`LaunchPadOccupancySnapshotMsgData`) go to all clients when coordination is relevant or when a compact update is not possible.
- **Deltas** (`LaunchPadOccupancyDeltaMsgData`) are sent only when **both** old and new occupancy lists contain **no** `Guid.Empty` “reservation-only” rows, **and** overflow/bubble fields are unchanged, **and** the op count is small (see `TryBuildDelta`). Otherwise a full snapshot is used. This is intentional: reservation rows share `Guid.Empty` and are not keyed uniquely in the delta wire format.
- **Plan B reservation**: client `LaunchPadCliMsg` / `LaunchPadReserveSiteRequestMsgData` → `Server/Message/LaunchPadMsgReader.cs` → `LaunchPadReservationRegistry.TryReserve` → reply `LaunchPadReserveSiteReplyMsgData` + `BroadcastSmart()`.
- **Lease / expiry**: vessel activity uses `LaunchPadVesselActivityTracker` with `LaunchPadLeaseTimeoutSeconds` (stale PRELAUNCH entries drop out of occupancy). Reservations use `LaunchPadReservationDurationSeconds` and `LaunchPadReservationRegistry.ClearExpired()` from the server main loop (`Server/Client/ClientMainThread.cs`). There is no separate always-on background “sweeper” thread; expiry is checked on those ticks and on occupancy reads.

## Optional DLL pin (KSCE / KK stack)

Server settings (XML / `GeneralSettingsDefinition`) can require a **pinned optional DLL** (relative path under `GameData`, optional SHA-256, optional file version min/max) when `LaunchPadKsceEnforceOptionalDllMatch` is true.

Client logic: `LaunchPadKsceCompatibility.RevalidateAfterSettingsSync()` (called from `SettingsMessageHandler`). If enforcement fails, **`StrictKsceDllCheckFailed`** is set, KK Harmony is skipped (`KsceHarmonyPatchesAllowed`), and the client **disconnects** after settings sync with a clear reason — this is the **executable gate** for mismatched clients.

**Limits:** The server does **not** independently cryptographically verify the client’s DLL; it advertises policy via settings, and the **client** enforces it before continuing the session. Cheating clients could lie unless you add a signed handshake or server-side mod proof beyond LMP’s current design.

## Version matrix (operators fill when pinning a build)

| KSP version | Kerbal Konstructs / pack versions | LMP version | Notes |
|-------------|-----------------------------------|-------------|-------|
| *fill* | *fill* | *fill* | e.g. `GameData` folder names, DLL path ending for `LaunchPadKsceOptionalDllRelativePath`, SHA from `Common.CalculateSha256FileHash` on the pinned file. |

## Behaviour notes (“fail soft” vs strict)

- If **`LaunchPadKsceEnforceOptionalDllMatch`** is **false**, KK patches still run when coordination is on and DLL checks are not applied — rely on server deny + stock tests as a baseline.
- If enforcement is **true** and the check **fails**, the session ends at settings sync (hard gate). That is stricter than “fail soft”; operators choose this explicitly.

## Future / outreach

- KK could expose a small API (e.g. `static event Action<KKLaunchSite> OnLaunchSiteSelected`) to reduce reflection fragility across releases.
- If another KSCE UI uses different types, add another optional `AccessTools.TypeByName` patch module using the same `LaunchPadSiteKeyUtil` / `LaunchPadCoordinationSystem` helpers.
