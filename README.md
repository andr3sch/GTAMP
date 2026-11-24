# GTAMP — Multiplayer para GTA V Lighter (OptiJuegos)

Multiplayer dedicado em C# (.NET) projetado especificamente para **GTA V Lighter (Build `1.0.463.1` / `1.0.505.2`)** via **ScriptHookVDotNet 2.x** e **LiteNetLib**, sem dependência de DLCs do GTA V moderno e com proteção total do save do Modo História.

---

## Principais Recursos

- **Sincronização em Tempo Real**: jogadores a pé, veículos (com rotação quaternion, faróis, sirene, freio, buzina), disparos, dano PvP, clima/horário e animações/emotes.
- **Voz por Proximidade 2.0 + Rádio (Walkie-Talkie)**:
  - Áudio **16 kHz wideband** com mixer stereo único (`MixingSampleProvider`).
  - **Áudio 3D espacial (Stereo Panning)** relativo à câmera + atenuação suave.
  - Modos alternáveis (`Z`): **Sussurrar (6m)** → **Normal** → **Gritar (45m)** → **Rádio**.
  - Frequências de rádio (`/radio <1-9999>`) com filtro de rádio + leve estática.
  - Suporte a **Push-to-Talk (`N`)** e **Ativação por Voz (VAD)**.
- **Mods & Resources no Servidor (estilo FiveM)**:
  - Servidor HTTP embutido (`/modpack.json` com SHA-256 automático) para distribuir mods colocados em `resources/modpack/`.
  - **Isolamento de Modpack por Servidor** no Launcher (cache separado por servidor e limpeza automática ao trocar de servidor).
  - **Scripts JSON** (`resources/scripts/*.json`) para criar markers 3D, blips, concessionárias, lojas, menus e comandos sem programar.
  - **Plugins DLL C#** (`resources/plugins/*.dll`) com hot-reload (`reload`).
- **Menu Interativo estilo NativeUI (`M`)**: navegação focada (bloqueia celular/pause) com Emotes, Vestiário (skins 100% do jogo base), Concessionária (20 veículos do jogo base), Rádio e Cura.
- **Painel Web Admin**: acessível em `http://localhost:4500/admin` para monitorar jogadores, ping, resources, executar comandos, kickar e recarregar scripts.
- **Isolamento do Modo História**: ativa `SET_MISSION_FLAG(true)` e salva um *snapshot* do jogador ao conectar, restaurando posição, skin, vida, colete e dinheiro ao desconectar — nada do servidor afeta seu save real.
- **Compatível com Save 100% Concluído**: abre interiores clássicos de RP via IPL (Concessionária do Simeon, Hospital Pillbox, Vangelico).
- **Anti-Cheat Server-Side**: detecção de teleporte não autorizado, HP/Armor absurdos e spam de dano.

---

## Estrutura da Solution

| Projeto | Alvo | Descrição |
| :--- | :--- | :--- |
| `src/GTAMP.Shared` | `net452;netstandard2.0` | Protocolo de rede (v3), pacotes e codec G.711 μ-law |
| `src/GTAMP.Client` | `net48` (SHVDN 2.x) | Mod cliente in-game (sync, voz 3D, chat, menu NativeUI, RP) |
| `src/GTAMP.Server` | `net8.0` | Servidor dedicado UDP + HTTP Mods/Admin + Resources + Anti-Cheat |
| `src/GTAMP.Launcher` | `net8.0-windows` | Launcher Desktop com server browser, modpack por servidor e config de voz |
| `tools/GTAMP.TestClient` | `net8.0` | Cliente bot de linha de comando para testes de carga/rede |

---

## Como Compilar

Requer **.NET 8 SDK**:

```bash
dotnet build GTAMP.sln -c Release
```

- **Mod Cliente**: copie `GTAMP.Client.dll`, `GTAMP.Shared.dll`, `LiteNetLib.dll` e `NAudio.dll` de `src/GTAMP.Client/bin/Release/net48/` para `gta_data/scripts/`.
- **Dependências no `gta_data/`**: `ScriptHookV.dll`, `dinput8.dll`, `ScriptHookVDotNet.asi`, `ScriptHookVDotNet.ini`, `ScriptHookVDotNet2.dll`, `ScriptHookVDotNet3.dll`.

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

1. **Radmin VPN / ZeroTier / Tailscale** *(mais fácil)*: todos entram na mesma rede virtual e conectam pelo IP virtual na porta `4499`.
2. **Port Forwarding / VPS**: libere **UDP `4499`** (jogo/voz) e **TCP `4500`** (modpack HTTP / painel admin).
