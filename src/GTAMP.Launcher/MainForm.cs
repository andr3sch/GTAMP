using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Text.RegularExpressions;
using GTAMP.Shared;

namespace GTAMP.Launcher;

/// <summary>
/// Launcher moderno estilo FiveM para GTAMP (GTA V Lighter 1.0.463.1 / 1.0.505.2).
/// Abas laterais: Servidores (Server Browser), Conexao Direta e Configuracoes.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly Regex NicknameRegex =
        new(@"^[\p{L}\p{N}_\-\.\[\] ]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Paleta FiveM Dark
    private static readonly Color BgWindow   = Color.FromArgb(13, 16, 23);
    private static readonly Color BgSidebar  = Color.FromArgb(16, 20, 29);
    private static readonly Color BgCard     = Color.FromArgb(22, 27, 38);
    private static readonly Color BgInput    = Color.FromArgb(30, 36, 50);
    private static readonly Color BgRowAlt   = Color.FromArgb(19, 24, 34);
    private static readonly Color BgRowSel   = Color.FromArgb(33, 62, 108);
    private static readonly Color AccentBlue = Color.FromArgb(43, 142, 255);
    private static readonly Color AccentGreen = Color.FromArgb(39, 174, 96);
    private static readonly Color AccentPurple = Color.FromArgb(124, 77, 255);
    private static readonly Color BorderSubtle = Color.FromArgb(42, 50, 68);
    private static readonly Color TextPrimary = Color.FromArgb(240, 244, 250);
    private static readonly Color TextMuted   = Color.FromArgb(148, 160, 182);

    private readonly LauncherSettings _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };

    private List<(ServerListEntry Entry, ServerQueryResult Info)> _allServers = new();

    // Sidebar
    private readonly Panel _sidebar = new();
    private readonly RoundedButton _navServers = new();
    private readonly RoundedButton _navDirect = new();
    private readonly RoundedButton _navSettings = new();
    private readonly Label _lblProfileNick = new();

    // Paginas
    private readonly Panel _contentHost = new();
    private readonly Panel _pageServers = new();
    private readonly Panel _pageDirect = new();
    private readonly Panel _pageSettings = new();

    // Pagina 1: Servidores
    private readonly TextBox _txtSearch = new();
    private readonly RoundedButton _btnRefresh = new();
    private readonly RoundedButton _btnAddServer = new();
    private readonly Label _lblServerCount = new();
    private readonly ListView _lstServers = new();
    private readonly Label _lblSelTitle = new();
    private readonly Label _lblSelSub = new();
    private readonly RoundedButton _btnPlaySelected = new();

    // Pagina 2: Conexao Direta
    private readonly TextBox _txtIp = new();
    private readonly NumericUpDown _numPort = new();
    private readonly TextBox _txtPassword = new();
    private readonly TextBox _txtModpackUrl = new();
    private readonly RoundedButton _btnSyncModpack = new();
    private readonly RoundedButton _btnPlayDirect = new();

    // Pagina 3: Configuracoes
    private readonly TextBox _txtNick = new();
    private readonly Label _lblNickStatus = new();
    private readonly TextBox _txtGtaDir = new();
    private readonly Label _lblGtaStatus = new();
    private readonly TextBox _txtListUrl = new();
    private readonly CheckBox _chkVad = new();
    private readonly CheckBox _chkAutoSync = new();
    private readonly RoundedButton _btnSaveSettings = new();
    private readonly RoundedButton _btnCreateShortcut = new();

    // Barra inferior global
    private readonly ProgressBar _prgModpack = new();
    private readonly Label _lblStatus = new();

    public MainForm()
    {
        _settings = LauncherSettings.Load();
        DoubleBuffered = true;

        BuildUi();
        LoadSettingsIntoUi();
        ValidateNickUi();
        UpdateGtaStatusUi();
        SwitchTab(0);

        Shown += async (_, _) => await RefreshServersAsync();
        FormClosing += (_, _) => SaveSettingsFromUi(writeIniIfValid: true);
    }

    // =====================================================================
    // Construcao da Interface (Estilo FiveM)
    // =====================================================================

    private void BuildUi()
    {
        Text = "GTAMP — GTA V Lighter Multiplayer";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        Size = new Size(1060, 640);
        MinimumSize = new Size(920, 580);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgWindow;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9.5f);

        BuildSidebar();
        BuildBottomStatusBar();

        _contentHost.Dock = DockStyle.Fill;
        _contentHost.BackColor = BgWindow;
        _contentHost.Padding = new Padding(24, 20, 24, 16);
        Controls.Add(_contentHost);
        _contentHost.BringToFront();

        BuildServersPage();
        BuildDirectPage();
        BuildSettingsPage();

        _contentHost.Controls.Add(_pageServers);
        _contentHost.Controls.Add(_pageDirect);
        _contentHost.Controls.Add(_pageSettings);
    }

    private void BuildSidebar()
    {
        _sidebar.Dock = DockStyle.Left;
        _sidebar.Width = 220;
        _sidebar.BackColor = BgSidebar;
        Controls.Add(_sidebar);

        // Linha divisoria sutil a direita da sidebar
        var divider = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = BorderSubtle };
        _sidebar.Controls.Add(divider);

        // Cabecalho / Marca GTAMP
        var logoBox = new PictureBox
        {
            Size = new Size(44, 44),
            Location = new Point(18, 20),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        try
        {
            if (Icon != null) logoBox.Image = Icon.ToBitmap();
        }
        catch { }
        _sidebar.Controls.Add(logoBox);

        var lblBrand = new Label
        {
            Text = "GTAMP",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(70, 18)
        };
        _sidebar.Controls.Add(lblBrand);

        var lblTag = new Label
        {
            Text = "GTA V Lighter MP",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(46, 204, 113),
            AutoSize = true,
            Location = new Point(72, 46)
        };
        _sidebar.Controls.Add(lblTag);

        // Botoes de navegacao arredondados estilo FiveM
        ConfigureNavButton(_navServers, "▶   Servidores", 94, () => SwitchTab(0));
        ConfigureNavButton(_navDirect, "⚡   Conexão Direta", 146, () => SwitchTab(1));
        ConfigureNavButton(_navSettings, "⚙   Configurações", 198, () => SwitchTab(2));

        // Rodape da sidebar: Perfil + Links
        var pnlFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 148,
            BackColor = BgSidebar,
            Padding = new Padding(14, 8, 14, 14)
        };
        _sidebar.Controls.Add(pnlFooter);

        var profileCard = new RoundedPanel
        {
            Location = new Point(14, 6),
            Size = new Size(190, 58),
            CornerRadius = 10,
            FillColor = BgCard,
            BorderColor = BorderSubtle,
            Cursor = Cursors.Hand
        };
        profileCard.Click += (_, _) => SwitchTab(2);
        pnlFooter.Controls.Add(profileCard);

        var lblProfTitle = new Label
        {
            Text = "SEU APELIDO",
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = TextMuted,
            BackColor = BgCard,
            Location = new Point(12, 8),
            AutoSize = true
        };
        lblProfTitle.Click += (_, _) => SwitchTab(2);
        profileCard.Controls.Add(lblProfTitle);

        _lblProfileNick.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        _lblProfileNick.ForeColor = Color.White;
        _lblProfileNick.BackColor = BgCard;
        _lblProfileNick.Location = new Point(12, 26);
        _lblProfileNick.Size = new Size(166, 22);
        _lblProfileNick.Click += (_, _) => SwitchTab(2);
        profileCard.Controls.Add(_lblProfileNick);

        var btnDiscord = new RoundedButton { Text = "Discord Oficial", CornerRadius = 9 };
        btnDiscord.SetBounds(14, 74, 190, 30);
        StyleButton(btnDiscord, Color.FromArgb(88, 101, 242));
        btnDiscord.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        btnDiscord.Click += (_, _) => OpenUrl("https://discord.gg/XEqfhjmYUm");
        pnlFooter.Controls.Add(btnDiscord);

        var btnGitHub = new RoundedButton { Text = "GitHub / Releases", CornerRadius = 9 };
        btnGitHub.SetBounds(14, 110, 190, 28);
        StyleButton(btnGitHub, Color.FromArgb(35, 42, 56));
        btnGitHub.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        btnGitHub.Click += (_, _) => OpenUrl("https://github.com/andr3sch/GTAMP");
        pnlFooter.Controls.Add(btnGitHub);
    }

    private void ConfigureNavButton(RoundedButton btn, string text, int y, Action onClick)
    {
        btn.Text = text;
        btn.TextAlign = ContentAlignment.MiddleLeft;
        btn.CornerRadius = 10;
        btn.SetBounds(12, y, 194, 42);
        btn.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        btn.Padding = new Padding(14, 0, 0, 0);
        btn.Click += (_, _) => onClick();
        _sidebar.Controls.Add(btn);
    }

    private void SwitchTab(int index)
    {
        _pageServers.Visible = index == 0;
        _pageDirect.Visible = index == 1;
        _pageSettings.Visible = index == 2;

        RoundedButton[] navs = { _navServers, _navDirect, _navSettings };
        for (int i = 0; i < navs.Length; i++)
        {
            bool active = i == index;
            navs[i].SetPalette(active ? Color.FromArgb(35, 90, 175) : BgSidebar);
            navs[i].ForeColor = active ? Color.White : TextMuted;
        }
    }

    private void BuildBottomStatusBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 32,
            BackColor = Color.FromArgb(11, 14, 20),
            Padding = new Padding(16, 0, 16, 0)
        };
        var topLine = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = BorderSubtle };
        bar.Controls.Add(topLine);

        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.ForeColor = TextMuted;
        _lblStatus.Font = new Font("Segoe UI", 8.5f);
        _lblStatus.Text = "Pronto.";
        bar.Controls.Add(_lblStatus);

        _prgModpack.Dock = DockStyle.Right;
        _prgModpack.Width = 220;
        _prgModpack.Visible = false;
        bar.Controls.Add(_prgModpack);

        Controls.Add(bar);
    }

    // ---- Pagina 1: Server Browser estilo FiveM ----
    private void BuildServersPage()
    {
        _pageServers.Dock = DockStyle.Fill;
        _pageServers.BackColor = BgWindow;

        var topBar = new Panel { Dock = DockStyle.Top, Height = 86, BackColor = BgWindow };
        _pageServers.Controls.Add(topBar);

        var lblTitle = new Label
        {
            Text = "Servidores",
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(0, 0),
            AutoSize = true
        };
        topBar.Controls.Add(lblTitle);

        _lblServerCount.Text = "Consultando...";
        _lblServerCount.Font = new Font("Segoe UI", 9f);
        _lblServerCount.ForeColor = AccentBlue;
        _lblServerCount.Location = new Point(155, 11);
        _lblServerCount.AutoSize = true;
        topBar.Controls.Add(_lblServerCount);

        _txtSearch.SetBounds(0, 44, 340, 30);
        _txtSearch.PlaceholderText = "Buscar servidor por nome ou IP...";
        StyleInput(_txtSearch);
        _txtSearch.TextChanged += (_, _) => RenderServerList();
        topBar.Controls.Add(_txtSearch);

        _btnRefresh.Text = "Atualizar Lista";
        _btnRefresh.SetBounds(352, 43, 130, 31);
        StyleButton(_btnRefresh, AccentBlue);
        _btnRefresh.Click += async (_, _) => await RefreshServersAsync();
        topBar.Controls.Add(_btnRefresh);

        _btnAddServer.Text = "+ Adicionar IP";
        _btnAddServer.SetBounds(492, 43, 125, 31);
        StyleButton(_btnAddServer, Color.FromArgb(40, 48, 66));
        _btnAddServer.Click += (_, _) => AddServerDialog();
        topBar.Controls.Add(_btnAddServer);

        // Card inferior do servidor selecionado (Dock limpo, nunca sobrepoe o botao!)
        var selectedCard = new RoundedPanel
        {
            Dock = DockStyle.Bottom,
            Height = 86,
            CornerRadius = 12,
            FillColor = BgCard,
            BorderColor = BorderSubtle,
            Padding = new Padding(18, 14, 18, 14)
        };
        _pageServers.Controls.Add(selectedCard);

        var rightActionPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 230,
            BackColor = BgCard,
            Padding = new Padding(12, 4, 0, 4)
        };
        selectedCard.Controls.Add(rightActionPanel);

        _btnPlaySelected.Text = "▶  CONECTAR E JOGAR";
        _btnPlaySelected.Dock = DockStyle.Fill;
        _btnPlaySelected.CornerRadius = 12;
        StyleButton(_btnPlaySelected, AccentGreen);
        _btnPlaySelected.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _btnPlaySelected.Click += async (_, _) => await LaunchGameAsync(true);
        rightActionPanel.Controls.Add(_btnPlaySelected);

        var leftInfoPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BgCard
        };
        selectedCard.Controls.Add(leftInfoPanel);
        leftInfoPanel.BringToFront();

        _lblSelTitle.Text = "Selecione um servidor na lista";
        _lblSelTitle.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
        _lblSelTitle.ForeColor = Color.White;
        _lblSelTitle.BackColor = BgCard;
        _lblSelTitle.Dock = DockStyle.Top;
        _lblSelTitle.Height = 28;
        _lblSelTitle.AutoEllipsis = true;
        leftInfoPanel.Controls.Add(_lblSelTitle);

        _lblSelSub.Text = "Clique duas vezes em um servidor ou use Conexão Direta para entrar.";
        _lblSelSub.Font = new Font("Segoe UI", 9f);
        _lblSelSub.ForeColor = TextMuted;
        _lblSelSub.BackColor = BgCard;
        _lblSelSub.Dock = DockStyle.Fill;
        _lblSelSub.AutoEllipsis = true;
        leftInfoPanel.Controls.Add(_lblSelSub);
        _lblSelSub.BringToFront();

        // Lista de servidores OwnerDraw moderna
        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 12), BackColor = BgWindow };
        _pageServers.Controls.Add(listHost);
        listHost.BringToFront();

        _lstServers.Dock = DockStyle.Fill;
        _lstServers.View = View.Details;
        _lstServers.FullRowSelect = true;
        _lstServers.MultiSelect = false;
        _lstServers.OwnerDraw = true;
        _lstServers.BorderStyle = BorderStyle.None;
        _lstServers.BackColor = BgCard;
        _lstServers.ForeColor = TextPrimary;
        _lstServers.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _lstServers.Columns.Add("SERVIDOR", 260);
        _lstServers.Columns.Add("ENDEREÇO", 155);
        _lstServers.Columns.Add("MODO", 95);
        _lstServers.Columns.Add("JOGADORES", 95);
        _lstServers.Columns.Add("PING", 80);
        _lstServers.Columns.Add("MODPACK", 110);
        _lstServers.DrawColumnHeader += OnDrawServerHeader;
        _lstServers.DrawItem += (_, e) => e.DrawDefault = false;
        _lstServers.DrawSubItem += OnDrawServerSubItem;
        _lstServers.SelectedIndexChanged += (_, _) => OnServerSelected();
        _lstServers.DoubleClick += async (_, _) => await LaunchGameAsync(true);
        listHost.Controls.Add(_lstServers);
    }

    private void OnDrawServerHeader(object sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var bg = new SolidBrush(Color.FromArgb(18, 22, 32));
        using var border = new Pen(BorderSubtle);
        e.Graphics.FillRectangle(bg, e.Bounds);
        e.Graphics.DrawLine(border, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        TextRenderer.DrawText(e.Graphics, e.Header.Text, new Font("Segoe UI", 8f, FontStyle.Bold),
            new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height),
            TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    private void OnDrawServerSubItem(object sender, DrawListViewSubItemEventArgs e)
    {
        bool selected = e.Item.Selected;
        Color bgColor = selected ? BgRowSel : (e.ItemIndex % 2 == 0 ? BgCard : BgRowAlt);
        using var bg = new SolidBrush(bgColor);
        e.Graphics.FillRectangle(bg, e.Bounds);

        Color textColor = TextPrimary;
        Font font = e.ColumnIndex == 0 ? new Font("Segoe UI", 9.5f, FontStyle.Bold) : Font;

        if (e.Item.Tag is ValueTuple<ServerListEntry, ServerQueryResult> data)
        {
            if (!data.Item2.Online)
            {
                textColor = Color.FromArgb(115, 125, 145);
            }
            else if (e.ColumnIndex == 4) // Ping
            {
                int ping = data.Item2.PingMs;
                textColor = ping < 80 ? Color.FromArgb(72, 219, 130) :
                            ping < 160 ? Color.FromArgb(245, 200, 75) : Color.FromArgb(245, 95, 95);
            }
            else if (e.ColumnIndex == 5) // Modpack
            {
                textColor = e.SubItem.Text.StartsWith("Sim") ? Color.FromArgb(165, 130, 255) : TextMuted;
            }
            else if (e.ColumnIndex == 2) // Modo
            {
                textColor = AccentBlue;
            }
        }

        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, font,
            new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
            textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    // ---- Pagina 2: Conexao Direta ----
    private void BuildDirectPage()
    {
        _pageDirect.Dock = DockStyle.Fill;
        _pageDirect.BackColor = BgWindow;

        var lblTitle = new Label
        {
            Text = "Conexão Direta",
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(0, 0),
            AutoSize = true
        };
        _pageDirect.Controls.Add(lblTitle);

        var lblDesc = new Label
        {
            Text = "Conecte diretamente a um IP de VPS, Radmin VPN, ZeroTier ou servidor local.",
            ForeColor = TextMuted,
            Location = new Point(2, 36),
            AutoSize = true
        };
        _pageDirect.Controls.Add(lblDesc);

        var card = new RoundedPanel
        {
            Location = new Point(0, 74),
            Size = new Size(720, 290),
            CornerRadius = 14,
            FillColor = BgCard,
            BorderColor = BorderSubtle,
            Padding = new Padding(22)
        };
        _pageDirect.Controls.Add(card);

        AddFieldLabel(card, "ENDEREÇO IP OU DOMÍNIO", 24, 22);
        _txtIp.SetBounds(24, 44, 340, 30);
        StyleInput(_txtIp);
        card.Controls.Add(_txtIp);

        AddFieldLabel(card, "PORTA UDP", 384, 22);
        _numPort.SetBounds(384, 44, 110, 30);
        _numPort.Minimum = 1;
        _numPort.Maximum = 65535;
        _numPort.Value = Protocol.DefaultPort;
        _numPort.BackColor = BgInput;
        _numPort.ForeColor = Color.White;
        card.Controls.Add(_numPort);

        AddFieldLabel(card, "SENHA (OPCIONAL)", 514, 22);
        _txtPassword.SetBounds(514, 44, 180, 30);
        _txtPassword.UseSystemPasswordChar = true;
        StyleInput(_txtPassword);
        card.Controls.Add(_txtPassword);

        AddFieldLabel(card, "URL DO MODPACK DO SERVIDOR (OPCIONAL)", 24, 98);
        _txtModpackUrl.SetBounds(24, 120, 515, 30);
        _txtModpackUrl.PlaceholderText = "Ex.: http://ip-do-servidor:4500/modpack.json";
        StyleInput(_txtModpackUrl);
        card.Controls.Add(_txtModpackUrl);

        _btnSyncModpack.Text = "Sincronizar";
        _btnSyncModpack.SetBounds(552, 119, 142, 31);
        StyleButton(_btnSyncModpack, AccentPurple);
        _btnSyncModpack.Click += async (_, _) => await SyncModpackOnlyAsync();
        card.Controls.Add(_btnSyncModpack);

        _btnPlayDirect.Text = "▶  CONECTAR E INICIAR GTA V";
        _btnPlayDirect.SetBounds(24, 194, 300, 48);
        _btnPlayDirect.CornerRadius = 12;
        StyleButton(_btnPlayDirect, AccentGreen);
        _btnPlayDirect.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        _btnPlayDirect.Click += async (_, _) => await LaunchGameAsync(true);
        card.Controls.Add(_btnPlayDirect);

        var btnSaveOnly = new RoundedButton { Text = "Apenas Salvar mp_config.ini", CornerRadius = 12 };
        btnSaveOnly.SetBounds(340, 194, 230, 48);
        StyleButton(btnSaveOnly, Color.FromArgb(42, 50, 68));
        btnSaveOnly.Click += async (_, _) => await LaunchGameAsync(false);
        card.Controls.Add(btnSaveOnly);
    }

    // ---- Pagina 3: Configuracoes ----
    private void BuildSettingsPage()
    {
        _pageSettings.Dock = DockStyle.Fill;
        _pageSettings.BackColor = BgWindow;
        _pageSettings.AutoScroll = true;

        var lblTitle = new Label
        {
            Text = "Configurações",
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(0, 0),
            AutoSize = true
        };
        _pageSettings.Controls.Add(lblTitle);

        // Card 1: Perfil e Apelido
        var cardProfile = new RoundedPanel { Location = new Point(0, 48), Size = new Size(740, 118), CornerRadius = 14, FillColor = BgCard, BorderColor = BorderSubtle };
        _pageSettings.Controls.Add(cardProfile);

        AddFieldLabel(cardProfile, "SEU APELIDO NO JOGO (3 A 24 CARACTERES)", 20, 16);
        _txtNick.SetBounds(20, 38, 320, 30);
        _txtNick.MaxLength = Protocol.MaxNicknameLength;
        StyleInput(_txtNick);
        _txtNick.TextChanged += (_, _) => ValidateNickUi();
        cardProfile.Controls.Add(_txtNick);

        _lblNickStatus.Location = new Point(356, 44);
        _lblNickStatus.Size = new Size(360, 24);
        _lblNickStatus.BackColor = BgCard;
        _lblNickStatus.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        cardProfile.Controls.Add(_lblNickStatus);

        var lblNickTip = new Label
        {
            Text = "Seu apelido aparece acima do personagem e no chat. Você também pode trocar no jogo com /nick <novo>.",
            ForeColor = TextMuted,
            BackColor = BgCard,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(20, 78),
            AutoSize = true
        };
        cardProfile.Controls.Add(lblNickTip);

        // Card 2: Pasta do GTA V Lighter
        var cardGta = new RoundedPanel { Location = new Point(0, 180), Size = new Size(740, 118), CornerRadius = 14, FillColor = BgCard, BorderColor = BorderSubtle };
        _pageSettings.Controls.Add(cardGta);

        AddFieldLabel(cardGta, "PASTA DO GTA V LIGHTER (ONDE FICA O GTA5.EXE)", 20, 16);
        _txtGtaDir.SetBounds(20, 38, 575, 30);
        StyleInput(_txtGtaDir);
        _txtGtaDir.TextChanged += (_, _) => UpdateGtaStatusUi();
        cardGta.Controls.Add(_txtGtaDir);

        var btnBrowse = new RoundedButton { Text = "Procurar...", CornerRadius = 9 };
        btnBrowse.SetBounds(606, 37, 112, 32);
        StyleButton(btnBrowse, Color.FromArgb(44, 52, 70));
        btnBrowse.Click += (_, _) => BrowseGtaFolder();
        cardGta.Controls.Add(btnBrowse);

        _lblGtaStatus.Location = new Point(20, 78);
        _lblGtaStatus.Size = new Size(700, 24);
        _lblGtaStatus.BackColor = BgCard;
        _lblGtaStatus.Font = new Font("Segoe UI", 9f);
        cardGta.Controls.Add(_lblGtaStatus);

        // Card 3: Voz, Modpacks e Lista
        var cardExtra = new RoundedPanel { Location = new Point(0, 312), Size = new Size(740, 146), CornerRadius = 14, FillColor = BgCard, BorderColor = BorderSubtle };
        _pageSettings.Controls.Add(cardExtra);

        AddFieldLabel(cardExtra, "URL DE LISTA DE SERVIDORES JSON (OPCIONAL)", 20, 14);
        _txtListUrl.SetBounds(20, 36, 700, 30);
        _txtListUrl.PlaceholderText = "Vazio = usa o arquivo servers.json local";
        StyleInput(_txtListUrl);
        cardExtra.Controls.Add(_txtListUrl);

        _chkAutoSync.Text = "Sincronizar modpack do servidor automaticamente antes de abrir o GTA V";
        _chkAutoSync.ForeColor = TextPrimary;
        _chkAutoSync.BackColor = BgCard;
        _chkAutoSync.AutoSize = true;
        _chkAutoSync.Location = new Point(20, 80);
        cardExtra.Controls.Add(_chkAutoSync);

        _chkVad.Text = "Usar Ativação por Voz (VAD) em vez de segurar N (Push-to-Talk)";
        _chkVad.ForeColor = TextPrimary;
        _chkVad.BackColor = BgCard;
        _chkVad.AutoSize = true;
        _chkVad.Location = new Point(20, 108);
        cardExtra.Controls.Add(_chkVad);

        // Botoes de acao
        _btnSaveSettings.Text = "Salvar Configurações";
        _btnSaveSettings.SetBounds(0, 474, 210, 40);
        StyleButton(_btnSaveSettings, AccentBlue);
        _btnSaveSettings.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _btnSaveSettings.Click += (_, _) =>
        {
            if (!ValidateNickUi())
            {
                MessageBox.Show("Corrija o apelido antes de salvar.", "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SaveSettingsFromUi(writeIniIfValid: true);
            _lblStatus.Text = "Configurações e apelido salvos em launcher_settings.json e scripts\\mp_config.ini!";
        };
        _pageSettings.Controls.Add(_btnSaveSettings);

        _btnCreateShortcut.Text = "Criar Atalho na Área de Trabalho";
        _btnCreateShortcut.SetBounds(224, 474, 240, 40);
        StyleButton(_btnCreateShortcut, Color.FromArgb(44, 52, 70));
        _btnCreateShortcut.Click += (_, _) => CreateDesktopShortcut();
        _pageSettings.Controls.Add(_btnCreateShortcut);
    }

    private static void AddFieldLabel(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = TextMuted,
            BackColor = BgCard,
            Location = new Point(x, y),
            AutoSize = true
        });
    }

    private static void StyleInput(TextBox tb)
    {
        tb.BackColor = BgInput;
        tb.ForeColor = Color.White;
        tb.BorderStyle = BorderStyle.FixedSingle;
        tb.Font = new Font("Segoe UI", 10f);
    }

    private static void StyleButton(RoundedButton b, Color bg)
    {
        b.SetPalette(bg);
        b.ForeColor = Color.White;
    }

    private static void StyleButton(Button b, Color bg)
    {
        b.BackColor = bg;
        b.ForeColor = Color.White;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.Cursor = Cursors.Hand;
    }

    // =====================================================================
    // Logica de Configuracoes, Validacao de Nick e Atalho
    // =====================================================================

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
        _lblProfileNick.Text = _settings.Nickname;
    }

    private bool ValidateNickUi()
    {
        string nick = Regex.Replace((_txtNick.Text ?? "").Trim(), @"\s+", " ");
        _lblProfileNick.Text = string.IsNullOrEmpty(nick) ? "(sem apelido)" : nick;

        if (nick.Length < Protocol.MinNicknameLength || nick.Length > Protocol.MaxNicknameLength)
        {
            _lblNickStatus.ForeColor = Color.FromArgb(245, 110, 110);
            _lblNickStatus.Text = $"✖ Use entre {Protocol.MinNicknameLength} e {Protocol.MaxNicknameLength} caracteres.";
            return false;
        }
        if (!NicknameRegex.IsMatch(nick))
        {
            _lblNickStatus.ForeColor = Color.FromArgb(245, 110, 110);
            _lblNickStatus.Text = "✖ Use apenas letras, números, espaços ou _ - . [ ]";
            return false;
        }

        _lblNickStatus.ForeColor = Color.FromArgb(72, 219, 130);
        _lblNickStatus.Text = "✓ Apelido válido";
        return true;
    }

    private void SaveSettingsFromUi(bool writeIniIfValid)
    {
        string cleanNick = Regex.Replace((_txtNick.Text ?? "").Trim(), @"\s+", " ");
        if (cleanNick.Length >= Protocol.MinNicknameLength)
            _settings.Nickname = cleanNick;

        _settings.GtaFolder = _txtGtaDir.Text.Trim();
        _settings.ServerListUrl = _txtListUrl.Text.Trim();
        _settings.LastIp = _txtIp.Text.Trim();
        _settings.LastPort = (int)_numPort.Value;
        _settings.LastPassword = _txtPassword.Text;
        _settings.VoiceActivation = _chkVad.Checked;
        _settings.AutoSyncModpack = _chkAutoSync.Checked;
        _settings.Save();

        if (writeIniIfValid && Directory.Exists(_settings.GtaFolder) && File.Exists(Path.Combine(_settings.GtaFolder, "GTA5.exe")))
        {
            WriteClientIni(_settings.GtaFolder, _settings.Nickname);
        }
    }

    private void WriteClientIni(string gtaDir, string nick)
    {
        try
        {
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
        }
        catch { }
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

    private void UpdateGtaStatusUi()
    {
        string dir = _txtGtaDir.Text.Trim();
        string exe = Path.Combine(dir, "GTA5.exe");
        if (!File.Exists(exe))
        {
            _lblGtaStatus.ForeColor = Color.Orange;
            _lblGtaStatus.Text = "⚠ GTA5.exe não encontrado nesta pasta.";
            return;
        }

        string ver = FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "desconhecida";
        bool hasMod = File.Exists(Path.Combine(dir, "scripts", "GTAMP.Client.dll"));
        bool hasShv = File.Exists(Path.Combine(dir, "ScriptHookV.dll")) && File.Exists(Path.Combine(dir, "ScriptHookVDotNet.asi"));
        _lblGtaStatus.ForeColor = (hasMod && hasShv) ? Color.FromArgb(72, 219, 130) : Color.Gold;
        _lblGtaStatus.Text = $"✓ GTA5.exe v{ver}  |  GTAMP Client: {(hasMod ? "OK" : "Ausente")}  |  SHV/SHVDN: {(hasShv ? "OK" : "Ausente")}";
    }

    private void CreateDesktopShortcut()
    {
        try
        {
            string exePath = Application.ExecutablePath;
            string workDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string lnkPath = Path.Combine(desktop, "GTAMP Launcher.lnk");

            string ps = $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{lnkPath.Replace("'", "''")}');" +
                        $"$s.TargetPath='{exePath.Replace("'", "''")}';" +
                        $"$s.WorkingDirectory='{workDir.Replace("'", "''")}';" +
                        $"$s.IconLocation='{exePath.Replace("'", "''")},0';" +
                        $"$s.Description='GTAMP Launcher (GTA V Lighter Multiplayer)';$s.Save();";

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit(3000);

            _lblStatus.Text = "Atalho 'GTAMP Launcher' criado na Área de Trabalho!";
            MessageBox.Show("Atalho criado na Área de Trabalho com sucesso!", "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erro ao criar atalho: " + ex.Message, "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // =====================================================================
    // Lista de Servidores & Adicionar Servidor
    // =====================================================================

    private async Task RefreshServersAsync()
    {
        _btnRefresh.Enabled = false;
        _lblStatus.Text = "Consultando servidores...";
        _lblServerCount.Text = "Atualizando...";

        List<ServerListEntry> entries = await LoadServerEntriesAsync();
        var tasks = entries.Select(async entry =>
        {
            var info = await ServerQuerier.QueryAsync(entry.Ip, entry.Port);
            if (info.Online && !string.IsNullOrWhiteSpace(info.ModpackUrl) && string.IsNullOrWhiteSpace(entry.ModpackUrl))
                entry.ModpackUrl = info.ModpackUrl;
            return (Entry: entry, Info: info);
        });

        _allServers = (await Task.WhenAll(tasks)).ToList();
        RenderServerList();

        int online = _allServers.Count(r => r.Info.Online);
        _lblServerCount.Text = $"{online}/{_allServers.Count} online";
        _lblStatus.Text = $"{online} de {_allServers.Count} servidor(es) online.";
        _btnRefresh.Enabled = true;
    }

    private void RenderServerList()
    {
        string q = (_txtSearch.Text ?? "").Trim();
        _lstServers.BeginUpdate();
        _lstServers.Items.Clear();

        foreach (var pair in _allServers)
        {
            string name = pair.Info.Online && !string.IsNullOrWhiteSpace(pair.Info.ServerName)
                ? pair.Info.ServerName
                : pair.Entry.Name;

            if (!string.IsNullOrEmpty(q) &&
                !name.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !pair.Entry.Ip.Contains(q, StringComparison.OrdinalIgnoreCase))
                continue;

            var item = new ListViewItem(name) { Tag = pair };
            item.SubItems.Add($"{pair.Entry.Ip}:{pair.Entry.Port}");
            item.SubItems.Add(pair.Info.Online ? (string.IsNullOrWhiteSpace(pair.Info.GameMode) ? "Roleplay" : pair.Info.GameMode) : "Offline");
            item.SubItems.Add(pair.Info.Online ? $"{pair.Info.Players}/{pair.Info.MaxPlayers}" : "-");
            item.SubItems.Add(pair.Info.Online ? $"{pair.Info.PingMs} ms" : "-");
            string mpStatus = !pair.Info.Online ? "-" :
                              (!string.IsNullOrWhiteSpace(pair.Entry.ModpackUrl) ? "Sim (Auto)" : (pair.Info.HasPassword ? "Senha" : "Vanilla"));
            item.SubItems.Add(mpStatus);
            _lstServers.Items.Add(item);
        }

        _lstServers.EndUpdate();
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

    private void AddServerDialog()
    {
        using var dlg = new Form
        {
            Text = "Adicionar Servidor",
            Size = new Size(400, 230),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = BgCard,
            ForeColor = TextPrimary
        };

        AddFieldLabel(dlg, "NOME DO SERVIDOR", 18, 16);
        var tbName = new TextBox { Text = "Meu Servidor GTAMP" };
        tbName.SetBounds(18, 36, 346, 28);
        StyleInput(tbName);
        dlg.Controls.Add(tbName);

        AddFieldLabel(dlg, "IP / ENDEREÇO", 18, 76);
        var tbIp = new TextBox { Text = "127.0.0.1" };
        tbIp.SetBounds(18, 96, 236, 28);
        StyleInput(tbIp);
        dlg.Controls.Add(tbIp);

        AddFieldLabel(dlg, "PORTA", 268, 76);
        var numP = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = Protocol.DefaultPort, BackColor = BgInput, ForeColor = Color.White };
        numP.SetBounds(268, 96, 96, 28);
        dlg.Controls.Add(numP);

        var btnOk = new Button { Text = "Salvar", DialogResult = DialogResult.OK };
        btnOk.SetBounds(254, 144, 110, 32);
        StyleButton(btnOk, AccentGreen);
        dlg.Controls.Add(btnOk);
        dlg.AcceptButton = btnOk;

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        string ip = tbIp.Text.Trim();
        if (string.IsNullOrEmpty(ip)) return;

        string localFile = Path.Combine(AppContext.BaseDirectory, "servers.json");
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
        List<ServerListEntry> list = new();
        try
        {
            if (File.Exists(localFile))
                list = JsonSerializer.Deserialize<List<ServerListEntry>>(File.ReadAllText(localFile), opts) ?? new();
        }
        catch { }

        list.Add(new ServerListEntry
        {
            Name = string.IsNullOrWhiteSpace(tbName.Text) ? ip : tbName.Text.Trim(),
            Ip = ip,
            Port = (int)numP.Value
        });

        try { File.WriteAllText(localFile, JsonSerializer.Serialize(list, opts)); } catch { }
        _ = RefreshServersAsync();
    }

    private void OnServerSelected()
    {
        if (_lstServers.SelectedItems.Count == 0)
            return;

        if (_lstServers.SelectedItems[0].Tag is ValueTuple<ServerListEntry, ServerQueryResult> pair)
        {
            _txtIp.Text = pair.Item1.Ip;
            _numPort.Value = Math.Clamp(pair.Item1.Port, 1, 65535);
            _txtModpackUrl.Text = pair.Item1.ModpackUrl ?? "";

            string name = pair.Item2.Online && !string.IsNullOrWhiteSpace(pair.Item2.ServerName)
                ? pair.Item2.ServerName
                : pair.Item1.Name;
            _lblSelTitle.Text = name;
            _lblSelSub.Text = pair.Item2.Online
                ? $"{pair.Item1.Ip}:{pair.Item1.Port}  •  {pair.Item2.Players}/{pair.Item2.MaxPlayers} jogadores  •  {pair.Item2.PingMs} ms  •  {pair.Item2.Motd}"
                : $"{pair.Item1.Ip}:{pair.Item1.Port}  •  Servidor offline (você ainda pode tentar conectar)";
        }
    }

    // =====================================================================
    // Sync de Modpack e Inicializacao do GTA V
    // =====================================================================

    private async Task<bool> SyncModpackInternalAsync(bool showErrorDialog)
    {
        string url = _txtModpackUrl.Text.Trim();
        string gtaDir = _txtGtaDir.Text.Trim();
        if (!Directory.Exists(gtaDir))
        {
            if (showErrorDialog)
                MessageBox.Show("Selecione a pasta correta do GTA V em Configurações primeiro.", "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        string serverKey = $"{_txtIp.Text.Trim()}_{(int)_numPort.Value}";

        _btnSyncModpack.Enabled = false;
        _btnPlaySelected.Enabled = false;
        _btnPlayDirect.Enabled = false;
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
        _btnPlaySelected.Enabled = true;
        _btnPlayDirect.Enabled = true;
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
        if (!ValidateNickUi())
        {
            SwitchTab(2);
            MessageBox.Show($"Informe um apelido válido ({Protocol.MinNicknameLength}-{Protocol.MaxNicknameLength} caracteres) em Configurações.",
                "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string nick = Regex.Replace(_txtNick.Text.Trim(), @"\s+", " ");
        SaveSettingsFromUi(writeIniIfValid: false);

        string gtaDir = _txtGtaDir.Text.Trim();
        if (!Directory.Exists(gtaDir) || !File.Exists(Path.Combine(gtaDir, "GTA5.exe")))
        {
            SwitchTab(2);
            MessageBox.Show("Selecione a pasta correta do GTA V (onde está o GTA5.exe) em Configurações.",
                "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (startExe && _chkAutoSync.Checked && !string.IsNullOrWhiteSpace(_txtModpackUrl.Text))
        {
            bool syncOk = await SyncModpackInternalAsync(true);
            if (!syncOk)
                return;
        }

        WriteClientIni(gtaDir, nick);
        _lblStatus.Text = $"mp_config.ini salvo com apelido '{nick}'.";

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
            _lblStatus.Text = $"Iniciando GTA V como '{nick}'...";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erro ao iniciar o GTA V: " + ex.Message, "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }
}
