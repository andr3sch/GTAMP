# GTAMP — Multijugador para GTA V Lighter (OptiJuegos)

🌐 **Idiomas:** [🇧🇷 Português](README.md) | **🇪🇸 Español** | [🇺🇸 English](README.en.md)

[![Release](https://img.shields.io/github/v/release/andr3sch/GTAMP?style=flat-square&color=2ea44f)](https://github.com/andr3sch/GTAMP/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)
[![GTA V](https://img.shields.io/badge/GTA%20V-1.0.463.1%20%2F%201.0.505.2-orange?style=flat-square)](#)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%2F%20SHVDN%202.x-512bd4?style=flat-square)](#)

Multijugador dedicado en C# (.NET) diseñado específicamente para **GTA V Lighter (OptiJuegos — Build `1.0.463.1` / `1.0.505.2`)** mediante **ScriptHookVDotNet 2.x** y **LiteNetLib**, como alternativa ligera, estable y moderna a mods como RageCoop — **sin depender de DLCs del GTA V actualizado** y con **protección total de tu partida del Modo Historia**.

---

## Descarga e Instalación Rápida

Ve a la [**página de Releases**](https://github.com/andr3sch/GTAMP/releases/latest) y elige una opción:

| Paquete | Ideal para | Cómo usar |
| :--- | :--- | :--- |
| **`GTAMP-Instalador.zip`** | Jugadores (1 clic) | Extrae y ejecuta **`Instalar_GTAMP.bat`**, selecciona tu carpeta `gta_data` ¡y listo! |
| **`GTAMP-Portable.zip`** | Jugadores + Hosts de Servidor | Incluye carpetas listas: `1_Copiar_Para_gta_data`, `2_Launcher` y `3_Servidor`. |

> **Requisito previo para el Launcher y el Servidor:** instala [**Microsoft .NET 8 Desktop Runtime (x64)**](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) si aún no lo tienes en Windows.

---

## ¿Por qué usar GTAMP en GTA V Lighter?

- **100% compatible con GTA V Lighter (`1.0.463.1`)**: usa únicamente *natives*, vehículos, skins y animaciones del juego base (cero cierres/crashes por falta de DLCs).
- **Compatible con Partidas al 100%**: abre automáticamente los interiores clásicos de RP mediante IPL (Concesionaria de Simeon, Hospital Pillbox, Joyería Vangelico).
- **Aislamiento del Modo Historia**: al conectarte, bloquea el autoguardado y el guardado rápido (`SET_MISSION_FLAG`) y guarda una copia temporal (*snapshot*) de tu personaje; al desconectarte, restaura tu posición, skin, vida, chaleco y dinero originales y elimina los vehículos del servidor.
- **Voz por Proximidad 3D + Radio integrados**: audio **16 kHz wideband** con *stereo panning* 3D, modos **Susurrar / Normal / Gritar**, **Radio Walkie-Talkie (`/radio`)**, **Push-to-Talk (`N`)** o **Activación por Voz (VAD)**.
- **Mods y Resources en el Servidor (estilo FiveM)**: servidor HTTP integrado con manifiesto `/modpack.json` (SHA-256) y caché aislada por servidor en el Launcher.

---

## Características Principales

- **Sincronización en Tiempo Real**: jugadores a pie, vehículos (quaternion, luces, sirena, frenos, bocina), disparos, daño PvP, clima/hora y emotes.
- **Menú Interactivo estilo NativeUI (`M`)**: navegación enfocada (bloquea el celular y la pausa) con Emotes, Vestuario, Concesionaria (20 vehículos del juego base), Radio y Curación.
- **Panel Web Admin**: entra a `http://localhost:4500/admin` (token por defecto `gtamp-admin` en `server_config.json`) para ver jugadores, ping, resources, expulsar, ejecutar comandos y recargar scripts.
- **Anti-Cheat Server-Side**: detección de teletransporte no autorizado, HP/chaleco imposibles y spam de daño.

---

## Cómo Poner Mods en el Servidor

Dentro de la carpeta donde ejecutas `GTAMP.Server.exe`, encontrarás la carpeta `resources/`:

1. **`resources/modpack/` (Autos, Mapas, Skins, DLLs)**:
   - Replica la estructura de la carpeta `gta_data` del jugador (ej.: `resources/modpack/mods/update/x64/dlcpacks/miauto/dlc.rpf`).
   - El servidor genera `/modpack.json` automáticamente con SHA-256 y `GTAMP.Launcher.exe` sincroniza todo antes de abrir el juego (limpiando los mods anteriores al cambiar de servidor).
2. **`resources/scripts/*.json` (Tiendas, Markers, Menús y Comandos sin programar)**:
   - Crea o edita archivos JSON como `concessionaria_exemplo.json` y escribe `reload` en la consola del servidor para aplicarlos al instante.
3. **`resources/plugins/*.dll` (Plugins C#)**:
   - Implementa `IServerResource` (`src/GTAMP.Server/ResourceManager.cs`) y coloca tu DLL en `resources/plugins/`.

---

## Teclas y Comandos

| Tecla | Acción |
| :--- | :--- |
| `F9` | Conectar / Desconectar |
| `T` | Abrir Chat |
| `N` | Hablar (Push-to-Talk) |
| `Z` | Cambiar alcance de voz (Susurrar / Normal / Gritar / Radio) |
| `M` | Abrir / Cerrar Menú Rápido |
| `E` | Interactuar en Markers RP |

**Comandos en el chat**: `/help`, `/menu`, `/players`, `/ping`, `/ooc`, `/me`, `/do`, `/g`, `/s`, `/radio <frec|0>`, `/e <dancar|maos|acenar|baterpalma|fumar|sentar|parar>`, `/concessionaria`, `/vestiario`, `/emprego`, `/pagar`, `/depositar`, `/sacar`, `/carro`, `/skin`, `/arma`, `/tp`.

---

## Cómo Conectar Jugadores desde Otros Lugares

1. **Radmin VPN / ZeroTier / Tailscale** *(lo más fácil, sin tocar el router)*:
   - Todos entran a la misma red virtual; el host abre `GTAMP.Server.exe` y pasa su IP virtual (puerto `4499`) para que sus amigos la pongan en `GTAMP.Launcher.exe`.
2. **Port Forwarding / VPS**:
   - Abre **UDP `4499`** (juego, chat y voz) y **TCP `4500`** (descarga de modpack y panel admin) en el router/firewall.
   - Consulta la guía completa paso a paso (VPS Linux con `systemd`, VPS Windows y VPN) en **[docs/HOSTING.es.md](docs/HOSTING.es.md)**.

---

## Preguntas Frecuentes / Solución de Problemas

- **El Launcher no abre**: instala [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).
- **Sale `ConnectionFailed` al conectar**: verifica que `GTAMP.Server.exe` esté abierto, que el Firewall de Windows permita **UDP `4499`** y que la IP en el Launcher sea correcta.
- **Aviso de `.asi` duplicado al iniciar GTA**: algunas versiones de GTA V Lighter ya traen su propio ASI loader (usado por `OpenIV.asi`); si ocurre, elimina solo `dinput8.dll` de la carpeta `gta_data`.

---

## Cómo Compilar desde el Código Fuente

Requiere **.NET 8 SDK**:

```bash
dotnet build GTAMP.sln -c Release
```

---

## Aviso Legal (Proyecto Sin Fines de Lucro)

> **Proyecto 100% gratuito, de código abierto y SIN FINES DE LUCRO (no financiero).**
> - GTAMP es una modificación hecha por fans únicamente con fines educativos y de entretenimiento comunitario, sin ningún tipo de monetización, venta o beneficio comercial.
> - Este proyecto **no** está afiliado, respaldado ni asociado con **Rockstar Games**, **Take-Two Interactive** ni **OptiJuegos**.
> - No se distribuye ningún archivo original del juego en este repositorio ni en las releases; el mod funciona exclusivamente en servidores privados/dedicados y **no** interactúa con el GTA Online oficial.

## Licencia

Distribuido bajo licencia **MIT**. Consulta [LICENSE](LICENSE) para más detalles.
