using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GTAMP.Shared;

namespace GTAMP.Launcher;

/// <summary>
/// Launcher GTAMP com visual inspirado no dashboard oficial do FiveM:
/// - Barra superior limpa com logo transparente GTAMP, navegacao, contador online, pill de perfil e engrenagem.
/// - Pagina Inicial (Home) com Hero Server Card, Favoritos/Conexao Direta, Modo Historia, Crie um Servidor,
///   Aviso Importante (ambar) e Feed de Novidades a direita.
/// - Paginas completas de Servidores, Conexao Direta e Configuracoes.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly Regex NicknameRegex =
        new(@"^[\p{L}\p{N}_\-\.\[\] ]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Paleta FiveM Official Dark
    private static readonly Color BgWindow     = Color.FromArgb(18, 19, 26);   // #12131A
    private static readonly Color BgTopBar     = Color.FromArgb(15, 16, 22);   // #0F1016
    private static readonly Color BgCard       = Color.FromArgb(24, 26, 36);   // #181A24
    private static readonly Color BgCardHover  = Color.FromArgb(31, 34, 48);   // #1F2230
    private static readonly Color BgInput      = Color.FromArgb(30, 33, 46);
    private static readonly Color BgRowAlt     = Color.FromArgb(21, 23, 32);
    private static readonly Color BgRowSel     = Color.FromArgb(46, 32, 54);
    private static readonly Color BorderSubtle = Color.FromArgb(38, 41, 56);   // #262938
    private static readonly Color AccentPink   = Color.FromArgb(186, 29, 90);  // #BA1D5A (FiveM pill)
    private static readonly Color AccentGreen  = Color.FromArgb(39, 174, 96);
    private static readonly Color AccentBlue   = Color.FromArgb(43, 142, 255);
    private static readonly Color NoticeFill   = Color.FromArgb(94, 70, 37);   // #5E4625 (FiveM amber notice)
    private static readonly Color NoticeBorder = Color.FromArgb(181, 133, 54); // #B58536
    private static readonly Color TextPrimary  = Color.FromArgb(242, 244, 248);
    private static readonly Color TextMuted    = Color.FromArgb(146, 152, 172);

    private readonly LauncherSettings _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private List<(ServerListEntry Entry, ServerQueryResult Info)> _allServers = new();
    private Image _brandLogo;

    // Top Bar
    private readonly Panel _topBar = new();
    private readonly RoundedButton _navHome = new();
    private readonly RoundedButton _navServers = new();
    private readonly RoundedButton _navDirect = new();
    private readonly Label _lblOnlineTotal = new();
    private readonly RoundedButton _btnProfilePill = new();
    private readonly RoundedButton _btnGear = new();

    // Paginas
    private readonly Panel _contentHost = new();
    private readonly Panel _pageHome = new();
    private readonly Panel _pageServers = new();
    private readonly Panel _pageDirect = new();
    private readonly Panel _pageSettings = new();

    // Home Hero Server widgets
    private readonly Label _lblHeroTitle = new();
    private readonly Label _lblHeroDesc = new();
    private readonly Label _lblHeroPlayers = new();
    private readonly Label _lblHeroTags = new();
    private readonly RoundedButton _btnHeroConnect = new();
    private readonly Label _lblQuickPlaySub = new();

    // Pagina Servidores
    private readonly TextBox _txtSearch = new();
    private readonly RoundedButton _btnRefresh = new();
    private readonly RoundedButton _btnAddServer = new();
    private readonly Label _lblServerCount = new();
    private readonly ListView _lstServers = new();
    private readonly Label _lblSelTitle = new();
    private readonly Label _lblSelSub = new();
    private readonly RoundedButton _btnPlaySelected = new();

    // Pagina Conexao Direta
    private readonly TextBox _txtIp = new();
    private readonly NumericUpDown _numPort = new();
    private readonly TextBox _txtPassword = new();
    private readonly TextBox _txtModpackUrl = new();
    private readonly RoundedButton _btnSyncModpack = new();
    private readonly RoundedButton _btnPlayDirect = new();

    // Pagina Configuracoes
    private readonly TextBox _txtNick = new();
    private readonly Label _lblNickStatus = new();
    private readonly TextBox _txtGtaDir = new();
    private readonly Label _lblGtaStatus = new();
    private readonly TextBox _txtListUrl = new();
    private readonly CheckBox _chkVad = new();
    private readonly CheckBox _chkAutoSync = new();
    private readonly RoundedButton _btnSaveSettings = new();
    private readonly RoundedButton _btnCreateShortcut = new();

    // Barra inferior
    private readonly ProgressBar _prgModpack = new();
    private readonly Label _lblStatus = new();

    public MainForm()
    {
        _settings = LauncherSettings.Load();
        DoubleBuffered = true;
        LoadBrandLogo();

        BuildUi();
        LoadSettingsIntoUi();
        ValidateNickUi();
        UpdateGtaStatusUi();
        SwitchTab(0);

        Shown += async (_, _) => await RefreshServersAsync();
        FormClosing += (_, _) => SaveSettingsFromUi(writeIniIfValid: true);
    }

    private void LoadBrandLogo()
    {
        try
        {
            string png = Path.Combine(AppContext.BaseDirectory, "gtamp-logo.png");
            if (File.Exists(png))
                _brandLogo = Image.FromFile(png);
        }
        catch { }
    }

    // =====================================================================
    // Construcao da UI estilo FiveM
    // =====================================================================

    private void BuildUi()
    {
        Text = "GTAMP — GTA V Lighter Multiplayer";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        Size = new Size(1180, 690);
        MinimumSize = new Size(980, 610);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BgWindow;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9.5f);

        BuildTopBar();
        BuildBottomStatusBar();

        _contentHost.Dock = DockStyle.Fill;
        _contentHost.BackColor = BgWindow;
        _contentHost.Padding = new Padding(22, 16, 22, 14);
        Controls.Add(_contentHost);
        _contentHost.BringToFront();

        BuildHomePage();
        BuildServersPage();
        BuildDirectPage();
        BuildSettingsPage();

        _contentHost.Controls.Add(_pageHome);
        _contentHost.Controls.Add(_pageServers);
        _contentHost.Controls.Add(_pageDirect);
        _contentHost.Controls.Add(_pageSettings);
    }

    private void BuildTopBar()
    {
        _topBar.Dock = DockStyle.Top;
        _topBar.Height = 62;
        _topBar.BackColor = BgTopBar;
        _topBar.Padding = new Padding(18, 0, 18, 0);
        Controls.Add(_topBar);

        var bottomLine = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = BorderSubtle };
        _topBar.Controls.Add(bottomLine);

        // Esquerda: Logo transparente + links de navegacao
        var leftFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            Width = 620,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = BgTopBar,
            Padding = new Padding(0, 12, 0, 0)
        };
        _topBar.Controls.Add(leftFlow);

        var logoPic = new PictureBox
        {
            Size = new Size(86, 38),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 18, 0)
        };
        if (_brandLogo != null) logoPic.Image = _brandLogo;
        else if (Icon != null) logoPic.Image = Icon.ToBitmap();
        logoPic.Click += (_, _) => SwitchTab(0);
        leftFlow.Controls.Add(logoPic);

        AddTopNav(_navHome, "Início", 78, () => SwitchTab(0), leftFlow);
        AddTopNav(_navServers, "Servidores", 104, () => SwitchTab(1), leftFlow);
        AddTopNav(_navDirect, "Conexão Direta", 128, () => SwitchTab(2), leftFlow);

        var btnDiscord = new RoundedButton { Text = "Discord ↗", Size = new Size(92, 34), CornerRadius = 17, Margin = new Padding(6, 2, 4, 0) };
        btnDiscord.SetPalette(BgTopBar);
        btnDiscord.ForeColor = TextMuted;
        btnDiscord.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        btnDiscord.Click += (_, _) => OpenUrl("https://discord.gg/XEqfhjmYUm");
        leftFlow.Controls.Add(btnDiscord);

        var btnGithub = new RoundedButton { Text = "GitHub ↗", Size = new Size(88, 34), CornerRadius = 17, Margin = new Padding(2, 2, 0, 0) };
        btnGithub.SetPalette(BgTopBar);
        btnGithub.ForeColor = TextMuted;
        btnGithub.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        btnGithub.Click += (_, _) => OpenUrl("https://github.com/andr3sch/GTAMP");
        leftFlow.Controls.Add(btnGithub);

        // Direita: contador online + pill magenta de apelido + engrenagem
        var rightFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 360,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = BgTopBar,
            Padding = new Padding(0, 13, 0, 0)
        };
        _topBar.Controls.Add(rightFlow);

        _btnGear.Text = "⚙";
        _btnGear.Size = new Size(36, 36);
        _btnGear.CornerRadius = 18;
        _btnGear.Margin = new Padding(8, 0, 0, 0);
        _btnGear.Font = new Font("Segoe UI", 12f, FontStyle.Regular);
        _btnGear.SetPalette(BgCard);
        _btnGear.BorderColor = BorderSubtle;
        _btnGear.Click += (_, _) => SwitchTab(3);
        rightFlow.Controls.Add(_btnGear);

        _btnProfilePill.Text = "Player";
        _btnProfilePill.Size = new Size(150, 36);
        _btnProfilePill.CornerRadius = 18;
        _btnProfilePill.Margin = new Padding(8, 0, 0, 0);
        _btnProfilePill.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _btnProfilePill.SetPalette(AccentPink);
        _btnProfilePill.Click += (_, _) => SwitchTab(3);
        rightFlow.Controls.Add(_btnProfilePill);

        _lblOnlineTotal.Text = "👥 0 online";
        _lblOnlineTotal.ForeColor = TextMuted;
        _lblOnlineTotal.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _lblOnlineTotal.AutoSize = false;
        _lblOnlineTotal.Size = new Size(135, 36);
        _lblOnlineTotal.TextAlign = ContentAlignment.MiddleRight;
        rightFlow.Controls.Add(_lblOnlineTotal);
    }

    private void AddTopNav(RoundedButton btn, string text, int width, Action onClick, FlowLayoutPanel host)
    {
        btn.Text = text;
        btn.Size = new Size(width, 34);
        btn.CornerRadius = 17;
        btn.Margin = new Padding(2, 2, 4, 0);
        btn.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        btn.Click += (_, _) => onClick();
        host.Controls.Add(btn);
    }

    private void SwitchTab(int index)
    {
        _pageHome.Visible = index == 0;
        _pageServers.Visible = index == 1;
        _pageDirect.Visible = index == 2;
        _pageSettings.Visible = index == 3;

        RoundedButton[] navs = { _navHome, _navServers, _navDirect };
        for (int i = 0; i < navs.Length; i++)
        {
            bool active = i == index;
            navs[i].SetPalette(active ? Color.FromArgb(34, 37, 52) : BgTopBar);
            navs[i].ForeColor = active ? Color.White : TextMuted;
        }
        _btnGear.SetPalette(index == 3 ? AccentBlue : BgCard);
    }

    private void BuildBottomStatusBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            BackColor = BgTopBar,
            Padding = new Padding(18, 0, 18, 0)
        };
        bar.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = BorderSubtle });

        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _lblStatus.ForeColor = TextMuted;
        _lblStatus.Font = new Font("Segoe UI", 8.2f);
        _lblStatus.Text = "GTAMP v1.1.0 — GTA V Lighter 1.0.463.1";
        bar.Controls.Add(_lblStatus);

        _prgModpack.Dock = DockStyle.Right;
        _prgModpack.Width = 220;
        _prgModpack.Visible = false;
        bar.Controls.Add(_prgModpack);

        Controls.Add(bar);
    }

    // =====================================================================
    // Pagina 0: HOME DASHBOARD (layout fiel ao print do FiveM)
    // =====================================================================

    private void BuildHomePage()
    {
        _pageHome.Dock = DockStyle.Fill;
        _pageHome.BackColor = BgWindow;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = BgWindow
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _pageHome.Controls.Add(grid);

        // ---- COLUNA ESQUERDA (Principal) ----
        var leftGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0, 0, 14, 0),
            BackColor = BgWindow
        };
        leftGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 148f)); // Top tiles: Jogar + Favoritos/Direta
        leftGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // Hero Top Server
        leftGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 112f)); // Bottom tiles: Historia / Criar Servidor
        grid.Controls.Add(leftGrid, 0, 0);

        // Linha 1: Jogar (esquerda grande) + Favoritos / Conexao Direta (empilhados a direita)
        var rowTop = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 12),
            BackColor = BgWindow
        };
        rowTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68f));
        rowTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));
        leftGrid.Controls.Add(rowTop, 0, 0);

        // Card grande "JOGAR"
        var cardPlay = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 14,
            FillColor = Color.FromArgb(26, 22, 38),
            BorderColor = Color.FromArgb(62, 42, 74),
            Margin = new Padding(0, 0, 12, 0),
            Cursor = Cursors.Hand,
            Padding = new Padding(22, 18, 22, 16)
        };
        cardPlay.Click += (_, _) => SwitchTab(1);
        rowTop.Controls.Add(cardPlay, 0, 0);

        var lblPlayBig = new Label
        {
            Text = "JOGAR",
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(26, 22, 38),
            Location = new Point(22, 18),
            AutoSize = true,
            Cursor = Cursors.Hand
        };
        lblPlayBig.Click += (_, _) => SwitchTab(1);
        cardPlay.Controls.Add(lblPlayBig);

        _lblQuickPlaySub.Text = "Explorar servidores GTAMP online e entrar com 1 clique";
        _lblQuickPlaySub.ForeColor = TextMuted;
        _lblQuickPlaySub.BackColor = Color.FromArgb(26, 22, 38);
        _lblQuickPlaySub.Font = new Font("Segoe UI", 9.5f);
        _lblQuickPlaySub.Location = new Point(24, 64);
        _lblQuickPlaySub.Size = new Size(420, 24);
        _lblQuickPlaySub.Cursor = Cursors.Hand;
        _lblQuickPlaySub.Click += (_, _) => SwitchTab(1);
        cardPlay.Controls.Add(_lblQuickPlaySub);

        var btnQuickArrow = new RoundedButton { Text = "▶  Ver Servidores", CornerRadius = 16, Size = new Size(150, 32) };
        btnQuickArrow.Location = new Point(22, 92);
        btnQuickArrow.SetPalette(AccentPink);
        btnQuickArrow.Font = new Font("Segoe UI", 8.8f, FontStyle.Bold);
        btnQuickArrow.Click += (_, _) => SwitchTab(1);
        cardPlay.Controls.Add(btnQuickArrow);

        // Stack direita: FAVORITOS + CONEXAO DIRETA
        var stackRight = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = BgWindow
        };
        stackRight.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        stackRight.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        rowTop.Controls.Add(stackRight, 1, 0);

        var btnFavTile = CreateTileButton("SERVIDORES", "★", () => SwitchTab(1));
        btnFavTile.Margin = new Padding(0, 0, 0, 8);
        stackRight.Controls.Add(btnFavTile, 0, 0);

        var btnDirectTile = CreateTileButton("CONEXÃO DIRETA", "⚡", () => SwitchTab(2));
        btnDirectTile.Margin = new Padding(0, 2, 0, 0);
        stackRight.Controls.Add(btnDirectTile, 0, 1);

        // Linha 2: TOP SERVERS Hero Card
        var heroWrapper = new Panel { Dock = DockStyle.Fill, BackColor = BgWindow, Padding = new Padding(0, 2, 0, 12) };
        leftGrid.Controls.Add(heroWrapper, 0, 1);

        var lblTopTag = new Label
        {
            Text = "TOP SERVERS  🇧🇷",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = TextMuted,
            Dock = DockStyle.Top,
            Height = 22
        };
        heroWrapper.Controls.Add(lblTopTag);

        var heroCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 14,
            FillColor = BgCard,
            BorderColor = BorderSubtle,
            Padding = new Padding(20)
        };
        heroWrapper.Controls.Add(heroCard);
        heroCard.BringToFront();

        // Conteudo interno do Hero Card
        var heroIcon = new RoundedPanel
        {
            Size = new Size(76, 76),
            Location = new Point(22, 22),
            CornerRadius = 14,
            FillColor = Color.FromArgb(16, 20, 30),
            BorderColor = Color.FromArgb(46, 204, 113)
        };
        var heroPic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Padding = new Padding(8) };
        if (_brandLogo != null) heroPic.Image = _brandLogo;
        heroIcon.Controls.Add(heroPic);
        heroCard.Controls.Add(heroIcon);

        _lblHeroTitle.Text = "GTAMP 1.0.463.1 Roleplay";
        _lblHeroTitle.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
        _lblHeroTitle.ForeColor = Color.White;
        _lblHeroTitle.BackColor = BgCard;
        _lblHeroTitle.Location = new Point(114, 22);
        _lblHeroTitle.Size = new Size(560, 32);
        _lblHeroTitle.AutoEllipsis = true;
        heroCard.Controls.Add(_lblHeroTitle);

        _lblHeroDesc.Text = "Servidor dedicado GTAMP para GTA V Lighter — Voz 3D, Rádio, Concessionária e Mods automáticos.";
        _lblHeroDesc.Font = new Font("Segoe UI", 9.5f);
        _lblHeroDesc.ForeColor = TextMuted;
        _lblHeroDesc.BackColor = BgCard;
        _lblHeroDesc.Location = new Point(116, 56);
        _lblHeroDesc.Size = new Size(560, 42);
        heroCard.Controls.Add(_lblHeroDesc);

        _lblHeroPlayers.Text = "👥 0/32";
        _lblHeroPlayers.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _lblHeroPlayers.ForeColor = Color.FromArgb(72, 219, 130);
        _lblHeroPlayers.BackColor = BgCard;
        _lblHeroPlayers.Location = new Point(24, 108);
        _lblHeroPlayers.AutoSize = true;
        heroCard.Controls.Add(_lblHeroPlayers);

        // Rodape do Hero Card (Botao Conectar estilo pill outline FiveM + Tags)
        var heroFooter = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = BgCard };
        heroCard.Controls.Add(heroFooter);

        _btnHeroConnect.Text = "Conectar";
        _btnHeroConnect.Size = new Size(134, 38);
        _btnHeroConnect.Location = new Point(2, 8);
        _btnHeroConnect.CornerRadius = 19;
        _btnHeroConnect.SetPalette(Color.FromArgb(30, 34, 48));
        _btnHeroConnect.BorderColor = Color.FromArgb(130, 140, 165);
        _btnHeroConnect.BorderThickness = 1.4f;
        _btnHeroConnect.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _btnHeroConnect.Click += async (_, _) => await LaunchGameAsync(true);
        heroFooter.Controls.Add(_btnHeroConnect);

        _lblHeroTags.Text = "GTA V Lighter 1.0.463.1  •  Voz 3D  •  Roleplay RP";
        _lblHeroTags.Font = new Font("Segoe UI", 8.5f);
        _lblHeroTags.ForeColor = TextMuted;
        _lblHeroTags.BackColor = BgCard;
        _lblHeroTags.Dock = DockStyle.Right;
        _lblHeroTags.Width = 360;
        _lblHeroTags.TextAlign = ContentAlignment.MiddleRight;
        heroFooter.Controls.Add(_lblHeroTags);

        // Linha 3: Rodape esquerdo (MODO HISTORIA + CRIAR ATALHO + CRIE UM SERVIDOR)
        var rowBottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = BgWindow
        };
        rowBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        rowBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
        rowBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
        leftGrid.Controls.Add(rowBottom, 0, 2);

        var cardStory = CreateActionCard("MODO HISTÓRIA", "Abrir GTA V sem conectar ao MP", "▶", () => LaunchStoryModeOnly());
        cardStory.Margin = new Padding(0, 0, 10, 0);
        rowBottom.Controls.Add(cardStory, 0, 0);

        var cardShortcut = CreateActionCard("ATALHO DESKTOP", "Criar atalho com ícone oficial", "★", () => CreateDesktopShortcut());
        cardShortcut.Margin = new Padding(0, 0, 10, 0);
        rowBottom.Controls.Add(cardShortcut, 1, 0);

        var cardHost = CreateActionCard("CRIE UM SERVIDOR", "Manual de hospedagem VPS / VPN", "↗", () => OpenUrl("https://github.com/andr3sch/GTAMP/blob/main/docs/HOSTING.md"));
        rowBottom.Controls.Add(cardHost, 2, 0);

        // ---- COLUNA DIREITA (Aviso Ambar + Feed de Novidades) ----
        var rightGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = BgWindow
        };
        rightGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 128f));
        rightGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        grid.Controls.Add(rightGrid, 1, 0);

        // Card Ambar "AVISO IMPORTANTE" (identico ao print!)
        var noticeCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 12,
            FillColor = NoticeFill,
            BorderColor = NoticeBorder,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(14, 12, 14, 12)
        };
        rightGrid.Controls.Add(noticeCard, 0, 0);

        noticeCard.Controls.Add(new Label
        {
            Text = "⚠  AVISO IMPORTANTE",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 215, 130),
            BackColor = NoticeFill,
            Dock = DockStyle.Top,
            Height = 22
        });

        var lblNoticeBody = new Label
        {
            Text = "Seu save do Modo História fica 100% protegido durante o MP.\n\nNo jogo: F9 conecta | M menu | N voz 3D | Z alcance | T chat.",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(245, 232, 205),
            BackColor = NoticeFill,
            Dock = DockStyle.Fill
        };
        noticeCard.Controls.Add(lblNoticeBody);
        lblNoticeBody.BringToFront();

        // Card "NOVIDADES GTAMP" (estilo X.COM FEED do print)
        var feedCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 12,
            FillColor = BgCard,
            BorderColor = BorderSubtle,
            Padding = new Padding(14, 12, 14, 12)
        };
        rightGrid.Controls.Add(feedCard, 0, 1);

        var feedFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = BgCard
        };
        feedCard.Controls.Add(feedFlow);

        feedFlow.Controls.Add(new Label
        {
            Text = "NOVIDADES GTAMP",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = TextMuted,
            BackColor = BgCard,
            Size = new Size(250, 24)
        });

        AddFeedPost(feedFlow, "GTAMP v1.1.0", "Novo Launcher inspirado no FiveM com cards arredondados, logo oficial transparente e sistema de nicks corrigido (/nick).");
        AddFeedPost(feedFlow, "Voz 3D + Rádio", "Áudio 16 kHz com stereo panning 3D, modos Sussurrar/Normal/Gritar (Z) e walkie-talkie (/radio 190).");
        AddFeedPost(feedFlow, "Mods estilo FiveM", "Coloque carros, mapas ou scripts JSON em resources/ no servidor e o Launcher sincroniza automaticamente com SHA-256.");
        AddFeedPost(feedFlow, "Comunidade Discord", "Entre no nosso servidor oficial para suporte, modpacks e jogar com a galera do GTA V Lighter!");
    }

    private RoundedButton CreateTileButton(string title, string icon, Action onClick)
    {
        var btn = new RoundedButton
        {
            Dock = DockStyle.Fill,
            CornerRadius = 12,
            Text = $"{title}    {icon}",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(18, 0, 14, 0),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            BorderColor = BorderSubtle
        };
        btn.SetPalette(BgCard);
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private RoundedPanel CreateActionCard(string title, string subtitle, string icon, Action onClick)
    {
        var card = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 12,
            FillColor = BgCard,
            BorderColor = BorderSubtle,
            Cursor = Cursors.Hand,
            Padding = new Padding(14, 12, 14, 12)
        };
        card.Click += (_, _) => onClick();

        var lblT = new Label
        {
            Text = $"{title}  {icon}",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = BgCard,
            Location = new Point(14, 14),
            AutoSize = true,
            Cursor = Cursors.Hand
        };
        lblT.Click += (_, _) => onClick();
        card.Controls.Add(lblT);

        var lblS = new Label
        {
            Text = subtitle,
            Font = new Font("Segoe UI", 8.2f),
            ForeColor = TextMuted,
            BackColor = BgCard,
            Location = new Point(14, 42),
            Size = new Size(210, 44),
            Cursor = Cursors.Hand
        };
        lblS.Click += (_, _) => onClick();
        card.Controls.Add(lblS);

        return card;
    }

    private void AddFeedPost(FlowLayoutPanel host, string title, string body)
    {
        var post = new RoundedPanel
        {
            Size = new Size(256, 94),
            CornerRadius = 10,
            FillColor = Color.FromArgb(20, 22, 31),
            BorderColor = BorderSubtle,
            Margin = new Padding(0, 6, 0, 4),
            Padding = new Padding(10, 8, 10, 8)
        };
        post.Controls.Add(new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(20, 22, 31),
            Dock = DockStyle.Top,
            Height = 20
        });
        var lb = new Label
        {
            Text = body,
            Font = new Font("Segoe UI", 8f),
            ForeColor = TextMuted,
            BackColor = Color.FromArgb(20, 22, 31),
            Dock = DockStyle.Fill
        };
        post.Controls.Add(lb);
        lb.BringToFront();
        host.Controls.Add(post);
    }

    // =====================================================================
    // Pagina 1: SERVIDORES (Server Browser completo)
    // =====================================================================

    private void BuildServersPage()
    {
        _pageServers.Dock = DockStyle.Fill;
        _pageServers.BackColor = BgWindow;

        var topBar = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = BgWindow };
        _pageServers.Controls.Add(topBar);

        topBar.Controls.Add(new Label
        {
            Text = "Servidores",
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(0, 0),
            AutoSize = true
        });

        _lblServerCount.Text = "Consultando...";
        _lblServerCount.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _lblServerCount.ForeColor = AccentPink;
        _lblServerCount.Location = new Point(145, 11);
        _lblServerCount.AutoSize = true;
        topBar.Controls.Add(_lblServerCount);

        _txtSearch.SetBounds(0, 42, 360, 30);
        _txtSearch.PlaceholderText = "Buscar servidor por nome ou IP...";
        StyleInput(_txtSearch);
        _txtSearch.TextChanged += (_, _) => RenderServerList();
        topBar.Controls.Add(_txtSearch);

        _btnRefresh.Text = "Atualizar";
        _btnRefresh.SetBounds(372, 40, 120, 34);
        _btnRefresh.CornerRadius = 17;
        _btnRefresh.SetPalette(AccentBlue);
        _btnRefresh.Click += async (_, _) => await RefreshServersAsync();
        topBar.Controls.Add(_btnRefresh);

        _btnAddServer.Text = "+ Adicionar IP";
        _btnAddServer.SetBounds(502, 40, 134, 34);
        _btnAddServer.CornerRadius = 17;
        _btnAddServer.SetPalette(BgCard);
        _btnAddServer.BorderColor = BorderSubtle;
        _btnAddServer.Click += (_, _) => AddServerDialog();
        topBar.Controls.Add(_btnAddServer);

        // Card inferior do servidor selecionado (Dock limpo, nunca buga!)
        var selectedCard = new RoundedPanel
        {
            Dock = DockStyle.Bottom,
            Height = 82,
            CornerRadius = 14,
            FillColor = BgCard,
            BorderColor = BorderSubtle,
            Padding = new Padding(18, 12, 18, 12)
        };
        _pageServers.Controls.Add(selectedCard);

        var rightAction = new Panel { Dock = DockStyle.Right, Width = 220, BackColor = BgCard, Padding = new Padding(10, 6, 0, 6) };
        selectedCard.Controls.Add(rightAction);

        _btnPlaySelected.Text = "▶  CONECTAR";
        _btnPlaySelected.Dock = DockStyle.Fill;
        _btnPlaySelected.CornerRadius = 20;
        _btnPlaySelected.SetPalette(AccentGreen);
        _btnPlaySelected.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _btnPlaySelected.Click += async (_, _) => await LaunchGameAsync(true);
        rightAction.Controls.Add(_btnPlaySelected);

        var leftInfo = new Panel { Dock = DockStyle.Fill, BackColor = BgCard };
        selectedCard.Controls.Add(leftInfo);
        leftInfo.BringToFront();

        _lblSelTitle.Text = "Selecione um servidor na lista";
        _lblSelTitle.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        _lblSelTitle.ForeColor = Color.White;
        _lblSelTitle.BackColor = BgCard;
        _lblSelTitle.Dock = DockStyle.Top;
        _lblSelTitle.Height = 26;
        _lblSelTitle.AutoEllipsis = true;
        leftInfo.Controls.Add(_lblSelTitle);

        _lblSelSub.Text = "Clique duas vezes em um servidor ou clique em Conectar.";
        _lblSelSub.Font = new Font("Segoe UI", 9f);
        _lblSelSub.ForeColor = TextMuted;
        _lblSelSub.BackColor = BgCard;
        _lblSelSub.Dock = DockStyle.Fill;
        _lblSelSub.AutoEllipsis = true;
        leftInfo.Controls.Add(_lblSelSub);
        _lblSelSub.BringToFront();

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
        _lstServers.Columns.Add("SERVIDOR", 320);
        _lstServers.Columns.Add("ENDEREÇO", 180);
        _lstServers.Columns.Add("MODO", 110);
        _lstServers.Columns.Add("JOGADORES", 110);
        _lstServers.Columns.Add("PING", 90);
        _lstServers.Columns.Add("MODPACK", 130);
        _lstServers.DrawColumnHeader += OnDrawServerHeader;
        _lstServers.DrawItem += (_, e) => e.DrawDefault = false;
        _lstServers.DrawSubItem += OnDrawServerSubItem;
        _lstServers.SelectedIndexChanged += (_, _) => OnServerSelected();
        _lstServers.DoubleClick += async (_, _) => await LaunchGameAsync(true);
        listHost.Controls.Add(_lstServers);
    }

    private void OnDrawServerHeader(object sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var bg = new SolidBrush(Color.FromArgb(19, 20, 28));
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
            if (!data.Item2.Online) textColor = Color.FromArgb(115, 120, 140);
            else if (e.ColumnIndex == 4)
                textColor = data.Item2.PingMs < 80 ? Color.FromArgb(72, 219, 130) :
                            data.Item2.PingMs < 160 ? Color.FromArgb(245, 200, 75) : Color.FromArgb(245, 95, 95);
            else if (e.ColumnIndex == 5)
                textColor = e.SubItem.Text.StartsWith("Sim") ? Color.FromArgb(170, 135, 255) : TextMuted;
            else if (e.ColumnIndex == 2)
                textColor = AccentBlue;
        }

        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, font,
            new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height),
            textColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    // =====================================================================
    // Pagina 2: CONEXAO DIRETA
    // =====================================================================

    private void BuildDirectPage()
    {
        _pageDirect.Dock = DockStyle.Fill;
        _pageDirect.BackColor = BgWindow;

        _pageDirect.Controls.Add(new Label
        {
            Text = "Conexão Direta",
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(0, 0),
            AutoSize = true
        });
        _pageDirect.Controls.Add(new Label
        {
            Text = "Entre diretamente pelo IP de uma VPS, Radmin VPN, ZeroTier ou servidor local.",
            ForeColor = TextMuted,
            Location = new Point(2, 36),
            AutoSize = true
        });

        var card = new RoundedPanel
        {
            Location = new Point(0, 72),
            Size = new Size(760, 286),
            CornerRadius = 14,
            FillColor = BgCard,
            BorderColor = BorderSubtle
        };
        _pageDirect.Controls.Add(card);

        AddFieldLabel(card, "ENDEREÇO IP OU DOMÍNIO", 24, 22);
        _txtIp.SetBounds(24, 44, 360, 30);
        StyleInput(_txtIp);
        card.Controls.Add(_txtIp);

        AddFieldLabel(card, "PORTA UDP", 404, 22);
        _numPort.SetBounds(404, 44, 115, 30);
        _numPort.Minimum = 1;
        _numPort.Maximum = 65535;
        _numPort.Value = Protocol.DefaultPort;
        _numPort.BackColor = BgInput;
        _numPort.ForeColor = Color.White;
        card.Controls.Add(_numPort);

        AddFieldLabel(card, "SENHA (OPCIONAL)", 538, 22);
        _txtPassword.SetBounds(538, 44, 196, 30);
        _txtPassword.UseSystemPasswordChar = true;
        StyleInput(_txtPassword);
        card.Controls.Add(_txtPassword);

        AddFieldLabel(card, "URL DO MODPACK DO SERVIDOR (OPCIONAL)", 24, 100);
        _txtModpackUrl.SetBounds(24, 122, 540, 30);
        _txtModpackUrl.PlaceholderText = "Ex.: http://ip-do-servidor:4500/modpack.json";
        StyleInput(_txtModpackUrl);
        card.Controls.Add(_txtModpackUrl);

        _btnSyncModpack.Text = "Sincronizar";
        _btnSyncModpack.SetBounds(578, 120, 156, 34);
        _btnSyncModpack.CornerRadius = 17;
        _btnSyncModpack.SetPalette(Color.FromArgb(124, 77, 255));
        _btnSyncModpack.Click += async (_, _) => await SyncModpackOnlyAsync();
        card.Controls.Add(_btnSyncModpack);

        _btnPlayDirect.Text = "▶  CONECTAR E JOGAR";
        _btnPlayDirect.SetBounds(24, 194, 260, 46);
        _btnPlayDirect.CornerRadius = 23;
        _btnPlayDirect.SetPalette(AccentGreen);
        _btnPlayDirect.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        _btnPlayDirect.Click += async (_, _) => await LaunchGameAsync(true);
        card.Controls.Add(_btnPlayDirect);

        var btnSaveOnly = new RoundedButton { Text = "Salvar mp_config.ini", CornerRadius = 23 };
        btnSaveOnly.SetBounds(300, 194, 210, 46);
        btnSaveOnly.SetPalette(BgCardHover);
        btnSaveOnly.BorderColor = BorderSubtle;
        btnSaveOnly.Click += async (_, _) => await LaunchGameAsync(false);
        card.Controls.Add(btnSaveOnly);
    }

    // =====================================================================
    // Pagina 3: CONFIGURACOES
    // =====================================================================

    private void BuildSettingsPage()
    {
        _pageSettings.Dock = DockStyle.Fill;
        _pageSettings.BackColor = BgWindow;
        _pageSettings.AutoScroll = true;

        _pageSettings.Controls.Add(new Label
        {
            Text = "Configurações",
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(0, 0),
            AutoSize = true
        });

        var cardProfile = new RoundedPanel { Location = new Point(0, 48), Size = new Size(760, 114), CornerRadius = 14, FillColor = BgCard, BorderColor = BorderSubtle };
        _pageSettings.Controls.Add(cardProfile);

        AddFieldLabel(cardProfile, "SEU APELIDO NO JOGO (3 A 24 CARACTERES)", 20, 16);
        _txtNick.SetBounds(20, 38, 320, 30);
        _txtNick.MaxLength = Protocol.MaxNicknameLength;
        StyleInput(_txtNick);
        _txtNick.TextChanged += (_, _) => ValidateNickUi();
        cardProfile.Controls.Add(_txtNick);

        _lblNickStatus.Location = new Point(356, 44);
        _lblNickStatus.Size = new Size(380, 24);
        _lblNickStatus.BackColor = BgCard;
        _lblNickStatus.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        cardProfile.Controls.Add(_lblNickStatus);

        cardProfile.Controls.Add(new Label
        {
            Text = "Aparece no chat e acima do personagem. Você também pode mudar in-game com /nick <novo>.",
            ForeColor = TextMuted,
            BackColor = BgCard,
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(20, 76),
            AutoSize = true
        });

        var cardGta = new RoundedPanel { Location = new Point(0, 176), Size = new Size(760, 114), CornerRadius = 14, FillColor = BgCard, BorderColor = BorderSubtle };
        _pageSettings.Controls.Add(cardGta);

        AddFieldLabel(cardGta, "PASTA DO GTA V LIGHTER (ONDE FICA O GTA5.EXE)", 20, 16);
        _txtGtaDir.SetBounds(20, 38, 595, 30);
        StyleInput(_txtGtaDir);
        _txtGtaDir.TextChanged += (_, _) => UpdateGtaStatusUi();
        cardGta.Controls.Add(_txtGtaDir);

        var btnBrowse = new RoundedButton { Text = "Procurar...", CornerRadius = 16 };
        btnBrowse.SetBounds(626, 36, 112, 34);
        btnBrowse.SetPalette(BgCardHover);
        btnBrowse.BorderColor = BorderSubtle;
        btnBrowse.Click += (_, _) => BrowseGtaFolder();
        cardGta.Controls.Add(btnBrowse);

        _lblGtaStatus.Location = new Point(20, 76);
        _lblGtaStatus.Size = new Size(720, 24);
        _lblGtaStatus.BackColor = BgCard;
        _lblGtaStatus.Font = new Font("Segoe UI", 9f);
        cardGta.Controls.Add(_lblGtaStatus);

        var cardExtra = new RoundedPanel { Location = new Point(0, 304), Size = new Size(760, 144), CornerRadius = 14, FillColor = BgCard, BorderColor = BorderSubtle };
        _pageSettings.Controls.Add(cardExtra);

        AddFieldLabel(cardExtra, "URL DE LISTA DE SERVIDORES JSON (OPCIONAL)", 20, 14);
        _txtListUrl.SetBounds(20, 36, 718, 30);
        _txtListUrl.PlaceholderText = "Vazio = usa servers.json local";
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

        _btnSaveSettings.Text = "Salvar Configurações";
        _btnSaveSettings.SetBounds(0, 464, 210, 42);
        _btnSaveSettings.CornerRadius = 21;
        _btnSaveSettings.SetPalette(AccentPink);
        _btnSaveSettings.Click += (_, _) =>
        {
            if (!ValidateNickUi())
            {
                MessageBox.Show("Corrija o apelido antes de salvar.", "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SaveSettingsFromUi(writeIniIfValid: true);
            _lblStatus.Text = "Configurações e apelido salvos com sucesso!";
        };
        _pageSettings.Controls.Add(_btnSaveSettings);

        _btnCreateShortcut.Text = "Criar Atalho na Área de Trabalho";
        _btnCreateShortcut.SetBounds(226, 464, 250, 42);
        _btnCreateShortcut.CornerRadius = 21;
        _btnCreateShortcut.SetPalette(BgCard);
        _btnCreateShortcut.BorderColor = BorderSubtle;
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

    // =====================================================================
    // Configuracoes, Validacao de Nick e Atalho
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
        _btnProfilePill.Text = _settings.Nickname;
    }

    private bool ValidateNickUi()
    {
        string nick = Regex.Replace((_txtNick.Text ?? "").Trim(), @"\s+", " ");
        _btnProfilePill.Text = string.IsNullOrEmpty(nick) ? "Definir Apelido" : nick;

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
            WriteClientIni(_settings.GtaFolder, _settings.Nickname, autoConnect: true);
    }

    private void WriteClientIni(string gtaDir, string nick, bool autoConnect)
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
                $"AutoConnect={(autoConnect ? "true" : "false")}\r\n" +
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
    // Servidores & Hero Update
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

        int onlineCount = _allServers.Count(r => r.Info.Online);
        int totalPlayers = _allServers.Where(r => r.Info.Online).Sum(r => r.Info.Players);
        _lblServerCount.Text = $"{onlineCount}/{_allServers.Count} online";
        _lblOnlineTotal.Text = $"👥 {totalPlayers} online";
        _lblStatus.Text = $"{onlineCount} de {_allServers.Count} servidor(es) online.";

        // Atualiza o Hero Server Card na Home com o primeiro servidor online (ou o primeiro da lista)
        var featured = _allServers.FirstOrDefault(s => s.Info.Online);
        if (featured.Entry == null && _allServers.Count > 0)
            featured = _allServers[0];
        if (featured.Entry != null)
            ApplyHeroServer(featured.Entry, featured.Info);

        _btnRefresh.Enabled = true;
    }

    private void ApplyHeroServer(ServerListEntry entry, ServerQueryResult info)
    {
        _txtIp.Text = entry.Ip;
        _numPort.Value = Math.Clamp(entry.Port, 1, 65535);
        _txtModpackUrl.Text = entry.ModpackUrl ?? "";

        _lblHeroTitle.Text = info.Online && !string.IsNullOrWhiteSpace(info.ServerName) ? info.ServerName : entry.Name;
        _lblHeroDesc.Text = info.Online && !string.IsNullOrWhiteSpace(info.Motd)
            ? info.Motd
            : $"{entry.Ip}:{entry.Port} — {(info.Online ? "Online" : "Offline (inicie o GTAMP.Server.exe)")}";
        _lblHeroPlayers.Text = info.Online ? $"👥 {info.Players}/{info.MaxPlayers}   •   {info.PingMs} ms" : "● Offline";
        _lblHeroPlayers.ForeColor = info.Online ? Color.FromArgb(72, 219, 130) : TextMuted;
        _lblHeroTags.Text = $"{(string.IsNullOrWhiteSpace(info.GameMode) ? "Roleplay RP" : info.GameMode)}  •  GTA V Lighter 1.0.463.1  •  Voz 3D";
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
            catch (Exception ex) { _lblStatus.Text = "Falha na URL JSON: " + ex.Message; }
        }

        string localFile = Path.Combine(AppContext.BaseDirectory, "servers.json");
        if (File.Exists(localFile))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<ServerListEntry>>(File.ReadAllText(localFile), opts);
                if (list != null) return list;
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

        var btnOk = new Button { Text = "Salvar", DialogResult = DialogResult.OK, BackColor = AccentGreen, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnOk.FlatAppearance.BorderSize = 0;
        btnOk.SetBounds(254, 144, 110, 32);
        dlg.Controls.Add(btnOk);
        dlg.AcceptButton = btnOk;

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string ip = tbIp.Text.Trim();
        if (string.IsNullOrEmpty(ip)) return;

        string localFile = Path.Combine(AppContext.BaseDirectory, "servers.json");
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
        List<ServerListEntry> list = new();
        try { if (File.Exists(localFile)) list = JsonSerializer.Deserialize<List<ServerListEntry>>(File.ReadAllText(localFile), opts) ?? new(); } catch { }

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
        if (_lstServers.SelectedItems.Count == 0) return;
        if (_lstServers.SelectedItems[0].Tag is ValueTuple<ServerListEntry, ServerQueryResult> pair)
        {
            ApplyHeroServer(pair.Item1, pair.Item2);
            string name = pair.Item2.Online && !string.IsNullOrWhiteSpace(pair.Item2.ServerName) ? pair.Item2.ServerName : pair.Item1.Name;
            _lblSelTitle.Text = name;
            _lblSelSub.Text = pair.Item2.Online
                ? $"{pair.Item1.Ip}:{pair.Item1.Port}  •  {pair.Item2.Players}/{pair.Item2.MaxPlayers} jogadores  •  {pair.Item2.PingMs} ms  •  {pair.Item2.Motd}"
                : $"{pair.Item1.Ip}:{pair.Item1.Port}  •  Servidor offline";
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
        _btnHeroConnect.Enabled = false;
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
        _btnHeroConnect.Enabled = true;
        return ok;
    }

    private async Task SyncModpackOnlyAsync()
    {
        if (string.IsNullOrWhiteSpace(_txtModpackUrl.Text))
        {
            MessageBox.Show("Informe a URL de um manifesto JSON de modpack.", "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        await SyncModpackInternalAsync(true);
    }

    private void LaunchStoryModeOnly()
    {
        string gtaDir = _txtGtaDir.Text.Trim();
        if (!Directory.Exists(gtaDir) || !File.Exists(Path.Combine(gtaDir, "GTA5.exe")))
        {
            SwitchTab(3);
            MessageBox.Show("Selecione a pasta correta do GTA V em Configurações.", "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        WriteClientIni(gtaDir, _settings.Nickname, autoConnect: false);
        StartGtaProcess(gtaDir);
    }

    private async Task LaunchGameAsync(bool startExe)
    {
        if (!ValidateNickUi())
        {
            SwitchTab(3);
            MessageBox.Show($"Informe um apelido válido ({Protocol.MinNicknameLength}-{Protocol.MaxNicknameLength} caracteres) em Configurações.",
                "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string nick = Regex.Replace(_txtNick.Text.Trim(), @"\s+", " ");
        SaveSettingsFromUi(writeIniIfValid: false);

        string gtaDir = _txtGtaDir.Text.Trim();
        if (!Directory.Exists(gtaDir) || !File.Exists(Path.Combine(gtaDir, "GTA5.exe")))
        {
            SwitchTab(3);
            MessageBox.Show("Selecione a pasta correta do GTA V (onde está o GTA5.exe) em Configurações.",
                "GTAMP Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (startExe && _chkAutoSync.Checked && !string.IsNullOrWhiteSpace(_txtModpackUrl.Text))
        {
            bool syncOk = await SyncModpackInternalAsync(true);
            if (!syncOk) return;
        }

        WriteClientIni(gtaDir, nick, autoConnect: true);
        _lblStatus.Text = $"mp_config.ini salvo com apelido '{nick}'.";

        if (startExe)
            StartGtaProcess(gtaDir);
    }

    private void StartGtaProcess(string gtaDir)
    {
        string playExe = Path.Combine(gtaDir, "PlayGTAV.exe");
        string targetExe = File.Exists(playExe) ? playExe : Path.Combine(gtaDir, "GTA5.exe");
        try
        {
            Process.Start(new ProcessStartInfo { FileName = targetExe, WorkingDirectory = gtaDir, UseShellExecute = true });
            _lblStatus.Text = "Iniciando GTA V...";
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
