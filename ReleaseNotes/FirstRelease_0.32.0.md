# KSPMP 0.32.0 — first public release

**KSPMP** (Kerbal Space Program Multiplayer) is a fork and continuation of the **Luna Multiplayer (LMP)** lineage, updated for a consistent **KSPMP** identity on the network, a modern `**GameData/KSPMultiplayer`** layout, and a long run of **sync, stability, and career** fixes that did not exist in the last wide “legacy LMP” builds you may remember.

This is our **first GitHub release** as **ItzPray/KSPMP**: one download for the **KSP client mod** and one for the **standalone server**, with in-game update checks against **published** [GitHub Releases](https://github.com/ItzPray/KSPMulti/releases).

---

## What we improved compared to legacy LMP

### Product and packaging

- **Rebrand to KSPMP** — wire protocol, logging, and user-facing text use the **KSPMP** product name; the mod lives under `**GameData/KSPMultiplayer`** (with `**KSPMultiplayer.version**` for KSP-AVC).
- **Saves and migration** — multiplayer save data uses `**saves/KSPMultiplayer`**, with a **one-time migration** from the old `saves/LunaMultiplayer` folder so existing saves are not left behind.
- **Mod control** — `**KSPModControl.xml`** (with **legacy `LMPModControl.xml` fallback**) for part/mod compatibility checks.
- **Build and release automation** — scripts to stage **AppVeyor-matched** zips and publish via **GitHub CLI**; asset names match what the updater expects (e.g. `KSPMultiplayer-Client-Release.zip`).

### Multiplayer sync and architecture

- **Persistent sync pipeline** — a **canonical snapshot** model and reconciler, with **tests** for deferred and rejected snapshot handling, replacing fragile ad-hoc scenario patching.
- **Scenario sync domains** — structured **server/client domains** for career/science/contracts/facilities, with **clear authority** and **typed intents** (no “two writers, one node” races).
- **Contract system** — major work on **contract population**, **duplicates**, **reconnect/restart**, **offers**, **mission completion lag**, and **session stability**; contracts stay aligned across clients and server restarts far more reliably than in typical legacy LMP sessions.
- **R&D and science** — **tech tree / R&D** refresh and **duplication** issues addressed; **experimental parts** and **upgradable facility** snapshots improved.
- **Vessels and space center** — **vessel sync**, **spectate**, **HUD/labels**, **locks**, **launch site** coordination, **safety bubble**, and **KSC/warp** autosync fixes from months of focused iteration.
- **Stability** — **memory** pressure and **leak**-related fixes, **reconnection** hardening, and **handshake** validation.

### Player-facing polish

- **Localization** — strings updated for **KSPMP** across shipped languages (where applicable).
- **Loading and UI** — loading screen and related behavior brought in line with the new branding.

---

## Install (short)

- **Client:** Unzip so you have `**GameData/KSPMultiplayer`** and `**GameData/000_Harmony**` (see `**KSPMP Readme.txt**` in the client zip).
- **Server:** Run the published **server** output **outside** the KSP folder (e.g. a folder on the desktop or a drive root).

We still **cannot guarantee compatibility with arbitrary mod stacks** in MP; keep installs close to your friends and the server’s **mod control** lists.

---

## Upgrading from old “LMP” / LunaMultiplayer

- **First run** will **migrate** `saves/LunaMultiplayer` → `**saves/KSPMultiplayer`** where applicable.
- **Back up** your save folder before updating.
- **Server and client** should be on **compatible** KSPMP builds (see version compatibility in-game and in release tags).

---

## Thanks

- **Luna Multiplayer** project and community for the original open-source base and years of multiplayer Kerbal play.
- Everyone who reported issues, tried nightlies, and helped narrow down contract and sync edge cases.

---

**Repository history (from the fork baseline through current work):**  
[ItzPray/KSPMulti — commit log](https://github.com/ItzPray/KSPMulti/commits) · after tagging, GitHub can show a range, e.g. `https://github.com/ItzPray/KSPMulti/compare/<earlier>...0.32.0`