# KSPMP 0.33.0

Build 0.33.0 (configuration: Release). In-game KSP mod + standalone KSPMPServer. The client zip path matches the GitHub autoupdate asset name.

**Full Changelog**: [0.32.0…0.33.0 on GitHub](https://github.com/ItzPray/KSPMulti/compare/0.32.0...0.33.0)

---

## Highlights

0.33.0 is an **infrastructure and packaging** release: **automatic updates** (client + dedicated server) tied to [GitHub Releases](https://github.com/ItzPray/KSPMulti/releases), with clearer **KSPMP** server branding, **version alignment** between the mod and the server, and small **tooling** additions for local testing. Gameplay and sync work from 0.32.0 is unchanged; this build is the natural follow-up for anyone who needs **reliable, repeatable upgrades** from published zips.

---

## Client (KSP)

- **In-game autoupdate** — Update flow and UI (including localization hooks) to download and install new **KSPMP** client zips, with a Windows **external** update path where needed and safer integration with the install layout.
- **UpdateHandler / repo wiring** — Release checks, asset naming, and `RepoConstants` aligned with **ItzPray/KSPMulti** so the in-game “latest” view matches the published `KSPMultiplayer-Client-*.zip` on GitHub.
- **KSP-AVC** — `KSPMultiplayer.version` and client assembly metadata bumped to **0.33.0** so the mod reports and checks the right version.

---

## Dedicated server (KSPMPServer)

- **Self-update on start / on exit (optional)** — The standalone server can check the latest **GitHub** release, download the server zip, and **merge** it into the current install while **preserving** your data (`Universe`, `Universe_Backup`, `Config`, `logs`, `Backup`, `Plugins`, and the bans file policy already documented in code).
- **Windows deferred apply** — Because the running process locks native DLLs, the helper **`Kspmp-Apply-Server-Update.cmd`** (robocopy) runs after the process exits, with on-screen **steps and pauses** so the window is not a silent flash; PowerShell is used to print **Server.dll** assembly version before/after the merge.
- **Console / process identity** — Titles and log lines use **KSPMP**; server and client `AssemblyInfo` and **session handshake** are aligned so **0.32.x/0.33.x**-style `AssemblyInformationalVersion` differences (e.g. `x.y.z` vs `x.y.z-compiled`) do not block handshakes.

---

## Tooling and release scripts

- **`SetKspmpDevServerVersionBelowRelease.ps1`** — Optional path (default e.g. `C:\KSPMultiServer`); steps the **server** version down, publishes, and robocopy-merges like `PublishServerToTest` so you can **test the self-updater** against a newer GitHub tag.
- **`SetKspmpReleaseVersion.ps1`** — Also updates the **dedicated server** `AssemblyInfo` in lockstep with the client and `KSPMultiplayer.version` (avoids “update loop” and empty fork/version mistakes on future tags).
- **`PublishGitHubRelease.ps1` / packaging** — Tweaks so the release process matches the new asset names and team repo layout.
- **Tests** — `ServerTest` coverage for the server self-updater and handshake admission; `VersionChecker` noise reduced on the server.

---

## Install and upgrade (short)

- **Client** — Unzip the client so `GameData\KSPMultiplayer` and Harmony are as in **`KSPMP Readme.txt`**; keep the game and the published **0.33.0** client in sync.
- **Server** — Unzip the server zip to a **folder of your choice** (not inside KSP’s `GameData`). On first use of self-update, read the helper window: it will show **staged** vs **installed** `Server.dll` version and wait for a keypress when finished.
- **First time from 0.32.x** — You can stay on 0.32.0, or move both client and server to 0.33.0 for the new update flows; **back up** saves and server `Universe` before major upgrades as usual.

---

## Thanks

- Everyone testing **0.32.0** in the wild, and to the **KSPMP** contributors and reporters who helped validate packaging and handshakes.
- The **Luna Multiplayer** lineage; this release is built on that foundation.

---

**Compare range (after tagging 0.33.0 on GitHub):**  
<https://github.com/ItzPray/KSPMulti/compare/0.32.0...0.33.0>

**Prior release (high-level list):** see [`FirstRelease_0.32.0.md`](./FirstRelease_0.32.0.md) for the full 0.32.0 “what changed from legacy LMP” narrative.
