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

*KSP Multiplayer was originally created as a fork of [Luna Multiplayer (LMP)](https://github.com/LunaMultiplayer/LunaMultiplayer), the multiplayer mod for [Kerbal Space Program](https://kerbalspaceprogram.com).*  

At the time, LMP appeared to be inactive, so this repository was a place to experiment with fixes and keep multiplayer playable.

## Current Plan

Since Luna Multiplayer is active again, future development for these fixes will be moved back to the main LMP project.

The goal is to:

1. Use this repo as a reference for existing work
2. Bring useful changes back upstream where appropriate
3. Keep future development focused on the main LMP repository

## Status

This repo may still contain experimental or in-progress changes. Some fixes may be moved over to LMP after review.

For now, this repo is mainly being kept public so the existing work can be reviewed and referenced while development moves back upstream.

## Upstream Credit

KSPMulti is based on the [LunaMultiplayer/LunaMultiplayer](https://github.com/LunaMultiplayer/LunaMultiplayer) project. Original authorship and a substantial portion of this codebase come from Luna Multiplayer and its contributors.

## What this fork focuses on

- Quality-of-life improvements for players and server hosts.
- Stability fixes for long-running multiplayer sessions.
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

The existing [wiki](../../wiki) contains installation, gameplay, build, and troubleshooting notes. Some inherited pages may still reference Luna Multiplayer or LMP naming.

## Troubleshooting

For common issues, start with the [Troubleshooting](../../wiki/Troubleshooting) page. When reporting bugs, include your KSP version, mod version, installed mod list, server/client logs, and reproduction steps where possible.

## Contributing

Thanks to the LMP team for bringing the project back into active development. I’m happy to contribute my fixes and continue helping with multiplayer improvements there.


|   Branch  |  Build  |  Tests  |  Last commit  |   Activity    |    Commits    |
| --------- | ------- | ------- | ------------- | ------------- | ------------- |
| **main** | [![CI](https://github.com/ItzPray/KSPMulti/actions/workflows/ci.yml/badge.svg)](https://github.com/ItzPray/KSPMulti/actions/workflows/ci.yml) | [![CI](https://github.com/ItzPray/KSPMulti/actions/workflows/ci.yml/badge.svg)](https://github.com/ItzPray/KSPMulti/actions/workflows/ci.yml) | [![Last commit](https://img.shields.io/github/last-commit/ItzPray/KSPMulti.svg?style=flat&logo=github&logoColor=white)](../../commits) | [![Commit activity](https://img.shields.io/github/commit-activity/y/ItzPray/KSPMulti.svg?style=flat&logo=github&logoColor=white)](../../commits) | [![Commits since release](https://img.shields.io/github/commits-since/ItzPray/KSPMulti/latest.svg?style=flat&logo=github&logoColor=white)](../../commits) |


---
