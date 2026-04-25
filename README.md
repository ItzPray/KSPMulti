<p align="center">
    <img src="../master/External/logo.png" alt="KSP Multiplayer logo"/>
    <a href="https://www.youtube.com/watch?v=rmJL_c-EJK8"><img src="https://img.youtube.com/vi/rmJL_c-EJK8/0.jpg" alt="Video" height="187" width="250"/></a>    
    <a href="https://www.youtube.com/watch?v=gf6xyLnpnoM"><img src="https://img.youtube.com/vi/gf6xyLnpnoM/0.jpg" alt="Video" height="187" width="250"/></a>
</p>

<p align="center">
    <a href="https://paypal.me/gavazquez"><img src="https://img.shields.io/badge/paypal-donate-yellow.svg?style=flat&logo=paypal" alt="PayPal"/></a>
    <a href="https://discord.gg/wKVMhWQ"><img src="https://img.shields.io/discord/378456662392045571.svg?style=flat&logo=discord&label=discord" alt="Chat on discord"/></a>
    <a href="../../releases"><img src="https://img.shields.io/github/release/lunamultiplayer/lunamultiplayer.svg?style=flat&logo=github&logoColor=white" alt="Latest release" /></a>
    <a href="../../releases"><img src="https://img.shields.io/github/downloads/lunamultiplayer/lunamultiplayer/total.svg?style=flat&logo=github&logoColor=white" alt="Total downloads" /></a>
</p>

<p align="center">
    <a href="https://forum.kerbalspaceprogram.com/index.php?/topic/168271-131-luna-multiplayer-lmp-alpha"><img src="https://img.shields.io/badge/KSP%20Forum-Post-4265f4.svg?style=flat" alt="KSP forum post"/></a>
    <a href="https://github.com/LunaMultiplayer/LunaMultiplayerUpdater"><img src="https://img.shields.io/badge/Automatic-Updater-4265f4.svg?style=flat" alt="Latest build updater"/></a>
</p>

---

<p align="center">
  <a href="../../releases/latest"><img src="../master/External/downloadIcon.png" alt="Download" height="85" width="300"/></a>
  <a href="../../wiki"><img src="../master/External/documentationIcon.png" alt="Documentation" height="85" width="353"/></a>
</p>

---

# KSP Multiplayer (KSPMP)

