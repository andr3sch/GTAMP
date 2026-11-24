using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using GTAMP.Shared;
using GTAMP.Shared.Packets;

namespace GTAMP.Server;

/// <summary>
/// API publica exposta para plugins C# (.dll em resources/plugins/) e scripts JSON (resources/scripts/).
/// Permite criar recursos estilo FiveM (comandos, markers, menus interativos, empregos, lojas) sem recompilar o cliente.
/// </summary>
public interface IServerApi
{
    void SendChat(PlayerSession player, string message);
    void BroadcastChat(string message);
    void Notify(PlayerSession player, string gtaText);
    void Teleport(PlayerSession player, float x, float y, float z, float heading = 0f);
    void SpawnVehicle(PlayerSession player, string modelName);
    void SetModel(PlayerSession player, string modelName);
    void GiveWeapon(PlayerSession player, string weaponName, int ammo = 250);
    void Heal(PlayerSession player, int health = 200);
    void OpenMenu(PlayerSession player, PlayerMenuPacket menu);
    void CloseMenu(PlayerSession player);
    void PlayAnimation(PlayerSession player, string dict, string name, int flags = 1);
    void StopAnimation(PlayerSession player);
    bool TryChargeCash(PlayerSession player, long amount);
    void GiveCash(PlayerSession player, long amount);
    RpPlayerProfile GetProfile(PlayerSession player);
}

/// <summary>Contrato para plugins C# colocados em resources/plugins/*.dll.</summary>
public interface IServerResource
{
    string Name { get; }
    string Version { get; }
    string Author { get; }

    void OnStart(IServerApi api);
    void OnStop();
    void OnPlayerJoin(PlayerSession player) { }
    void OnPlayerLeave(PlayerSession player) { }
    bool OnChatCommand(PlayerSession player, string command, string args) => false;
    bool OnMarkerInteract(PlayerSession player, int markerId) => false;
    bool OnMenuSelect(PlayerSession player, string menuId, string itemId) => false;
}

// =====================================================================
// Modelos de Resources declarativos em JSON (resources/scripts/*.json)
// =====================================================================

public sealed class JsonResourceDef
{
    public string Name { get; set; } = "unnamed";
    public string Version { get; set; } = "1.0";
    public string Author { get; set; } = "Server";
    public List<JsonMarkerDef> Markers { get; set; } = new();
    public List<JsonMenuDef> Menus { get; set; } = new();
    public List<JsonCommandDef> Commands { get; set; } = new();
}

public sealed class JsonMarkerDef
{
    public int Id { get; set; }
    public string Label { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; } = 2.0f;
    public byte R { get; set; } = 0;
    public byte G { get; set; } = 180;
    public byte B { get; set; } = 255;
    public int BlipSprite { get; set; } = 1;
    public int BlipColor { get; set; } = 3;
    public string Action { get; set; } = "open_menu"; // open_menu, spawn_vehicle, set_model, give_weapon, teleport, heal, notify, give_cash
    public string Param { get; set; } = "";
}

public sealed class JsonMenuDef
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public List<JsonMenuItemDef> Items { get; set; } = new();
}

