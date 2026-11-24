# GTAMP — Multiplayer for GTA V Lighter (OptiJuegos)

🌐 **Languages:** [🇧🇷 Português](README.md) | [🇪🇸 Español](README.es.md) | **🇺🇸 English**

[![Release](https://img.shields.io/github/v/release/andr3sch/GTAMP?style=flat-square&color=2ea44f)](https://github.com/andr3sch/GTAMP/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)
[![GTA V](https://img.shields.io/badge/GTA%20V-1.0.463.1%20%2F%201.0.505.2-orange?style=flat-square)](#)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%2F%20SHVDN%202.x-512bd4?style=flat-square)](#)

A dedicated C# (.NET) multiplayer mod built specifically for **GTA V Lighter (OptiJuegos — Build `1.0.463.1` / `1.0.505.2`)** using **ScriptHookVDotNet 2.x** and **LiteNetLib**, designed as a lightweight, modern alternative to mods like RageCoop — **with zero dependency on newer GTA V DLCs** and **full protection for your Story Mode save**.

---

## Quick Download & Installation

Visit the [**Releases page**](https://github.com/andr3sch/GTAMP/releases/latest) and choose a package:

| Package | Best for | How to use |
| :--- | :--- | :--- |
| **`GTAMP-Instalador.zip`** | Players (1-click) | Extract and run **`Instalar_GTAMP.bat`**, pick your `gta_data` folder, and play! |
| **`GTAMP-Portable.zip`** | Players + Server Hosts | Pre-organized folders: `1_Copiar_Para_gta_data`, `2_Launcher`, and `3_Servidor`. |

> **Prerequisite for Launcher & Server:** install [**Microsoft .NET 8 Desktop Runtime (x64)**](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) if not already installed.

---

## Why GTAMP for GTA V Lighter?

- **100% GTA V Lighter (`1.0.463.1`) compatible**: relies only on base-game natives, vehicles, peds, and animations (no crashes from missing DLCs).
- **100% Completed Save Friendly**: automatically loads open RP interiors via IPL (Simeon's Dealership, Pillbox Hospital, Vangelico).
- **Story Mode Save Isolation**: enables `SET_MISSION_FLAG` (disabling autosave/quick-save) and takes a player snapshot on connect, restoring your original Story Mode position, skin, health, armor, and money upon disconnect.
- **Built-in 3D Proximity Voice + Radio**: **16 kHz wideband** audio with 3D stereo panning, **Whisper / Normal / Shout** ranges, **Walkie-Talkie Radio (`/radio`)**, **Push-to-Talk (`N`)**, and **Voice Activity Detection (VAD)**.
- **FiveM-Style Server Mods & Resources**: built-in HTTP server with auto-generated `/modpack.json` (SHA-256) and per-server isolated modpack caching in the Launcher.

---

## Key Features

- **Real-time Sync**: on-foot players, vehicles (quaternion rotation, lights, sirens, brakes, horn), gunshots, PvP damage, weather/time, and synced emotes.
- **NativeUI-Style Interactive Menu (`M`)**: focused navigation (blocks phone/pause conflicts) with Emotes, Wardrobe, Dealership (20 base-game vehicles), Radio, and Heal.
- **Web Admin Panel**: available at `http://localhost:4500/admin` (default token `gtamp-admin` in `server_config.json`) to monitor players, ping, resources, kick players, run commands, and hot-reload scripts.
- **Server-Side Anti-Cheat**: detects unauthorized teleportation, abnormal health/armor, and damage spam.

---

## Adding Mods to Your Server

Inside the `GTAMP.Server.exe` directory, use the `resources/` folder:

1. **`resources/modpack/` (Cars, Maps, Skins, Client DLLs)**:
   - Mirrors the player's `gta_data` directory (e.g. `resources/modpack/mods/update/x64/dlcpacks/mycar/dlc.rpf`).
   - The server auto-generates `/modpack.json` with SHA-256 hashes, and `GTAMP.Launcher.exe` syncs files automatically before launching GTA V.
2. **`resources/scripts/*.json` (No-code Markers, Shops, Menus & Commands)**:
   - Add or edit JSON definitions and type `reload` in the server console for live updates.
3. **`resources/plugins/*.dll` (C# Server Plugins)**:
   - Implement `IServerResource` (`src/GTAMP.Server/ResourceManager.cs`) and drop your DLL into `resources/plugins/`.

---

## Controls & Commands

| Key | Action |
| :--- | :--- |
| `F9` | Connect / Disconnect |
| `T` | Open Chat |
| `N` | Talk (Push-to-Talk) |
| `Z` | Cycle Voice Range (Whisper / Normal / Shout / Radio) |
| `M` | Toggle Quick Menu |
| `E` | Interact at RP Markers |

---

## Connecting Remote Players

1. **Radmin VPN / ZeroTier / Tailscale** *(easiest — no router config)*: join the same virtual network and connect using the host's virtual IP on port `4499`.
2. **Port Forwarding / VPS**: forward **UDP `4499`** (game/chat/voice) and **TCP `4500`** (HTTP modpacks & admin panel).

---

## Building from Source

Requires **.NET 8 SDK**:

```bash
dotnet build GTAMP.sln -c Release
```

---

## Legal Disclaimer (Non-Commercial / Non-Profit)

> **This project is 100% free, open-source, and strictly NON-COMMERCIAL / NON-PROFIT.**
> - GTAMP is a fan-made modification created solely for educational and community entertainment purposes, with no monetization, sales, or commercial intent.
> - This project is **not** affiliated with, endorsed by, or associated with **Rockstar Games**, **Take-Two Interactive**, or **OptiJuegos**.
> - No copyrighted game files are distributed in this repository or its releases; the mod operates exclusively on private/dedicated servers and does **not** interact with official GTA Online.

## License

Licensed under the **MIT License**. See [LICENSE](LICENSE) for details.