*Multiplayer mod for [Kerbal Space Program (KSP)](https://kerbalspaceprogram.com)*

### Main features:

- [x] Clean and optimized code, based on systems and windows which makes it easier to read and modify.
- [x] Multi threaded.
- [x] [NTP](https://en.wikipedia.org/wiki/Network_Time_Protocol) protocol to sync the time between clients and the server.
- [x] [UDP](https://en.wikipedia.org/wiki/User_Datagram_Protocol) based using the [Lidgren](https://github.com/lidgren/lidgren-network-gen3) library for reliable UDP message handling.
- [x] [Interpolation](http://www.gabrielgambetta.com/entity-interpolation.html) so the vessels won't jump when there are bad network conditions.
- [x] Multilanguage.
- [x] [Nat-punchtrough](../../wiki/Master-server) feature so a server doesn't need to open ports on it's router.
- [x] [IPv6](https://en.wikipedia.org/wiki/IPv6) support for client<->server connections, allowing connection setup even behind symmetric IPv4 NAT
- [x] Servers displayed within the mod.
- [x] Settings saved as XML.
- [x] [UPnP](https://en.wikipedia.org/wiki/Universal_Plug_and_Play) support for servers and [master servers](../../wiki/Master-server)
- [x] Better creation of network messages so they are easier to modify and serialize.
- [x] Every network message is cached in order to reduce the garbage collector spikes.
- [x] Based on tasks instead of threads.
- [x] Supports career and science modes (funds, science, strategies, etc are shared between all players).
- [x] Cached [QuickLZ](http://www.quicklz.com) for fast compression without generating garbage.
- [ ] Support for groups/companies inside career and science modes.

Please check the [wiki](../../wiki) for how to install, play, build, and debug (documentation still references the original fork naming in some URLs; the mod in-game and in this repo is **KSP Multiplayer / KSPMP**).

---
### Troubleshooting:

Please visit [this page](../../wiki/Troubleshooting) in the wiki to solve the most common issues with KSP Multiplayer 
[![Analytics](https://ga-beacon.appspot.com/UA-118326748-1/Home?pixel&useReferer)](https://github.com/igrigorik/ga-beacon)

---
### Contributing:

Consider [donating trough paypal](https://paypal.me/gavazquez) if you like this project. 
It will encourage us to do future releases, fix bugs and add new features :star:

Please write the code as you were going to leave it, return after 1 year and you'd have to understand what you wrote.  
It's **very** important that the code is clean and documented so in case someone leaves, another programmer could take and maintain it. Bear in mind that **nobody** likes to take a project where it's code looks like a dumpster.

There's also a test project in case you want to add tests to your code.

---
### Servers:

You can check [how many servers are up](../../wiki/Master-server-status) and running either in [Release](../../wiki/How-to-get-the-latest-version-of-LMP) or in [Nightly](../../wiki/How-to-get-nightly-builds) versions through our [master servers](../../wiki/Master-server)

| Master server | Release | Nightly |
| ------------  | ------- |-------- |
[Dagger](https://github.com/gavazquez) | [![Release servers](https://img.shields.io/website-up-down-brightgreen-red/http/servers.lunamultiplayer.com:8701.svg?label=status)](http://servers.lunamultiplayer.com:8701) | [![Nightly servers](https://img.shields.io/website-up-down-brightgreen-red/http/servers.lunamultiplayer.com:8751.svg?label=status)](http://servers.lunamultiplayer.com:8751) |
Tekbot | [![Release servers](https://img.shields.io/website-up-down-brightgreen-red/http/168.119.90.137:8701.svg?label=status)](http://168.119.90.137:8701) | [![Nightly servers](https://img.shields.io/website-up-down-brightgreen-red/http/168.119.90.137:8751.svg?label=status)](http://168.119.90.137:8751) |
[Angryjoshi](https://github.com/Angryjoshi) | [![Release servers](https://img.shields.io/website-up-down-brightgreen-red/http/lmp.anschuetznet.de:8701.svg?label=status)](http://lmp.anschuetznet.de:8701) | [![Nightly servers](https://img.shields.io/website-up-down-brightgreen-red/http/lmp.anschuetznet.de:8751.svg?label=status)](http://lmp.anschuetznet.de:8751) |
Piro | [![Release servers](https://img.shields.io/website-up-down-brightgreen-red/http/91.126.41.111:8701.svg?label=status)](http://91.126.41.111:8701) | [![Nightly servers](https://img.shields.io/website-up-down-brightgreen-red/http/91.126.41.111:8751.svg?label=status)](http://91.126.41.111:8751) |
[Bloodfallen](https://github.com/Bloodfallen) | [![Release servers](https://img.shields.io/website-up-down-brightgreen-red/http/direct.imexile.moe:8701.svg?label=status)](http://direct.imexile.moe:8701) | [![Nightly servers](https://img.shields.io/website-up-down-brightgreen-red/http/direct.imexile.moe:8703.svg?label=status)](http://direct.imexile.moe:8703)|

---
### Status:

`main` ― [GitHub Actions](https://github.com/ItzPray/KSPMulti/actions/workflows/ci.yml) runs the **dedicated server** `dotnet test` (`ServerTest`) and does **not** require KSP/Unity `Managed` DLLs. For a full **LmpClient** build, use your local `External/KSPLibraries` (see [AGENTS.md](AGENTS.md)) or a separate AppVeyor/VM pipeline; you do not need AppVeyor for this badge.

|   Branch  |  Build  |  Tests  |  Last commit  |   Activity    |    Commits    |
| --------- | ------- | ------- | ------------- | ------------- | ------------- |
| **main** | [![CI](https://github.com/ItzPray/KSPMulti/actions/workflows/ci.yml/badge.svg)](https://github.com/ItzPray/KSPMulti/actions/workflows/ci.yml) | [![CI](https://github.com/ItzPray/KSPMulti/actions/workflows/ci.yml/badge.svg)](https://github.com/ItzPray/KSPMulti/actions/workflows/ci.yml) | [![Last commit](https://img.shields.io/github/last-commit/ItzPray/KSPMulti.svg?style=flat&logo=github&logoColor=white)](../../commits) | [![Commit activity](https://img.shields.io/github/commit-activity/y/ItzPray/KSPMulti.svg?style=flat&logo=github&logoColor=white)](../../commits) | [![Commits since release](https://img.shields.io/github/commits-since/ItzPray/KSPMulti/latest.svg?style=flat&logo=github&logoColor=white)](../../commits) |

---

<p align="center">
  <a href="mailto:gavazquez@gmail.com"><img src="https://img.shields.io/badge/email-gavazquez@gmail.com-blue.svg?style=flat" alt="Email: gavazquez@gmail.com" /></a>
  <a href="./LICENSE"><img src="https://img.shields.io/github/license/lunamultiplayer/LunaMultiPlayer.svg" alt="License" /></a>
</p>
