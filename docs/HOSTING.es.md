# Guía de Hosting — GTAMP Server (VPS Linux, Windows y VPN)

🌐 **Idiomas:** [🇧🇷 Português](HOSTING.md) | **🇪🇸 Español** | [🇺🇸 English](HOSTING.en.md)

Esta guía muestra cómo alojar un servidor dedicado **GTAMP** 24/7 en una VPS Linux, VPS Windows o en tu propia PC.

---

## Requisitos Mínimos del Servidor

`GTAMP.Server` está desarrollado en **.NET 8** y es extremadamente ligero:
- **CPU**: 1 vCPU
- **RAM**: 512 MB a 1 GB (suficiente para decenas de jugadores)
- **Puertos**:
  - **UDP `4499`** *(obligatorio)* — juego, chat y voz por proximidad/radio
  - **TCP `4500`** *(opcional)* — servidor HTTP integrado de modpack (`/modpack.json`) y Panel Web Admin (`/admin`)

---

## 1. Alojando en VPS Linux (Ubuntu / Debian) — Recomendado

### Paso 1: Instalar dependencias
```bash
sudo apt update && sudo apt install -y git screen dotnet-sdk-8.0
```
> Si el repositorio de tu Ubuntu/Debian no incluye `dotnet-sdk-8.0`, instálalo con el script oficial de Microsoft:
> ```bash
> curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
> export PATH="$HOME/.dotnet:$PATH"
> ```

### Paso 2: Clonar y compilar el servidor
```bash
git clone https://github.com/andr3sch/GTAMP.git
cd GTAMP
dotnet publish src/GTAMP.Server/GTAMP.Server.csproj -c Release -o ~/gtamp-server
```

### Paso 3: Abrir puertos en el Firewall
Abre los puertos tanto en el firewall del sistema como en el **panel / Security Group de tu proveedor de VPS** (Oracle Cloud, AWS, Contabo, DigitalOcean, etc.):
```bash
sudo ufw allow 4499/udp
sudo ufw allow 4500/tcp
```

### Paso 4: Configurar `server_config.json`
Ejecuta una vez para generar los archivos por defecto y escribe `stop`:
```bash
cd ~/gtamp-server
./GTAMP.Server
```
Edita el archivo con `nano ~/gtamp-server/server_config.json`:
- **`ServerName`** / **`Motd`**: nombre y mensaje de bienvenida del servidor.
- **`PublicHost`**: coloca la **IP pública o dominio de tu VPS** (usado en las URLs automáticas de `/modpack.json`).
- **`AdminToken`**: **cambia `"gtamp-admin"` por una contraseña segura.**
- **`HttpEnabled`**: `true` para servir mods en `resources/modpack/` y el panel `/admin`.

### Paso 5: Ejecutar 24/7 con `systemd` (reinicio automático)
Crea el servicio:
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

Comandos útiles:
```bash
sudo systemctl status gtamp      # ver estado
journalctl -u gtamp -f           # ver logs en tiempo real
sudo systemctl restart gtamp     # reiniciar servidor
```

*(Alternativa sencilla sin systemd: usa `screen -S gtamp`, ejecuta `./GTAMP.Server` y pulsa `Ctrl+A` y luego `D` para dejarlo en segundo plano.)*

---

## 2. Alojando en VPS Windows / PC Windows

1. Instala [**Microsoft .NET 8 Runtime (x64)**](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).
2. Descarga `GTAMP-Portable.zip` en la [página de Releases](https://github.com/andr3sch/GTAMP/releases/latest) y extrae la carpeta **`3_Servidor`**.
3. Abre **UDP `4499`** y **TCP `4500`** en el Firewall de Windows (y en el panel de tu VPS/router).
4. Ejecuta `GTAMP.Server.exe` como **Administrador** (necesario en Windows para que `HttpListener` acepte conexiones externas en el puerto `4500`).
5. Edita `PublicHost` y `AdminToken` en `server_config.json` y reinicia el servidor.

---

## 3. Jugando con Amigos sin VPS (Radmin VPN / ZeroTier)

Si quieres jugar solo con amigos sin abrir puertos en el router:
1. Todos entran a la misma red en **Radmin VPN** (o **ZeroTier** / **Tailscale**).
2. El host abre `GTAMP.Server.exe` en su PC y pasa su **IP virtual** (ej.: `26.x.x.x`) a sus amigos.
3. Los amigos ponen esa IP en `GTAMP.Launcher.exe` (puerto `4499`) y hacen clic en **Conectar e Iniciar GTA V**.

---

## 4. Verificando que está Online

1. **Panel Admin / Modpack HTTP**: abre `http://IP_DEL_SERVIDOR:4500/admin` en tu navegador e ingresa el `AdminToken`.
2. **Conexión del Juego**: agrega el servidor en `GTAMP.Launcher.exe` (o en `servers.json`) y haz clic en **Atualizar** — si aparece `Online` y el `Ping`, ¡los jugadores ya pueden entrar!
