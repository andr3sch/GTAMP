using System.Text.Json;
using GTAMP.Shared;

namespace GTAMP.Launcher;

public sealed class ServerListEntry
{
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "127.0.0.1";
    public int Port { get; set; } = Protocol.DefaultPort;
    public string ModpackUrl { get; set; } = "";
}

public sealed class ServerQueryResult
{
    public bool Online { get; set; }
    public string ServerName { get; set; } = "";
    public string Motd { get; set; } = "";
    public int Players { get; set; }
    public int MaxPlayers { get; set; }
    public int PingMs { get; set; }
    public bool HasPassword { get; set; }
    public string GameMode { get; set; } = "";
    public string ModpackUrl { get; set; } = "";
}

public sealed class LauncherSettings
{
    public string Nickname { get; set; } = "Player" + Random.Shared.Next(100, 999);
    public string GtaFolder { get; set; } = @"D:\GTA V LIGHTER V10 By OptiJuegos\GTA V LIGHTER V10 By OptiJuegos\gta_data";
    public string ServerListUrl { get; set; } = "";
    public string LastIp { get; set; } = "127.0.0.1";
    public int LastPort { get; set; } = Protocol.DefaultPort;
    public string LastPassword { get; set; } = "";
    public bool VoiceActivation { get; set; } = false;
    public bool AutoSyncModpack { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "launcher_settings.json");

    public static LauncherSettings Load()
    {
        try
        {
            if (File.Exists(DefaultPath))
                return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(DefaultPath), JsonOpts) ?? new LauncherSettings();
        }
        catch { }
        return new LauncherSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(DefaultPath, JsonSerializer.Serialize(this, JsonOpts)); }
        catch { }
    }
}
