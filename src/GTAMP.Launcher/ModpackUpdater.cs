using System.Security.Cryptography;
using System.Text.Json;

namespace GTAMP.Launcher;

public sealed class ModpackManifest
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public List<ModpackFileEntry> Files { get; set; } = new();
}

public sealed class ModpackFileEntry
{
    /// <summary>Caminho relativo dentro da pasta gta_data (ex.: mods/update/x64/dlcpacks/rp/dlc.rpf).</summary>
    public string Path { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>SHA-256 opcional em hex. Se vazio, verifica apenas se o arquivo existe.</summary>
    public string Sha256 { get; set; } = "";
}

public sealed class ActiveModpackState
{
    public string ServerKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public List<string> InstalledRelativePaths { get; set; } = new();
}

/// <summary>
/// Gerenciador de Modpacks por Servidor (estilo FiveM):
/// - Mantem cache local por servidor em modpacks_cache/&lt;serverKey&gt;/
/// - Rastreia arquivos instalados em gtamp_active_modpack.json na pasta do GTA
/// - Ao trocar de servidor, remove os mods do servidor anterior sem tocar nos arquivos base do GTA
/// - Valida integridade com SHA-256
/// </summary>
public static class ModpackUpdater
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private const string ActiveStateFileName = "gtamp_active_modpack.json";

    public static async Task<(bool Ok, string Summary)> SyncAsync(
        HttpClient http,
        string manifestUrl,
        string gtaDir,
        string serverKey,
        Action<int, string> onProgress)
    {
        string rootFull = Path.GetFullPath(gtaDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string statePath = Path.Combine(gtaDir, ActiveStateFileName);

        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            CleanupPreviousModpack(gtaDir, rootFull, statePath, null);
            return (true, "Nenhum modpack configurado (GTA limpo).");
        }

        onProgress(0, "Baixando manifesto do modpack...");
        ModpackManifest manifest;
        try
        {
            string json = await http.GetStringAsync(manifestUrl);
            manifest = JsonSerializer.Deserialize<ModpackManifest>(json, JsonOpts);
            if (manifest == null || manifest.Files == null)
                return (false, "Manifesto de modpack invalido.");
        }
        catch (Exception ex)
        {
            return (false, "Erro ao baixar manifesto: " + ex.Message);
        }

        string safeKey = SanitizeKey(string.IsNullOrWhiteSpace(serverKey) ? manifest.Name : serverKey);
        string cacheRoot = Path.Combine(AppContext.BaseDirectory, "modpacks_cache", safeKey);
        Directory.CreateDirectory(cacheRoot);

        var targetSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Files)
        {
            if (!string.IsNullOrWhiteSpace(entry.Path))
                targetSet.Add(NormalizeRel(entry.Path));
        }

        // Remove arquivos de modpacks anteriores que nao fazem parte deste servidor
        CleanupPreviousModpack(gtaDir, rootFull, statePath, targetSet);

        int total = manifest.Files.Count;
        int downloaded = 0;
        int restoredFromCache = 0;
        var installedPaths = new List<string>();

        for (int i = 0; i < total; i++)
        {
            var entry = manifest.Files[i];
            if (string.IsNullOrWhiteSpace(entry.Path) || string.IsNullOrWhiteSpace(entry.Url))
                continue;

            string rel = NormalizeRel(entry.Path);
            string destFull = Path.GetFullPath(Path.Combine(gtaDir, rel));
            if (!destFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                return (false, "Caminho invalido no manifesto: " + entry.Path);

            string cacheFull = Path.GetFullPath(Path.Combine(cacheRoot, rel));
            int pct = total > 0 ? (i * 100 / total) : 100;
            onProgress(pct, $"Verificando ({i + 1}/{total}): {entry.Path}");

            // 1) Verifica se ja esta valido no GTA
            bool gtaValid = FileMatches(destFull, entry.Sha256);
            if (!gtaValid)
            {
                // 2) Tenta restaurar do cache local do servidor sem baixar novamente
                if (FileMatches(cacheFull, entry.Sha256))
                {
                    onProgress(pct, $"Aplicando do cache ({i + 1}/{total}): {entry.Path}");
                    CopyFileEnsuringDir(cacheFull, destFull);
                    restoredFromCache++;
                }
                else
                {
                    // 3) Baixa para o cache e copia para a pasta do GTA
                    onProgress(pct, $"Baixando ({i + 1}/{total}): {entry.Path}");
                    try
                    {
                        string cacheDir = Path.GetDirectoryName(cacheFull);
                        if (!string.IsNullOrEmpty(cacheDir)) Directory.CreateDirectory(cacheDir);

                        using var resp = await http.GetAsync(entry.Url, HttpCompletionOption.ResponseHeadersRead);
                        resp.EnsureSuccessStatusCode();
                        await using (var src = await resp.Content.ReadAsStreamAsync())
                        await using (var dst = File.Create(cacheFull))
                        {
                            await src.CopyToAsync(dst);
                        }

                        if (!string.IsNullOrWhiteSpace(entry.Sha256) &&
                            !string.Equals(ComputeSha256(cacheFull), entry.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(cacheFull); } catch { }
                            return (false, $"Hash SHA-256 invalido para {entry.Path}.");
                        }

                        CopyFileEnsuringDir(cacheFull, destFull);
                        downloaded++;
                    }
                    catch (Exception ex)
                    {
                        return (false, $"Falha ao baixar {entry.Path}: {ex.Message}");
                    }
                }
            }

            installedPaths.Add(rel);
        }

        // Salva estado do modpack ativo para poder limpar ao trocar de servidor
        var state = new ActiveModpackState
        {
            ServerKey = safeKey,
            Name = manifest.Name,
            Version = manifest.Version,
            InstalledRelativePaths = installedPaths
        };
        try { File.WriteAllText(statePath, JsonSerializer.Serialize(state, JsonOpts)); } catch { }

        string msg = $"Modpack '{manifest.Name}' v{manifest.Version} OK ({downloaded} baixado(s), {restoredFromCache} do cache).";
        onProgress(100, msg);
        return (true, msg);
    }

    private static void CleanupPreviousModpack(string gtaDir, string rootFull, string statePath, HashSet<string> keepRelPaths)
    {
        if (!File.Exists(statePath))
            return;

        try
        {
            var prev = JsonSerializer.Deserialize<ActiveModpackState>(File.ReadAllText(statePath), JsonOpts);
            if (prev?.InstalledRelativePaths != null)
            {
                foreach (string rel in prev.InstalledRelativePaths)
                {
                    if (keepRelPaths != null && keepRelPaths.Contains(rel))
                        continue;

                    string full = Path.GetFullPath(Path.Combine(gtaDir, rel));
                    if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                    {
                        try { File.Delete(full); } catch { }
                    }
                }
            }

            if (keepRelPaths == null || keepRelPaths.Count == 0)
            {
                try { File.Delete(statePath); } catch { }
            }
        }
        catch { }
    }

    private static bool FileMatches(string path, string expectedSha256)
    {
        if (!File.Exists(path)) return false;
        if (string.IsNullOrWhiteSpace(expectedSha256)) return true;
        return string.Equals(ComputeSha256(path), expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyFileEnsuringDir(string src, string dst)
    {
        string dir = Path.GetDirectoryName(dst);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Copy(src, dst, overwrite: true);
    }

    private static string NormalizeRel(string p) =>
        p.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

    private static string SanitizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "default";
        var chars = key.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray();
        return new string(chars);
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }
}