public sealed class JsonMenuItemDef
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string RightLabel { get; set; } = "";
    public long Price { get; set; }
    public string Action { get; set; } = ""; // spawn_vehicle, set_model, give_weapon, teleport, heal, notify, open_menu, emote, radio
    public string Param { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class JsonCommandDef
{
    public string Command { get; set; } = ""; // ex: "/concessionaria"
    public string Action { get; set; } = "open_menu";
    public string Param { get; set; } = "";
}

/// <summary>
/// Gerenciador de Resources do servidor (estilo FiveM):
/// - resources/modpack/ -> arquivos enviados ao cliente via HTTP (/modpack.json)
/// - resources/scripts/ -> scripts JSON com markers, menus e comandos customizados
/// - resources/plugins/ -> plugins C# (.dll) carregados dinamicamente com hot-reload
/// </summary>
public sealed class ResourceManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _resourcesDir;
    private readonly string _scriptsDir;
    private readonly string _pluginsDir;
    private readonly string _modpackDir;
    private readonly IServerApi _api;

    private readonly List<JsonResourceDef> _jsonResources = new();
    private readonly Dictionary<int, JsonMarkerDef> _markers = new();
    private readonly Dictionary<string, JsonMenuDef> _menus = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonCommandDef> _commands = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<IServerResource> _plugins = new();
    private readonly List<AssemblyLoadContext> _pluginContexts = new();

    public string ModpackDir => _modpackDir;
    public IReadOnlyCollection<JsonMarkerDef> Markers => _markers.Values;
    public IReadOnlyList<string> LoadedResourceNames =>
        _jsonResources.Select(r => $"[JSON] {r.Name} v{r.Version}")
            .Concat(_plugins.Select(p => $"[DLL] {p.Name} v{p.Version}"))
            .ToList();

    public ResourceManager(string baseDir, IServerApi api)
    {
        _api = api;
        _resourcesDir = Path.Combine(baseDir, "resources");
        _scriptsDir = Path.Combine(_resourcesDir, "scripts");
        _pluginsDir = Path.Combine(_resourcesDir, "plugins");
        _modpackDir = Path.Combine(_resourcesDir, "modpack");

        EnsureDirectoriesAndSamples();
    }

    private void EnsureDirectoriesAndSamples()
    {
        Directory.CreateDirectory(_resourcesDir);
        Directory.CreateDirectory(_scriptsDir);
        Directory.CreateDirectory(_pluginsDir);
        Directory.CreateDirectory(_modpackDir);

        string readmePath = Path.Combine(_modpackDir, "LEIA-ME.txt");
        if (!File.Exists(readmePath))
        {
            File.WriteAllText(readmePath,
                "=== PASTA DE MODS DO SERVIDOR GTAMP (ESTILO FIVEM) ===\r\n" +
                "Coloque aqui os arquivos que os jogadores devem baixar automaticamente pelo Launcher.\r\n" +
                "A estrutura de pastas aqui dentro espelha a pasta gta_data do GTA V Lighter.\r\n\r\n" +
                "Exemplos:\r\n" +
                "  resources/modpack/mods/update/x64/dlcpacks/meucarro/dlc.rpf\r\n" +
                "  resources/modpack/scripts/meu_addon.dll\r\n\r\n" +
                "O servidor gera /modpack.json automaticamente com SHA-256 ao iniciar!\r\n" +
                "Arquivos LEIA-ME.txt sao ignorados pelo manifesto.\r\n");
        }

        string sampleScript = Path.Combine(_scriptsDir, "concessionaria_exemplo.json");
        if (!File.Exists(sampleScript))
        {
            var sample = new JsonResourceDef
            {
                Name = "concessionaria_rp",
                Version = "1.0",
                Author = "GTAMP",
                Markers = new List<JsonMarkerDef>
                {
                    new()
                    {
                        Id = 100,
                        Label = "Concessionaria Premium [E]",
                        X = -43.5f, Y = -1104.2f, Z = 26.4f,
                        Radius = 2.4f,
                        R = 255, G = 165, B = 0,
                        BlipSprite = 225, BlipColor = 47,
                        Action = "open_menu",
                        Param = "concessionaria"
                    }
                },
                Menus = new List<JsonMenuDef>
                {
                    new()
                    {
                        Id = "concessionaria",
                        Title = "Concessionaria",
                        Subtitle = "Veiculos do jogo base (Lighter)",
                        Items = new List<JsonMenuItemDef>
                        {
                            // Motos
                            new() { Id = "sanchez", Label = "Maibatsu Sanchez (Moto)", RightLabel = "$400", Price = 400, Action = "spawn_vehicle", Param = "sanchez", Message = "~g~Voce comprou uma Sanchez!" },
                            new() { Id = "akuma", Label = "Dinka Akuma (Moto)", RightLabel = "$650", Price = 650, Action = "spawn_vehicle", Param = "akuma", Message = "~g~Voce comprou uma Akuma!" },
                            new() { Id = "bati", Label = "Pegassi Bati 801 (Moto)", RightLabel = "$800", Price = 800, Action = "spawn_vehicle", Param = "bati", Message = "~g~Voce comprou uma Bati 801!" },
                            // Populares / Sedans / SUVs
                            new() { Id = "futo", Label = "Karin Futo", RightLabel = "$450", Price = 450, Action = "spawn_vehicle", Param = "futo", Message = "~g~Voce comprou um Futo!" },
                            new() { Id = "sultan", Label = "Karin Sultan", RightLabel = "$600", Price = 600, Action = "spawn_vehicle", Param = "sultan", Message = "~g~Voce comprou um Sultan!" },
                            new() { Id = "tailgater", Label = "Obey Tailgater", RightLabel = "$750", Price = 750, Action = "spawn_vehicle", Param = "tailgater", Message = "~g~Voce comprou um Tailgater!" },
                            new() { Id = "baller", Label = "Gallivanter Baller (SUV)", RightLabel = "$950", Price = 950, Action = "spawn_vehicle", Param = "baller", Message = "~g~Voce comprou um Baller!" },
                            new() { Id = "sandking", Label = "Vapid Sandking XL (4x4)", RightLabel = "$1100", Price = 1100, Action = "spawn_vehicle", Param = "sandking", Message = "~g~Voce comprou um Sandking XL!" },
                            // Muscle / Esportivos
                            new() { Id = "dominator", Label = "Vapid Dominator", RightLabel = "$1150", Price = 1150, Action = "spawn_vehicle", Param = "dominator", Message = "~g~Voce comprou um Dominator!" },
                            new() { Id = "gauntlet", Label = "Bravado Gauntlet", RightLabel = "$1200", Price = 1200, Action = "spawn_vehicle", Param = "gauntlet", Message = "~g~Voce comprou um Gauntlet!" },
                            new() { Id = "buffalo", Label = "Bravado Buffalo", RightLabel = "$1250", Price = 1250, Action = "spawn_vehicle", Param = "buffalo", Message = "~g~Voce comprou um Buffalo!" },
                            new() { Id = "banshee", Label = "Bravado Banshee", RightLabel = "$1450", Price = 1450, Action = "spawn_vehicle", Param = "banshee", Message = "~g~Voce comprou um Banshee!" },
                            new() { Id = "comet2", Label = "Pfister Comet", RightLabel = "$1600", Price = 1600, Action = "spawn_vehicle", Param = "comet2", Message = "~g~Voce comprou um Comet!" },
                            new() { Id = "ninef", Label = "Obey 9F", RightLabel = "$1800", Price = 1800, Action = "spawn_vehicle", Param = "ninef", Message = "~g~Voce comprou um 9F!" },
                            new() { Id = "feltzer2", Label = "Benefactor Feltzer", RightLabel = "$1950", Price = 1950, Action = "spawn_vehicle", Param = "feltzer2", Message = "~g~Voce comprou um Feltzer!" },
                            new() { Id = "carbonizzare", Label = "Grotti Carbonizzare", RightLabel = "$2200", Price = 2200, Action = "spawn_vehicle", Param = "carbonizzare", Message = "~g~Voce comprou um Carbonizzare!" },
                            // Super
                            new() { Id = "infernus", Label = "Pegassi Infernus", RightLabel = "$2600", Price = 2600, Action = "spawn_vehicle", Param = "infernus", Message = "~g~Voce comprou um Infernus!" },
                            new() { Id = "cheetah", Label = "Grotti Cheetah", RightLabel = "$3000", Price = 3000, Action = "spawn_vehicle", Param = "cheetah", Message = "~g~Voce comprou um Cheetah!" },
                            new() { Id = "entityxf", Label = "Overflod Entity XF", RightLabel = "$3300", Price = 3300, Action = "spawn_vehicle", Param = "entityxf", Message = "~g~Voce comprou um Entity XF!" },
                            new() { Id = "adder", Label = "Truffade Adder", RightLabel = "$3800", Price = 3800, Action = "spawn_vehicle", Param = "adder", Message = "~g~Voce comprou um Adder!" }
                        }
                    },
                    new()
                    {
                        Id = "vestiario",
                        Title = "Vestiario RP",
                        Subtitle = "Skins do jogo base (Lighter)",
                        Items = new List<JsonMenuItemDef>
                        {
                            new() { Id = "michael", Label = "Michael", RightLabel = "Gratis", Action = "set_model", Param = "player_zero" },
                            new() { Id = "franklin", Label = "Franklin", RightLabel = "Gratis", Action = "set_model", Param = "player_one" },
                            new() { Id = "trevor", Label = "Trevor", RightLabel = "Gratis", Action = "set_model", Param = "player_two" },
                            new() { Id = "civil_m", Label = "Civil Masculino", RightLabel = "Gratis", Action = "set_model", Param = "a_m_y_hipster_01" },
                            new() { Id = "civil_f", Label = "Civil Feminino", RightLabel = "Gratis", Action = "set_model", Param = "a_f_y_hipster_01" },
                            new() { Id = "policial", Label = "Policial LSPD", RightLabel = "Gratis", Action = "set_model", Param = "s_m_y_cop_01" },
                            new() { Id = "paramedico", Label = "Paramedico SAMU", RightLabel = "Gratis", Action = "set_model", Param = "s_m_m_paramedic_01" },
                            new() { Id = "mecanico", Label = "Mecanico", RightLabel = "Gratis", Action = "set_model", Param = "s_m_y_xmech_01" }
                        }
                    }
                },
                Commands = new List<JsonCommandDef>
                {
                    new() { Command = "/concessionaria", Action = "open_menu", Param = "concessionaria" },
                    new() { Command = "/vestiario", Action = "open_menu", Param = "vestiario" }
                }
            };
            File.WriteAllText(sampleScript, JsonSerializer.Serialize(sample, JsonOpts));
        }
    }

    public void LoadAll()
    {
        UnloadAll();
        LoadJsonScripts();
        LoadDllPlugins();
        Logger.Success($"Resources carregados: {_jsonResources.Count} script(s) JSON, {_plugins.Count} plugin(s) C#.");
    }

    public void UnloadAll()
    {
        foreach (var plugin in _plugins)
        {
            try { plugin.OnStop(); }
            catch (Exception ex) { Logger.Warn($"Erro ao parar plugin '{plugin.Name}': {ex.Message}"); }
        }
        _plugins.Clear();

        foreach (var ctx in _pluginContexts)
        {
            try { ctx.Unload(); } catch { }
        }
        _pluginContexts.Clear();

        _jsonResources.Clear();
        _markers.Clear();
        _menus.Clear();
        _commands.Clear();
    }

    private void LoadJsonScripts()
    {
        if (!Directory.Exists(_scriptsDir)) return;

        foreach (string file in Directory.GetFiles(_scriptsDir, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var def = JsonSerializer.Deserialize<JsonResourceDef>(File.ReadAllText(file), JsonOpts);
                if (def == null) continue;

                _jsonResources.Add(def);
                foreach (var m in def.Markers)
                    if (m.Id > 0) _markers[m.Id] = m;
                foreach (var menu in def.Menus)
                    if (!string.IsNullOrWhiteSpace(menu.Id)) _menus[menu.Id] = menu;
                foreach (var cmd in def.Commands)
                    if (!string.IsNullOrWhiteSpace(cmd.Command)) _commands[cmd.Command.ToLowerInvariant()] = cmd;

                Logger.Info($"  [Resource JSON] {def.Name} v{def.Version} ({def.Markers.Count} markers, {def.Menus.Count} menus, {def.Commands.Count} cmds)");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Falha ao carregar script '{Path.GetFileName(file)}': {ex.Message}");
            }
        }
    }

    private void LoadDllPlugins()
    {
        if (!Directory.Exists(_pluginsDir)) return;

        foreach (string dll in Directory.GetFiles(_pluginsDir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var ctx = new AssemblyLoadContext("GTAMP_Plugin_" + Path.GetFileNameWithoutExtension(dll), isCollectible: true);
                using var fs = File.OpenRead(dll);
                Assembly asm = ctx.LoadFromStream(fs);

                int found = 0;
                foreach (Type t in asm.GetTypes())
                {
                    if (t.IsAbstract || t.IsInterface || !typeof(IServerResource).IsAssignableFrom(t))
                        continue;

                    if (Activator.CreateInstance(t) is IServerResource res)
                    {
                        res.OnStart(_api);
                        _plugins.Add(res);
                        found++;
                        Logger.Info($"  [Resource DLL] {res.Name} v{res.Version} por {res.Author}");
                    }
                }

                if (found > 0)
                    _pluginContexts.Add(ctx);
                else
                    ctx.Unload();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Falha ao carregar plugin '{Path.GetFileName(dll)}': {ex.Message}");
            }
        }
    }

    public void NotifyPlayerJoin(PlayerSession player)
    {
        foreach (var p in _plugins)
        {
            try { p.OnPlayerJoin(player); } catch { }
        }
    }

    public void NotifyPlayerLeave(PlayerSession player)
    {
        foreach (var p in _plugins)
        {
            try { p.OnPlayerLeave(player); } catch { }
        }
    }

    public bool TryHandleCommand(PlayerSession player, string cmd, string args)
    {
        foreach (var p in _plugins)
        {
            try { if (p.OnChatCommand(player, cmd, args)) return true; }
            catch (Exception ex) { Logger.Warn($"Erro no comando {cmd} ({p.Name}): {ex.Message}"); }
        }

        if (_commands.TryGetValue(cmd.ToLowerInvariant(), out var def))
        {
            ExecuteAction(player, def.Action, def.Param, 0, "");
            return true;
        }

        return false;
    }

    public bool TryHandleMarkerInteract(PlayerSession player, int markerId)
    {
        foreach (var p in _plugins)
        {
            try { if (p.OnMarkerInteract(player, markerId)) return true; }
            catch (Exception ex) { Logger.Warn($"Erro no marker #{markerId} ({p.Name}): {ex.Message}"); }
        }

        if (_markers.TryGetValue(markerId, out var marker))
        {
            if (player.HasState)
            {
                float maxDist = marker.Radius + 4.0f;
                if (NetVector3.DistanceSquared(player.State.Position, new NetVector3(marker.X, marker.Y, marker.Z)) > maxDist * maxDist)
                    return true;
            }
            ExecuteAction(player, marker.Action, marker.Param, 0, "");
            return true;
        }

        return false;
    }

    public bool TryHandleMenuSelect(PlayerSession player, string menuId, string itemId)
    {
        foreach (var p in _plugins)
        {
            try { if (p.OnMenuSelect(player, menuId, itemId)) return true; }
            catch (Exception ex) { Logger.Warn($"Erro no menu '{menuId}' ({p.Name}): {ex.Message}"); }
        }

        if (_menus.TryGetValue(menuId, out var menu))
        {
            var item = menu.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                ExecuteAction(player, item.Action, item.Param, item.Price, item.Message);
                return true;
            }
        }

        return false;
    }

    public bool TryOpenJsonMenu(PlayerSession player, string menuId)
    {
        if (!_menus.TryGetValue(menuId, out var def))
            return false;

        var pkt = new PlayerMenuPacket
        {
            MenuId = def.Id,
            Title = def.Title,
            Subtitle = def.Subtitle,
            Close = false,
            Items = def.Items.Select(i => new MenuItemData
            {
                Id = i.Id,
                Label = i.Label,
                RightLabel = i.RightLabel
            }).ToList()
        };
        _api.OpenMenu(player, pkt);
        return true;
    }

    private void ExecuteAction(PlayerSession player, string action, string param, long price, string message)
    {
        if (price > 0 && !_api.TryChargeCash(player, price))
        {
            _api.Notify(player, $"~r~Dinheiro insuficiente!~w~ Necessario: ${price}");
            return;
        }

        switch ((action ?? "").ToLowerInvariant())
        {
            case "open_menu":
                if (!TryOpenJsonMenu(player, param))
                    _api.Notify(player, $"~r~Menu nao encontrado:~w~ {param}");
                break;

            case "spawn_vehicle":
                _api.SpawnVehicle(player, param);
                _api.CloseMenu(player);
                break;

            case "set_model":
                _api.SetModel(player, param);
                _api.CloseMenu(player);
                break;

            case "give_weapon":
                _api.GiveWeapon(player, param);
                break;

            case "heal":
                _api.Heal(player, 200);
                break;

            case "give_cash":
                if (long.TryParse(param, out long val) && val > 0)
                    _api.GiveCash(player, val);
                break;

            case "teleport":
                string[] parts = (param ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 &&
                    float.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out float z))
                {
                    _api.Teleport(player, x, y, z);
                    _api.CloseMenu(player);
                }
                break;

            case "notify":
                _api.Notify(player, param);
                break;
        }

        if (!string.IsNullOrWhiteSpace(message))
            _api.Notify(player, message);
    }
}
