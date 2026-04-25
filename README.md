<p align="center">
    <img src="External/logo.png" alt="KSP Multiplayer logo"/>
</p>

<p align="center">
    <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/ItzPray/KSPMulti?style=flat&logo=github&logoColor=white" alt="Latest release" /></a>
    <a href="../../releases"><img src="https://img.shields.io/github/downloads/ItzPray/KSPMulti/total.svg?style=flat&logo=github&logoColor=white" alt="Total downloads" /></a>
    <a href="./LICENSE"><img src="https://img.shields.io/github/license/ItzPray/KSPMulti.svg?style=flat" alt="License" /></a>
</p>

---

# KSP Multiplayer (KSPMP)

*KSP Multiplayer is a quality-of-life and stability-focused fork of [Luna Multiplayer (LMP)](https://github.com/LunaMultiplayer/LunaMultiplayer), the multiplayer mod for [Kerbal Space Program](https://kerbalspaceprogram.com).*  

This fork keeps the core LMP multiplayer experience while modernizing the project presentation around KSPMP and carrying forward improvements aimed at smoother play, easier maintenance, and more reliable hosting.

## What this fork focuses on

- Quality-of-life improvements for players and server hosts.
- Stability fixes for long-running multiplayer sessions.
- Cleaner project identity and repository links under KSP Multiplayer / KSPMP.
- Continued compatibility with the proven LMP client/server architecture.
- Maintenance-friendly changes that make the codebase easier to understand, build, and extend.

## Core multiplayer features

- Client/server multiplayer for Kerbal Space Program.
- UDP networking through Lidgren for reliable message handling.
- Time synchronization between clients and servers.
- Vessel interpolation to reduce visible jumps during poor network conditions.
- IPv6 support for client/server connections.
- UPnP support for easier server hosting where available.
- Shared career and science mode state, including funds, science, strategies, and related progression data.
- Multilanguage support.
- XML-backed settings.
- Cached network messages and compression paths to reduce garbage collection spikes.
- Task-based architecture for background work.

## Installation

1. Download the latest build from the [Releases](../../releases/latest) page.
2. Install the mod into your Kerbal Space Program `GameData` folder.
3. Start KSP and configure KSP Multiplayer from the in-game mod UI.
4. Join a compatible server, or host your own server using the included server components.

## Documentation

The existing [wiki](../../wiki) contains installation, gameplay, build, and troubleshooting notes. Some inherited pages may still reference Luna Multiplayer or LMP naming, but this repository is maintained as KSP Multiplayer / KSPMP.

## Troubleshooting

For common issues, start with the [Troubleshooting](../../wiki/Troubleshooting) page. When reporting bugs, include your KSP version, mod version, installed mod list, server/client logs, and reproduction steps where possible.

## Contributing

Contributions are welcome, especially fixes that improve stability, maintainability, compatibility, and quality of life.

Please keep changes readable and documented enough that another maintainer can understand them later. There is also a test project available for changes that benefit from automated coverage.

## Status

| Branch | Build | Tests | Last commit | Activity | Commits |
| ------ | ----- | ----- | ----------- | -------- | ------- |
| **main** | [![AppVeyor](https://img.shields.io/appveyor/ci/ItzPray/KSPMulti/main.svg?style=flat&logo=appveyor)](https://ci.appveyor.com/project/ItzPray/KSPMulti/branch/main) | [![AppVeyor Tests](https://img.shields.io/appveyor/tests/ItzPray/KSPMulti/main.svg?style=flat&logo=appveyor)](https://ci.appveyor.com/project/ItzPray/KSPMulti/branch/main/tests) | [![Last commit](https://img.shields.io/github/last-commit/ItzPray/KSPMulti/main.svg?style=flat&logo=github&logoColor=white)](../../commits/main) | [![Commit activity](https://img.shields.io/github/commit-activity/y/ItzPray/KSPMulti.svg?style=flat&logo=github&logoColor=white)](../../commits/main) | [![Commits since release](https://img.shields.io/github/commits-since/ItzPray/KSPMulti/latest.svg?style=flat&logo=github&logoColor=white)](../../commits/main) |

---

<p align="center">
  <a href="./LICENSE"><img src="https://img.shields.io/github/license/ItzPray/KSPMulti.svg?style=flat" alt="License" /></a>
</p>
