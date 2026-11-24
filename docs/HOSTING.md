# Guia de Hospedagem — GTAMP Server (VPS Linux, Windows e VPN)

Este guia mostra como hospedar um servidor dedicado **GTAMP** 24/7 em uma VPS Linux, VPS Windows ou no seu próprio PC.

---

## Requisitos Mínimos do Servidor

O `GTAMP.Server` é feito em **.NET 8** e é extremamente leve:
- **CPU**: 1 vCPU
- **RAM**: 512 MB a 1 GB (suficiente para dezenas de jogadores)
- **Portas**:
  - **UDP `4499`** *(obrigatória)* — jogo, chat e voz por proximidade/rádio
  - **TCP `4500`** *(opcional)* — servidor HTTP embutido de modpack (`/modpack.json`) e Painel Web Admin (`/admin`)

---

## 1. Hospedando em VPS Linux (Ubuntu / Debian) — Recomendado

### Passo 1: Instalar dependências
```bash
sudo apt update && sudo apt install -y git screen dotnet-sdk-8.0
```
> Se o repositório do seu Ubuntu/Debian não tiver `dotnet-sdk-8.0`, instale pelo script oficial da Microsoft:
> ```bash
> curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0
> export PATH="$HOME/.dotnet:$PATH"
> ```

### Passo 2: Clonar e compilar o servidor
```bash
git clone https://github.com/andr3sch/GTAMP.git
cd GTAMP
dotnet publish src/GTAMP.Server/GTAMP.Server.csproj -c Release -o ~/gtamp-server
```

### Passo 3: Liberar portas no Firewall
Libere as portas tanto no firewall da máquina quanto no **painel/Security Group da provedora da VPS** (Oracle Cloud, AWS, Contabo, DigitalOcean, etc.):
```bash
sudo ufw allow 4499/udp
sudo ufw allow 4500/tcp
```

### Passo 4: Configurar `server_config.json`
Execute uma vez para gerar os arquivos padrão e digite `stop`:
```bash
cd ~/gtamp-server
./GTAMP.Server
```
Edite o arquivo com `nano ~/gtamp-server/server_config.json`:
- **`ServerName`** / **`Motd`**: nome e mensagem do servidor.
- **`PublicHost`**: coloque o **IP público ou domínio da VPS** (usado nas URLs automáticas do `/modpack.json`).
- **`AdminToken`**: **troque `"gtamp-admin"` por uma senha forte!**
- **`HttpEnabled`**: `true` para servir mods em `resources/modpack/` e o painel `/admin`.

### Passo 5: Rodar 24/7 com `systemd` (reinício automático)
Crie o serviço:
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

Comandos úteis:
```bash
sudo systemctl status gtamp      # ver status
journalctl -u gtamp -f           # ver logs em tempo real
sudo systemctl restart gtamp     # reiniciar servidor
```

*(Alternativa simples sem systemd: use `screen -S gtamp`, rode `./GTAMP.Server` e pressione `Ctrl+A` depois `D` para deixar em segundo plano.)*

---

## 2. Hospedando em VPS Windows / PC Windows

1. Instale o [**Microsoft .NET 8 Runtime (x64)**](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).
2. Baixe `GTAMP-Portable.zip` na [página de Releases](https://github.com/andr3sch/GTAMP/releases/latest) e extraia a pasta **`3_Servidor`**.
3. Libere **UDP `4499`** e **TCP `4500`** no Firewall do Windows (e no painel da VPS/roteador).
4. Execute `GTAMP.Server.exe` como **Administrador** (necessário no Windows para o `HttpListener` aceitar conexões externas na porta `4500`).
5. Edite `PublicHost` e `AdminToken` em `server_config.json` e reinicie o servidor.

---

## 3. Jogando com Amigos sem VPS (Radmin VPN / ZeroTier)

Se quiser jogar só com amigos sem abrir portas no roteador:
1. Todos entram na mesma rede no **Radmin VPN** (ou **ZeroTier** / **Tailscale**).
2. O host abre `GTAMP.Server.exe` no PC e passa o **IP virtual** (ex.: `26.x.x.x`) para os amigos.
3. Os amigos colocam o IP no `GTAMP.Launcher.exe` (porta `4499`) e clicam em **Conectar e Iniciar GTA V**.

---

## 4. Verificando se está Online

1. **Painel Admin / Modpack HTTP**: abra `http://IP_DO_SERVIDOR:4500/admin` no navegador e informe o `AdminToken`.
2. **Conexão do Jogo**: adicione o servidor no `GTAMP.Launcher.exe` (ou no `servers.json`) e clique em **Atualizar** — se aparecer `Online` e o `Ping`, os jogadores já podem entrar!
