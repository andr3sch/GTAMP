# Hosting Guide — GTAMP Server (Linux VPS, Windows & VPN)

🌐 **Languages:** [🇧🇷 Português](HOSTING.md) | [🇪🇸 Español](HOSTING.es.md) | **🇺🇸 English**

This guide explains how to host a 24/7 **GTAMP** dedicated server on a Linux VPS, Windows VPS, or your own PC.

---

## Minimum Server Requirements

`GTAMP.Server` runs on **.NET 8** and is very lightweight:
- **CPU**: 1 vCPU
- **RAM**: 512 MB – 1 GB (enough for dozens of players)
- **Ports**:
  - **UDP `4499`** *(required)* — gameplay, chat, and proximity/radio voice
  - **TCP `4500`** *(optional)* — built-in HTTP modpack server (`/modpack.json`) and Web Admin Panel (`/admin`)

---

## 1. Hosting on a Linux VPS (Ubuntu / Debian) — Recommended

### Step 1: Install dependencies
```bash
sudo apt update && sudo apt install -y git screen dotnet-sdk-8.0
```
> If your Ubuntu/Debian repository does not include `dotnet-sdk-8.0`, use Microsoft's official install script:
> ```bash
> curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
> export PATH="$HOME/.dotnet:$PATH"
> ```

### Step 2: Clone and build the server
```bash
git clone https://github.com/andr3sch/GTAMP.git
cd GTAMP
dotnet publish src/GTAMP.Server/GTAMP.Server.csproj -c Release -o ~/gtamp-server
```

### Step 3: Open firewall ports
Open these ports both on the OS firewall and in your **VPS provider's panel / Security Group** (Oracle Cloud, AWS, Contabo, DigitalOcean, etc.):
```bash
sudo ufw allow 4499/udp
sudo ufw allow 4500/tcp
```

### Step 4: Configure `server_config.json`
Run once to generate default files, then type `stop`:
```bash
cd ~/gtamp-server
./GTAMP.Server
```
Edit `nano ~/gtamp-server/server_config.json`:
- **`ServerName`** / **`Motd`**: server name and welcome message.
- **`PublicHost`**: your **VPS public IP or domain** (used for auto-generated `/modpack.json` URLs).
- **`AdminToken`**: **change `"gtamp-admin"` to a strong password!**
- **`HttpEnabled`**: `true` to serve mods from `resources/modpack/` and the `/admin` panel.

### Step 5: Run 24/7 with `systemd` (auto-restart)
Create the service:
```bash
sudo tee /etc/systemd/system/gtamp.service > /dev/null <<EOF
[Unit]
Description=GTAMP Dedicated Server
After=network.target

[Service]
Type=simple
WorkingDirectory=$HOME/gtamp-server
ExecStart=$HOME/gtamp-server/GTAMP.Server
Restart=always
RestartSec=5
User=$USER

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable --now gtamp
```

Useful commands:
```bash
sudo systemctl status gtamp      # check status
journalctl -u gtamp -f           # follow live logs
sudo systemctl restart gtamp     # restart server
```

*(Quick alternative without systemd: run `screen -S gtamp`, start `./GTAMP.Server`, and press `Ctrl+A` then `D` to detach.)*

---

## 2. Hosting on a Windows VPS / Windows PC

1. Install [**Microsoft .NET 8 Runtime (x64)**](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).
2. Download `GTAMP-Portable.zip` from the [Releases page](https://github.com/andr3sch/GTAMP/releases/latest) and extract the **`3_Servidor`** folder.
3. Allow **UDP `4499`** and **TCP `4500`** in Windows Firewall (and your VPS/router panel).
4. Run `GTAMP.Server.exe` as **Administrator** (required on Windows for `HttpListener` to accept external connections on port `4500`).
5. Set `PublicHost` and `AdminToken` in `server_config.json` and restart.

---

## 3. Playing with Friends without a VPS (Radmin VPN / ZeroTier)

To play with friends without port forwarding:
1. Everyone joins the same virtual network in **Radmin VPN** (or **ZeroTier** / **Tailscale**).
2. The host runs `GTAMP.Server.exe` and shares their **virtual IP** (e.g., `26.x.x.x`).
3. Friends enter that IP in `GTAMP.Launcher.exe` (port `4499`) and click **Connect & Launch GTA V**.

---

## 4. Verifying Everything is Online

1. **Web Admin / HTTP Modpack**: open `http://SERVER_IP:4500/admin` in your browser and enter your `AdminToken`.
2. **Game Server**: add the server in `GTAMP.Launcher.exe` (or `servers.json`) and click **Refresh** — once it shows `Online` with a ping, players can join!
