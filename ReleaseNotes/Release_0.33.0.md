# KSPMP 0.33.0

**KSPMP** (Kerbal Space Program Multiplayer) — in-game mod plus **standalone KSPMPServer**, with in-game and server update flows aligned to [GitHub Releases](https://github.com/ItzPray/KSPMulti/releases).

Build **0.33.0** (configuration: **Release**). The client and server zips are staged the same way as in CI, and the **client zip path matches the GitHub autoupdate** asset name (`KSPMultiplayer-Client-Release.zip` for Release).

**Full changelog (commits):** https://github.com/ItzPray/KSPMulti/compare/0.32.0...0.33.0

---

## Highlights in 0.33.0

### In-game and dedicated-server updates

- **Client auto-update** — From the in-game **Update** UI: fetch the latest **GitHub** client zip, back up the current `GameData/KSPMultiplayer` (and `000_Harmony` as applicable), and merge a new build with a clear Windows helper path (`UpdateHandler` + `ClientModUpdateInstaller`, PowerShell helper, update window copy).
- **Dedicated server auto-update** — On start (and optional shutdown), compare your **local server build** to the **latest published release**; on approval, **download the server zip** and **merge** into the install while **preserving** `Universe`, `Config`, `logs`, `Plugins`, `Backup`, and similar. On **Windows**, a **`Kspmp-Apply-Server-Update.cmd`** step runs after exit so **locked native DLLs** (e.g. Lidgren) are replaced safely **via robocopy**; the window shows **staged and installed** `Server.dll` assembly version and **pauses** so you can read the log.
- **KSPMP branding** — Console and logs use **KSPMP**-oriented names/titles; **session admission** still enforces a common protocol fork id, and **exact build** matching treats **`x.y.z`** and **`x.y.z-compiled`** informational versions as the **same** release (client vs dedicated server).
- **Release tooling** — `SetKspmpReleaseVersion` / `SetKspmpDevServerVersionBelowRelease` help keep **client, server, and** `KSPMultiplayer.version` in sync; server tests cover handshake and updater merge rules.

### Vessels, networking, and server correctness

- **Network send path** — **Batching** in the send loop (capped per tick), with explicit **“needs flush”** so unnecessary queue flushes are reduced.
- **Flight state sync** — Fewer **redundant** flight-state messages (tolerances, forced resend window, last-sent tracking, **reset** hooks); on **control lock** acquisition: zero throttle, reset state tracking, and a **timely** position update where appropriate.
- **Removed vessels (server)** — `RemovedVessels` is kept in a **thread-safe** store; the server no longer **reinserts** vessels that were **removed** from the session, with **updaters** bailing out early for removed craft and a **regression test** in `ServerTest`.
- **Vessel update message size** — `VesselUpdateMsgData` accounts for **extra float** payload correctly.
- **Harmony** — Patching is **idempotent** so re-entry does not break startup.
- **Chat** — Formatting is simplified.
- **Version** — `KSPMultiplayer.version`, `LmpClient` and `Server` assemblies, and release notes for this file land at **0.33.0** together in the same release line.

---

## Install (short)

- **Client:** Unzip so you have **`GameData/KSPMultiplayer`** and, if you use it, **`GameData/000_Harmony`** (see **`KSPMP Readme.txt`** in the client zip).
- **Server:** Run the published **server** output **outside** the KSP game folder (e.g. a dedicated folder such as `C:\KSPMPServer` or your own path). After a self-update, keep using the **same** folder; user data is preserved by design.

We still **cannot guarantee compatibility** with every mod in multiplayer; align **client, server, and** mod allowlists / **mod control** with your group.

---

## Upgrading from 0.32.0

- **Back up** your KSP save directory and, for hosts, the **entire** server install (especially `Universe` / `Universe_Backup` and `Config`) before changing versions.
- Run **0.33.0** on **both** the **game mod** and the **standalone server** (or a matching pair from the same tag). The **in-game and server** updaters are built to move you between published builds without manually hunting zips, but a **manual** zip install from the [releases page](https://github.com/ItzPray/KSPMulti/releases) is always an option.
- If you use **Luna**-era paths, note that the project’s save layout and migration story were documented in **0.32.0**; first-time migration from `saves/LunaMultiplayer` to **`saves/KSPMultiplayer`** may already have run on 0.32.0. Keep backups when jumping versions.

---

## Thanks

- Everyone who tested **updater** flows, **KSPMPServer** on Windows, and long **multihour** career sessions; your reports drive these fixes.
- The **Luna Multiplayer** lineage for the open foundation this fork continues to build on.

---

**Repository:** [ItzPray/KSPMulti](https://github.com/ItzPray/KSPMulti)
