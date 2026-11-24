using System.Text.Json;
using GTAMP.Shared;

namespace GTAMP.Server;

public sealed class RpPlayerProfile
{
    public string Nickname { get; set; } = "";
    public long Cash { get; set; } = 1500;
    public long Bank { get; set; } = 5000;
    public string JobName { get; set; } = "Desempregado";
    public float LastX { get; set; } = -269.4f;
    public float LastY { get; set; } = -955.3f;
    public float LastZ { get; set; } = 31.2f;
    public bool HasSavedPosition { get; set; }
}

public sealed class RpMarkerDef
{
    public int Id { get; set; }
    public string Action { get; set; } = ""; // agency, garage, hospital, bank
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
}

/// <summary>
/// Persistencia leve de RP em JSON (rp_players.json e rp_markers.json).
/// Facil de inspecionar/editar manualmente ou migrar depois para SQLite/MySQL.
/// </summary>
public sealed class RpDatabase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _playersPath;
    private readonly string _markersPath;
    private readonly Dictionary<string, RpPlayerProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public List<RpMarkerDef> Markers { get; private set; } = new();

    public static readonly (string Name, int Salary)[] Jobs =
    {
        ("Desempregado", 150),
        ("Entregador", 600),
        ("Taxista", 750),
        ("Mecanico", 900),
        ("Policial", 1200),
        ("SAMU", 1100)
    };

    public RpDatabase(string baseDir)
    {
        _playersPath = Path.Combine(baseDir, "rp_players.json");
        _markersPath = Path.Combine(baseDir, "rp_markers.json");
        Load();
    }

    public RpPlayerProfile GetOrCreate(string nickname)
    {
        if (!_profiles.TryGetValue(nickname, out var p))
        {
            p = new RpPlayerProfile { Nickname = nickname };
            _profiles[nickname] = p;
            SavePlayers();
        }
        return p;
    }

    public static int GetSalary(string jobName)
    {
        foreach (var j in Jobs)
            if (string.Equals(j.Name, jobName, StringComparison.OrdinalIgnoreCase))
                return j.Salary;
        return 150;
    }

    public static string GetNextJob(string currentJob)
    {
        for (int i = 0; i < Jobs.Length; i++)
        {
            if (string.Equals(Jobs[i].Name, currentJob, StringComparison.OrdinalIgnoreCase))
                return Jobs[(i + 1) % Jobs.Length].Name;
        }
        return Jobs[1].Name;
    }

    public void Load()
    {
        if (File.Exists(_playersPath))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<RpPlayerProfile>>(File.ReadAllText(_playersPath), JsonOpts);
                if (list != null)
                {
                    foreach (var p in list)
                        if (!string.IsNullOrWhiteSpace(p.Nickname))
                            _profiles[p.Nickname] = p;
                }
            }
            catch (Exception ex) { Logger.Warn("Erro ao ler rp_players.json: " + ex.Message); }
        }

        if (File.Exists(_markersPath))
        {
            try
            {
                Markers = JsonSerializer.Deserialize<List<RpMarkerDef>>(File.ReadAllText(_markersPath), JsonOpts) ?? CreateDefaultMarkers();
            }
            catch { Markers = CreateDefaultMarkers(); }
        }
        else
        {
            Markers = CreateDefaultMarkers();
            SaveMarkers();
        }
    }

    public void SavePlayers()
    {
        try { File.WriteAllText(_playersPath, JsonSerializer.Serialize(_profiles.Values.ToList(), JsonOpts)); }
        catch { }
    }

    public void SaveMarkers()
    {
        try { File.WriteAllText(_markersPath, JsonSerializer.Serialize(Markers, JsonOpts)); }
        catch { }
    }

    private static List<RpMarkerDef> CreateDefaultMarkers() => new()
    {
        new RpMarkerDef
        {
            Id = 1, Action = "agency", Label = "Agencia de Empregos [E]",
            X = -268.9f, Y = -956.9f, Z = 31.2f, Radius = 2.0f,
            R = 50, G = 180, B = 255, BlipSprite = 407, BlipColor = 3
        },
        new RpMarkerDef
        {
            Id = 2, Action = "garage", Label = "Garagem Publica [E]",
            X = 215.1f, Y = -810.1f, Z = 30.7f, Radius = 2.5f,
            R = 80, G = 230, B = 120, BlipSprite = 357, BlipColor = 2
        },
        new RpMarkerDef
        {
            Id = 3, Action = "hospital", Label = "Hospital Pillbox - Cura [E]",
            X = 298.6f, Y = -584.5f, Z = 43.2f, Radius = 2.0f,
            R = 255, G = 80, B = 80, BlipSprite = 61, BlipColor = 1
        },
        new RpMarkerDef
        {
            Id = 4, Action = "bank", Label = "Banco Fleeca - Depositar Tudo [E]",
            X = 149.9f, Y = -1040.5f, Z = 29.3f, Radius = 1.8f,
            R = 255, G = 215, B = 0, BlipSprite = 108, BlipColor = 5
        }
    };
}
