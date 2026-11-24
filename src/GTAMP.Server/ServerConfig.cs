using System.Text.Json;
using GTAMP.Shared;

namespace GTAMP.Server;

/// <summary>
/// Configuracao persistida em server_config.json (criado automaticamente na primeira execucao).
/// </summary>
public sealed class ServerConfig
{
    public string ServerName { get; set; } = "GTAMP 1.0.463.1 Freeroam";
    public string Motd { get; set; } = "Bem-vindo ao servidor! Digite /help no chat.";
    public int Port { get; set; } = Protocol.DefaultPort;
    public int MaxPlayers { get; set; } = 32;

    /// <summary>Vazio = servidor publico (sem senha).</summary>
    public string Password { get; set; } = "";

    /// <summary>Quantas vezes por segundo o servidor retransmite estados (10..60).</summary>
    public int TickRate { get; set; } = 20;

    /// <summary>Distancia maxima (metros) para receber estados de outro jogador. 0 = ilimitado.</summary>
    public float StreamDistance { get; set; } = 0f;

    /// <summary>Tempo sem resposta ate considerar o cliente desconectado.</summary>
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>Limite anti-flood de pacotes de estado por jogador por segundo.</summary>
    public int MaxStatePacketsPerSecond { get; set; } = 60;

    /// <summary>Intervalo minimo entre mensagens de chat do mesmo jogador.</summary>
    public int ChatCooldownMs { get; set; } = 750;

    /// <summary>Pacotes invalidos tolerados antes de expulsar o jogador.</summary>
    public int MaxInvalidPackets { get; set; } = 50;

    // ---- Configuracoes de Roleplay, Mundo e Voz ----
    public bool RoleplayMode { get; set; } = true;
    public float ChatProximityRange { get; set; } = Protocol.DefaultChatProximityRange;
    public float VoiceRange { get; set; } = Protocol.DefaultVoiceRange;
    public byte StartHour { get; set; } = 12;
    public byte StartMinute { get; set; } = 0;
    public string Weather { get; set; } = "EXTRASUNNY";
    public int PaydayIntervalSeconds { get; set; } = 300;

    // ---- Servidor HTTP de Mods/Resources e Painel Web Admin ----
    public bool HttpEnabled { get; set; } = true;
    public int HttpPort { get; set; } = Protocol.DefaultHttpPort;
    /// <summary>Host publico opcional (ex.: meu-servidor.com) usado nas URLs do modpack. Vazio = detecta pela requisicao.</summary>
    public string PublicHost { get; set; } = "";
    /// <summary>URL externa de modpack (sobrescreve o /modpack.json embutido se preenchida).</summary>
    public string ModpackUrl { get; set; } = "";
    /// <summary>Token de acesso ao Painel Web Admin (/admin) e API (/api/*).</summary>
    public string AdminToken { get; set; } = "gtamp-admin";

    // ---- Anti-Cheat e Master Server ----
    public bool EnableAntiCheat { get; set; } = true;
    public bool AutoKickCheaters { get; set; } = false;
    /// <summary>URL opcional de Master Server para listar o servidor publicamente.</summary>
    public string MasterServerUrl { get; set; } = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ServerConfig LoadOrCreate(string path)
    {
        ServerConfig cfg;
        if (File.Exists(path))
        {
            try
            {
                cfg = JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path), JsonOptions) ?? new ServerConfig();
                Logger.Info($"Configuracao carregada de {path}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Falha ao ler {path}: {ex.Message}. Usando valores padrao.");
                cfg = new ServerConfig();
            }
        }
        else
        {
            cfg = new ServerConfig();
            Logger.Warn($"{path} nao encontrado. Criando arquivo padrao.");
        }

        cfg.Sanitize();

        // Regrava para incluir campos novos/corrigidos.
        try { File.WriteAllText(path, JsonSerializer.Serialize(cfg, JsonOptions)); }
        catch (Exception ex) { Logger.Warn($"Nao foi possivel salvar {path}: {ex.Message}"); }

        return cfg;
    }

    private void Sanitize()
    {
        ServerName = string.IsNullOrWhiteSpace(ServerName) ? "GTAMP Server" : ServerName.Trim();
        if (ServerName.Length > Protocol.MaxShortString) ServerName = ServerName[..Protocol.MaxShortString];
        Motd ??= "";
        if (Motd.Length > Protocol.MaxLongString) Motd = Motd[..Protocol.MaxLongString];
        Password ??= "";
        Port = Math.Clamp(Port, 1, 65535);
        MaxPlayers = Math.Clamp(MaxPlayers, 1, 256);
        TickRate = Math.Clamp(TickRate, 10, 60);
        StreamDistance = Math.Max(0f, StreamDistance);
        TimeoutMs = Math.Clamp(TimeoutMs, 3000, 60000);
        MaxStatePacketsPerSecond = Math.Clamp(MaxStatePacketsPerSecond, 10, 240);
        ChatCooldownMs = Math.Clamp(ChatCooldownMs, 0, 10000);
        MaxInvalidPackets = Math.Max(1, MaxInvalidPackets);
        HttpPort = Math.Clamp(HttpPort, 1, 65535);
        PublicHost ??= "";
        ModpackUrl ??= "";
        AdminToken = string.IsNullOrWhiteSpace(AdminToken) ? "gtamp-admin" : AdminToken.Trim();
        MasterServerUrl ??= "";
    }
}
