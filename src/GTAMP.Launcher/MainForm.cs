using System.Diagnostics;
using System.Text.Json;
using GTAMP.Shared;

namespace GTAMP.Launcher;

public sealed class MainForm : Form
{
    private readonly LauncherSettings _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly TextBox _txtNick = new();
    private readonly TextBox _txtGtaDir = new();
    private readonly Label _lblGtaVersion = new();
    private readonly TextBox _txtListUrl = new();
    private readonly ListView _lstServers = new();
    private readonly Button _btnRefresh = new();

    private readonly TextBox _txtIp = new();
    private readonly NumericUpDown _numPort = new();
    private readonly TextBox _txtPassword = new();
    private readonly TextBox _txtModpackUrl = new();
    private readonly CheckBox _chkVad = new();
    private readonly CheckBox _chkAutoSync = new();
    private readonly ProgressBar _prgModpack = new();
    private readonly Button _btnSyncModpack = new();
    private readonly Button _btnSaveOnly = new();
    private readonly Button _btnPlay = new();
    private readonly Label _lblStatus = new();

    public MainForm()
    {
        _settings = LauncherSettings.Load();
        BuildUi();
        LoadSettingsIntoUi();
        UpdateGtaVersionLabel();

        Shown += async (_, _) => await RefreshServersAsync();
        FormClosing += (_, _) => SaveSettingsFromUi();
    }

