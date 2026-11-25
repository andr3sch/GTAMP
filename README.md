<p align="center">
  <img src="docs/assets/gtamp-logo.png" alt="GTAMP Logo" width="220" />
</p>

# GTAMP — Multiplayer para GTA V Lighter (OptiJuegos)

🌐 **Idiomas:** **🇧🇷 Português** | [🇪🇸 Español](README.es.md) | [🇺🇸 English](README.en.md)

[![Release](https://img.shields.io/github/v/release/andr3sch/GTAMP?style=flat-square&color=2ea44f)](https://github.com/andr3sch/GTAMP/releases/latest)
[![Discord](https://img.shields.io/badge/Discord-Comunidade%20Oficial-5865F2?style=flat-square&logo=discord&logoColor=white)](https://discord.gg/XEqfhjmYUm)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)
[![GTA V](https://img.shields.io/badge/GTA%20V-1.0.463.1%20%2F%201.0.505.2-orange?style=flat-square)](#)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%2F%20SHVDN%202.x-512bd4?style=flat-square)](#)

Multiplayer dedicado em C# (.NET) feito sob medida para **GTA V Lighter (OptiJuegos — Build `1.0.463.1` / `1.0.505.2`)** via **ScriptHookVDotNet 2.x** e **LiteNetLib**, como alternativa leve, estável e moderna a mods como RageCoop — **sem depender de DLCs do GTA V atualizado** e com **proteção total do seu save do Modo História**.

---

## Download e Instalação Rápida

Acesse a [**página de Releases**](https://github.com/andr3sch/GTAMP/releases/latest) e escolha uma opção:

| Pacote | Ideal para | Como usar |
| :--- | :--- | :--- |
| **`GTAMP-Instalador.zip`** | Jogadores (1 clique) | Extraia e execute **`Instalar_GTAMP.bat`**, selecione sua pasta `gta_data` e pronto! |
| **`GTAMP-Portable.zip`** | Jogadores + Donos de Servidor | Contém pastas prontas: `1_Copiar_Para_gta_data`, `2_Launcher` e `3_Servidor`. |

> **Pré-requisito para o Launcher e Servidor:** instale o [**Microsoft .NET 8 Desktop Runtime (x64)**](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) caso ainda não tenha no Windows.

---

## Por que usar o GTAMP no GTA V Lighter?

- **100% compatível com GTA V Lighter (`1.0.463.1`)**: usa apenas *natives*, veículos, skins e animações do jogo base (zero crashes por falta de DLCs).
- **Compatível com Save 100% Concluído**: abre automaticamente interiores de RP via IPL (Concessionária do Simeon, Hospital Pillbox, Vangelico).
- **Isolamento do Modo História**: ao conectar, bloqueia autosave/quick-save (`SET_MISSION_FLAG`) e salva um *snapshot* do personagem; ao desconectar, restaura sua posição, skin, vida, colete e dinheiro originais e remove veículos do servidor.
- **Voz por Proximidade 3D + Rádio nativos**: áudio **16 kHz wideband** com *stereo panning* 3D, modos **Sussurrar / Normal / Gritar**, **Rádio Walkie-Talkie (`/radio`)**, **Push-to-Talk (`N`)** ou **VAD**.
- **Mods & Resources no Servidor (estilo FiveM)**: servidor HTTP embutido com manifesto `/modpack.json` (SHA-256) e cache isolado por servidor no Launcher.

---

## Principais Recursos

- **Sincronização em Tempo Real**: jogadores a pé, veículos (quaternion, faróis, sirene, freio, buzina), disparos, dano PvP, clima/horário e emotes.
- **Menu Interativo estilo NativeUI (`M`)**: navegação focada (bloqueia celular/pause) com Emotes, Vestiário, Concessionária (20 veículos do jogo base), Rádio e Cura.
- **Painel Web Admin**: acesse `http://localhost:4500/admin` (token padrão `gtamp-admin` em `server_config.json`) para ver jogadores, ping, resources, kickar, executar comandos e recarregar scripts.
- **Anti-Cheat Server-Side**: detecção de teleporte não autorizado, HP/Armor absurdos e spam de dano.

---

## Como Colocar Mods no Servidor

Dentro da pasta onde roda o `GTAMP.Server.exe`, existe a pasta `resources/`:

1. **`resources/modpack/` (Carros, Mapas, Skins, DLLs)**:
   - Espelha a pasta `gta_data` do jogador (ex.: `resources/modpack/mods/update/x64/dlcpacks/meucarro/dlc.rpf`).
   - O servidor gera `/modpack.json` automaticamente com SHA-256 e o `GTAMP.Launcher.exe` sincroniza antes de abrir o jogo (limpando mods antigos ao trocar de servidor).
2. **`resources/scripts/*.json` (Lojas, Markers, Menus e Comandos sem programar)**:
   - Crie ou edite JSONs como `concessionaria_exemplo.json` e digite `reload` no console do servidor para aplicar em tempo real.
3. **`resources/plugins/*.dll` (Plugins C#)**:
   - Implemente `IServerResource` (`src/GTAMP.Server/ResourceManager.cs`) e coloque a DLL em `resources/plugins/`.

---

## Teclas e Comandos

| Tecla | Ação |
| :--- | :--- |
| `F9` | Conectar / Desconectar |
| `T` | Abrir Chat |
| `N` | Falar (Push-to-Talk) |
| `Z` | Alternar alcance da voz (Sussurrar / Normal / Gritar / Rádio) |
| `M` | Abrir / Fechar Menu Rápido |
| `E` | Interagir em Markers RP |

**Comandos no chat**: `/help`, `/menu`, `/players`, `/ping`, `/ooc`, `/me`, `/do`, `/g`, `/s`, `/radio <freq|0>`, `/e <dancar|maos|acenar|baterpalma|fumar|sentar|parar>`, `/concessionaria`, `/vestiario`, `/emprego`, `/pagar`, `/depositar`, `/sacar`, `/carro`, `/skin`, `/arma`, `/tp`.

---

## Como Conectar Jogadores de Outros Locais

1. **Radmin VPN / ZeroTier / Tailscale** *(mais fácil, sem mexer no roteador)*:
   - Todos entram na mesma rede virtual; o host abre o `GTAMP.Server.exe` e passa o IP virtual (porta `4499`) para os amigos colocarem no `GTAMP.Launcher.exe`.
2. **Port Forwarding / VPS**:
   - Libere **UDP `4499`** (jogo, chat e voz) e **TCP `4500`** (download de modpack e painel admin) no roteador/firewall.
   - Veja o manual completo passo a passo (VPS Linux com `systemd`, VPS Windows e VPN) em **[docs/HOSTING.md](docs/HOSTING.md)**.

---

## FAQ / Solução de Problemas

- **O Launcher não abre**: instale o [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).
- **Deu `ConnectionFailed` ao conectar**: verifique se o `GTAMP.Server.exe` está aberto, se o Firewall do Windows liberou **UDP `4499`** e se o IP no Launcher está correto.
- **Aviso de `.asi` carregado duplicado ao abrir o GTA**: algumas versões do GTA V Lighter já possuem um ASI loader próprio (usado pelo `OpenIV.asi`); se acontecer, remova apenas o `dinput8.dll` da pasta `gta_data`.

---

## Como Compilar do Código-Fonte

Requer **.NET 8 SDK**:

```bash
dotnet build GTAMP.sln -c Release
```

| Projeto | Alvo | Descrição |
| :--- | :--- | :--- |
| `src/GTAMP.Shared` | `net452;netstandard2.0` | Protocolo de rede (v3), pacotes e codec G.711 μ-law |
| `src/GTAMP.Client` | `net48` (SHVDN 2.x) | Mod cliente in-game (sync, voz 3D, chat, menu NativeUI, RP) |
| `src/GTAMP.Server` | `net8.0` | Servidor dedicado UDP + HTTP Mods/Admin + Resources + Anti-Cheat |
| `src/GTAMP.Launcher` | `net8.0-windows` | Launcher Desktop com server browser, modpack por servidor e config de voz |
| `tools/GTAMP.TestClient` | `net8.0` | Cliente bot de linha de comando para testes |

---

## Aviso Legal (Projeto Não Financeiro)

> **Projeto 100% gratuito, de código aberto e SEM FINS LUCRATIVOS (não financeiro).**
> - O GTAMP é uma modificação feita por fãs apenas para fins educacionais e de entretenimento comunitário, sem qualquer monetização, venda ou arrecadação comercial.
> - Este projeto **não** é afiliado, endossado ou associado à **Rockstar Games**, **Take-Two Interactive** ou **OptiJuegos**.
> - Nenhum arquivo original do jogo é distribuído neste repositório ou nas releases; o mod funciona apenas em sessões privadas/dedicadas e **não** interage com o GTA Online oficial.

## Licença

Distribuído sob a licença **MIT**. Veja [LICENSE](LICENSE) para mais detalhes.