    private void BuildUi()
    {
        Text = "GTAMP Launcher — GTA V Build 1.0.463.1 / 1.0.505.2";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        Size = new Size(760, 560);
        MinimumSize = new Size(720, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(26, 28, 34);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9.5f);

        int pad = 14;
        int y = pad;

        // ---- Jogador e pasta do GTA ----
        AddLabel("Apelido:", pad, y + 3, 90);
        _txtNick.SetBounds(110, y, 210, 26);
        StyleInput(_txtNick);
        Controls.Add(_txtNick);

        _lblGtaVersion.SetBounds(340, y + 3, 390, 22);
        _lblGtaVersion.ForeColor = Color.LightSkyBlue;
        Controls.Add(_lblGtaVersion);
        y += 34;

        AddLabel("Pasta GTA V:", pad, y + 3, 90);
        _txtGtaDir.SetBounds(110, y, 520, 26);
        _txtGtaDir.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        StyleInput(_txtGtaDir);
        _txtGtaDir.TextChanged += (_, _) => UpdateGtaVersionLabel();
        Controls.Add(_txtGtaDir);

        var btnBrowse = new Button { Text = "Procurar..." };
        btnBrowse.SetBounds(640, y - 1, 90, 28);
        btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        StyleButton(btnBrowse, Color.FromArgb(55, 60, 72));
        btnBrowse.Click += (_, _) => BrowseGtaFolder();
        Controls.Add(btnBrowse);
        y += 36;

        // ---- URL JSON da lista de servidores ----
        AddLabel("Lista JSON:", pad, y + 3, 90);
        _txtListUrl.SetBounds(110, y, 520, 26);
        _txtListUrl.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _txtListUrl.PlaceholderText = "(opcional) URL de um JSON de servidores ou vazio para usar servers.json local";
        StyleInput(_txtListUrl);
        Controls.Add(_txtListUrl);

        _btnRefresh.Text = "Atualizar";
        _btnRefresh.SetBounds(640, y - 1, 90, 28);
        _btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        StyleButton(_btnRefresh, Color.FromArgb(42, 110, 185));
        _btnRefresh.Click += async (_, _) => await RefreshServersAsync();
        Controls.Add(_btnRefresh);
        y += 36;

        // ---- Lista de servidores ----
        _lstServers.SetBounds(pad, y, 716, 185);
        _lstServers.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _lstServers.View = View.Details;
        _lstServers.FullRowSelect = true;
        _lstServers.GridLines = false;
        _lstServers.BackColor = Color.FromArgb(34, 37, 46);
        _lstServers.ForeColor = Color.WhiteSmoke;
        _lstServers.BorderStyle = BorderStyle.FixedSingle;
        _lstServers.Columns.Add("Servidor", 210);
        _lstServers.Columns.Add("Endereco", 140);
        _lstServers.Columns.Add("Modo", 80);
        _lstServers.Columns.Add("Jogadores", 80);
        _lstServers.Columns.Add("Ping", 65);
        _lstServers.Columns.Add("Modpack", 125);
        _lstServers.SelectedIndexChanged += (_, _) => OnServerSelected();
        _lstServers.DoubleClick += async (_, _) => await LaunchGameAsync(true);
        Controls.Add(_lstServers);

        // ---- Painel inferior: conexao direta + modpack + botoes ----
        int bottomY = ClientSize.Height - 194;

        var grpDirect = new GroupBox
        {
            Text = "Conexao Direta, Voz e Modpack do Servidor",
            ForeColor = Color.LightGray,
            BackColor = Color.FromArgb(26, 28, 34),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        grpDirect.SetBounds(pad, bottomY, 716, 126);
        Controls.Add(grpDirect);

        AddLabel("IP:", 12, 27, 25, grpDirect);
        _txtIp.SetBounds(40, 24, 210, 26);
        StyleInput(_txtIp);
        grpDirect.Controls.Add(_txtIp);

        AddLabel("Porta:", 266, 27, 45, grpDirect);
        _numPort.SetBounds(316, 24, 80, 26);
        _numPort.Minimum = 1;
        _numPort.Maximum = 65535;
        _numPort.Value = Protocol.DefaultPort;
        _numPort.BackColor = Color.FromArgb(40, 44, 54);
        _numPort.ForeColor = Color.White;
        grpDirect.Controls.Add(_numPort);

        AddLabel("Senha:", 416, 27, 50, grpDirect);
        _txtPassword.SetBounds(470, 24, 230, 26);
        _txtPassword.UseSystemPasswordChar = true;
        StyleInput(_txtPassword);
        grpDirect.Controls.Add(_txtPassword);

        AddLabel("Modpack:", 12, 62, 65, grpDirect);
        _txtModpackUrl.SetBounds(80, 59, 490, 26);
        _txtModpackUrl.PlaceholderText = "(opcional) URL do manifesto JSON do modpack RP";
        StyleInput(_txtModpackUrl);
        grpDirect.Controls.Add(_txtModpackUrl);

        _btnSyncModpack.Text = "Sincronizar";
        _btnSyncModpack.SetBounds(580, 58, 120, 28);
        StyleButton(_btnSyncModpack, Color.FromArgb(120, 82, 180));
        _btnSyncModpack.Click += async (_, _) => await SyncModpackOnlyAsync();
        grpDirect.Controls.Add(_btnSyncModpack);

        _chkAutoSync.Text = "Sincronizar modpack automaticamente ao jogar";
        _chkAutoSync.ForeColor = Color.LightGray;
        _chkAutoSync.AutoSize = true;
        _chkAutoSync.Location = new Point(14, 95);
        grpDirect.Controls.Add(_chkAutoSync);

        _chkVad.Text = "Voz por Ativacao (VAD) em vez de Push-to-Talk (N)";
        _chkVad.ForeColor = Color.LightGray;
        _chkVad.AutoSize = true;
        _chkVad.Location = new Point(345, 95);
        grpDirect.Controls.Add(_chkVad);

        _prgModpack.SetBounds(pad, ClientSize.Height - 62, 716, 8);
        _prgModpack.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _prgModpack.Visible = false;
        Controls.Add(_prgModpack);

        int btnY = ClientSize.Height - 48;

        _lblStatus.SetBounds(pad, btnY + 8, 330, 24);
        _lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _lblStatus.ForeColor = Color.LightGray;
        _lblStatus.Text = "Pronto.";
        Controls.Add(_lblStatus);

        _btnSaveOnly.Text = "Salvar Config";
        _btnSaveOnly.SetBounds(380, btnY, 135, 36);
        _btnSaveOnly.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        StyleButton(_btnSaveOnly, Color.FromArgb(58, 64, 78));
        _btnSaveOnly.Click += async (_, _) => await LaunchGameAsync(false);
        Controls.Add(_btnSaveOnly);

        _btnPlay.Text = "Conectar e Iniciar GTA V";
        _btnPlay.SetBounds(525, btnY, 205, 36);
        _btnPlay.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        StyleButton(_btnPlay, Color.FromArgb(46, 155, 86));
        _btnPlay.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _btnPlay.Click += async (_, _) => await LaunchGameAsync(true);
        Controls.Add(_btnPlay);
    }

    private void AddLabel(string text, int x, int y, int w, Control parent = null)
    {
        var lbl = new Label { Text = text, ForeColor = Color.LightGray, AutoSize = false };
        lbl.SetBounds(x, y, w, 22);
        (parent ?? this).Controls.Add(lbl);
    }

    private static void StyleInput(TextBox tb)
    {
        tb.BackColor = Color.FromArgb(40, 44, 54);
        tb.ForeColor = Color.White;
        tb.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void StyleButton(Button b, Color bg)
    {
        b.BackColor = bg;
        b.ForeColor = Color.White;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.Cursor = Cursors.Hand;
    }

    private void LoadSettingsIntoUi()
    {
        _txtNick.Text = _settings.Nickname;
        _txtGtaDir.Text = _settings.GtaFolder;
        _txtListUrl.Text = _settings.ServerListUrl;
        _txtIp.Text = _settings.LastIp;
        _numPort.Value = Math.Clamp(_settings.LastPort, 1, 65535);
        _txtPassword.Text = _settings.LastPassword;
        _chkVad.Checked = _settings.VoiceActivation;
        _chkAutoSync.Checked = _settings.AutoSyncModpack;
    }

    private void SaveSettingsFromUi()
    {
        _settings.Nickname = _txtNick.Text.Trim();
        _settings.GtaFolder = _txtGtaDir.Text.Trim();
        _settings.ServerListUrl = _txtListUrl.Text.Trim();
        _settings.LastIp = _txtIp.Text.Trim();
        _settings.LastPort = (int)_numPort.Value;
        _settings.LastPassword = _txtPassword.Text;
        _settings.VoiceActivation = _chkVad.Checked;
        _settings.AutoSyncModpack = _chkAutoSync.Checked;
        _settings.Save();
    }

    private void BrowseGtaFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Selecione a pasta que contem GTA5.exe (ex.: gta_data)",
            SelectedPath = Directory.Exists(_txtGtaDir.Text) ? _txtGtaDir.Text : ""
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            _txtGtaDir.Text = dlg.SelectedPath;
    }

    private void UpdateGtaVersionLabel()
    {
        string dir = _txtGtaDir.Text.Trim();
        string exe = Path.Combine(dir, "GTA5.exe");
        if (!File.Exists(exe))
        {
            _lblGtaVersion.ForeColor = Color.Orange;
            _lblGtaVersion.Text = "GTA5.exe nao encontrado nessa pasta.";
            return;
        }

        string ver = FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "desconhecida";
        bool hasMod = File.Exists(Path.Combine(dir, "scripts", "GTAMP.Client.dll"));
        _lblGtaVersion.ForeColor = hasMod ? Color.LightGreen : Color.Gold;
        _lblGtaVersion.Text = $"GTA5.exe v{ver} | Mod: {(hasMod ? "OK" : "GTAMP.Client.dll ausente em scripts\\")}";
    }

    private async Task RefreshServersAsync()
    {
        _btnRefresh.Enabled = false;
        _lblStatus.Text = "Consultando servidores...";
        _lstServers.Items.Clear();

        List<ServerListEntry> entries = await LoadServerEntriesAsync();
        var tasks = entries.Select(async entry =>
        {
            var info = await ServerQuerier.QueryAsync(entry.Ip, entry.Port);
            return (entry, info);
        });

        var results = await Task.WhenAll(tasks);

        foreach (var (entry, info) in results)
        {
            if (info.Online && !string.IsNullOrWhiteSpace(info.ModpackUrl) && string.IsNullOrWhiteSpace(entry.ModpackUrl))
                entry.ModpackUrl = info.ModpackUrl;

            string name = info.Online && !string.IsNullOrWhiteSpace(info.ServerName) ? info.ServerName : entry.Name;
            var item = new ListViewItem(name) { Tag = entry };
            item.SubItems.Add($"{entry.Ip}:{entry.Port}");
            item.SubItems.Add(info.Online ? (string.IsNullOrWhiteSpace(info.GameMode) ? "RP" : info.GameMode) : "-");
            item.SubItems.Add(info.Online ? $"{info.Players}/{info.MaxPlayers}" : "-");
            item.SubItems.Add(info.Online ? $"{info.PingMs} ms" : "-");
            string mpStatus = !info.Online ? "Offline" :
                              (!string.IsNullOrWhiteSpace(entry.ModpackUrl) ? "Sim (Auto)" : (info.HasPassword ? "Senha" : "Vanilla"));
            item.SubItems.Add(mpStatus);
            item.ForeColor = info.Online ? Color.LightGreen : Color.Gray;
            _lstServers.Items.Add(item);
        }

        _lblStatus.Text = $"{results.Count(r => r.info.Online)}/{results.Length} servidor(es) online.";
        _btnRefresh.Enabled = true;
    }

    private async Task<List<ServerListEntry>> LoadServerEntriesAsync()
    {
        string url = _txtListUrl.Text.Trim();
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            try
            {
                string json = await _http.GetStringAsync(uri);
                var list = JsonSerializer.Deserialize<List<ServerListEntry>>(json, opts);
                if (list != null && list.Count > 0)
                    return list;
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Falha na URL JSON: " + ex.Message;
            }
        }

        string localFile = Path.Combine(AppContext.BaseDirectory, "servers.json");
        if (File.Exists(localFile))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<ServerListEntry>>(File.ReadAllText(localFile), opts);
                if (list != null)
                    return list;
            }
            catch { }
        }

        return new List<ServerListEntry> { new() { Name = "Servidor Local", Ip = "127.0.0.1", Port = Protocol.DefaultPort } };
    }

    private void OnServerSelected()
    {
        if (_lstServers.SelectedItems.Count == 0)
            return;

        if (_lstServers.SelectedItems[0].Tag is ServerListEntry entry)
        {
            _txtIp.Text = entry.Ip;
            _numPort.Value = Math.Clamp(entry.Port, 1, 65535);
            _txtModpackUrl.Text = entry.ModpackUrl ?? "";
        }
    }

    private async Task<bool> SyncModpackInternalAsync(bool showErrorDialog)
    {
        string url = _txtModpackUrl.Text.Trim();
        string gtaDir = _txtGtaDir.Text.Trim();
        if (!Directory.Exists(gtaDir))
        {
            if (showErrorDialog)
                MessageBox.Show("Selecione a pasta correta do GTA V primeiro.", "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        string serverKey = $"{_txtIp.Text.Trim()}_{(int)_numPort.Value}";

        _btnSyncModpack.Enabled = false;
        _btnPlay.Enabled = false;
        _prgModpack.Visible = true;
        _prgModpack.Value = 0;

        var (ok, summary) = await ModpackUpdater.SyncAsync(_http, url, gtaDir, serverKey, (pct, msg) =>
        {
            BeginInvoke(() =>
            {
                _prgModpack.Value = Math.Clamp(pct, 0, 100);
                _lblStatus.Text = msg;
            });
        });

        _lblStatus.Text = summary;
        if (!ok && showErrorDialog)
            MessageBox.Show(summary, "Modpack do Servidor", MessageBoxButtons.OK, MessageBoxIcon.Error);

        _btnSyncModpack.Enabled = true;
        _btnPlay.Enabled = true;
        return ok;
    }

    private async Task SyncModpackOnlyAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtModpackUrl.Text))
        {
            MessageBox.Show("Informe a URL de um manifesto JSON de modpack (ou selecione um servidor com modpack).", "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        await SyncModpackInternalAsync(true);
    }

    private async Task LaunchGameAsync(bool startExe)
    {
        SaveSettingsFromUi();

        string nick = _txtNick.Text.Trim();
        if (nick.Length < Protocol.MinNicknameLength || nick.Length > Protocol.MaxNicknameLength)
        {
            MessageBox.Show($"O apelido deve ter entre {Protocol.MinNicknameLength} e {Protocol.MaxNicknameLength} caracteres.",
                "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string gtaDir = _txtGtaDir.Text.Trim();
        if (!Directory.Exists(gtaDir) || !File.Exists(Path.Combine(gtaDir, "GTA5.exe")))
        {
            MessageBox.Show("Selecione a pasta correta do GTA V (onde esta o GTA5.exe).",
                "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (startExe && _chkAutoSync.Checked)
        {
            bool syncOk = await SyncModpackInternalAsync(true);
            if (!syncOk)
                return;
        }

        string scriptsDir = Path.Combine(gtaDir, "scripts");
        Directory.CreateDirectory(scriptsDir);

        string iniPath = Path.Combine(scriptsDir, "mp_config.ini");
        string iniContent =
            "[Server]\r\n" +
            $"IP={_txtIp.Text.Trim()}\r\n" +
            $"Port={(int)_numPort.Value}\r\n" +
            $"Password={_txtPassword.Text}\r\n" +
            "AutoConnect=true\r\n" +
            "\r\n" +
            "[Player]\r\n" +
            $"Nickname={nick}\r\n" +
            "\r\n" +
            "[Keys]\r\n" +
            "ConnectKey=F9\r\n" +
            "ChatKey=T\r\n" +
            "VoiceKey=N\r\n" +
            "VoiceModeKey=Z\r\n" +
            "InteractKey=E\r\n" +
            "MenuKey=M\r\n" +
            "\r\n" +
            "[Voice]\r\n" +
            $"VoiceActivation={(_chkVad.Checked ? "true" : "false")}\r\n" +
            "VadThreshold=0.03\r\n" +
            "MicDeviceIndex=0\r\n" +
            "OutputDeviceIndex=-1\r\n";

        File.WriteAllText(iniPath, iniContent);
        _lblStatus.Text = "mp_config.ini salvo em scripts\\.";

        if (!startExe)
            return;

        string playExe = Path.Combine(gtaDir, "PlayGTAV.exe");
        string targetExe = File.Exists(playExe) ? playExe : Path.Combine(gtaDir, "GTA5.exe");

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = targetExe,
                WorkingDirectory = gtaDir,
                UseShellExecute = true
            });
            _lblStatus.Text = "Iniciando GTA V...";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erro ao iniciar o GTA V: " + ex.Message, "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
