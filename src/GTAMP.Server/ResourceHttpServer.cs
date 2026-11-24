using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GTAMP.Server;

/// <summary>
/// Servidor HTTP embutido no GTAMP.Server:
/// 1) /modpack.json -> manifesto gerado automaticamente a partir de resources/modpack/ com SHA-256
/// 2) /files/...    -> download direto dos arquivos de modpack para o Launcher
/// 3) /admin        -> Painel Web Admin completo (HTML/JS embutido)
/// 4) /api/status, /api/players, /api/command -> API JSON autenticada por AdminToken
/// </summary>
public sealed class ResourceHttpServer : IDisposable
{
    private sealed record CachedHash(long Length, long LastWriteUtcTicks, string Sha256);

    private readonly ServerConfig _config;
    private readonly string _modpackDir;
    private readonly Func<object> _getStatus;
    private readonly Func<object> _getPlayers;
    private readonly Action<string> _enqueueCommand;
    private readonly ConcurrentDictionary<string, CachedHash> _hashCache = new(StringComparer.OrdinalIgnoreCase);

    private HttpListener _listener;
    private Thread _thread;
    private volatile bool _running;

    public bool IsRunning => _running;

    public ResourceHttpServer(
        ServerConfig config,
        string modpackDir,
        Func<object> getStatus,
        Func<object> getPlayers,
        Action<string> enqueueCommand)
    {
        _config = config;
        _modpackDir = modpackDir;
        _getStatus = getStatus;
        _getPlayers = getPlayers;
        _enqueueCommand = enqueueCommand;
    }

    public bool HasModpackFiles()
    {
        if (!Directory.Exists(_modpackDir)) return false;
        return Directory.EnumerateFiles(_modpackDir, "*", SearchOption.AllDirectories)
            .Any(f => !Path.GetFileName(f).Equals("LEIA-ME.txt", StringComparison.OrdinalIgnoreCase));
    }

    public void Start()
    {
        if (!_config.HttpEnabled)
            return;

        _listener = new HttpListener();
        try
        {
            _listener.Prefixes.Add($"http://+:{_config.HttpPort}/");
            _listener.Start();
        }
        catch
        {
            // Sem permissao de URLACL para http://+:porta/ -> fallback para localhost
            _listener = new HttpListener();
            try
            {
                _listener.Prefixes.Add($"http://localhost:{_config.HttpPort}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{_config.HttpPort}/");
                _listener.Start();
                Logger.Warn($"HTTP embutido iniciado apenas em localhost:{_config.HttpPort} (execute como admin ou registre netsh http add urlacl para acesso externo).");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Nao foi possivel iniciar servidor HTTP na porta {_config.HttpPort}: {ex.Message}");
                return;
            }
        }

        _running = true;
        _thread = new Thread(ListenLoop) { IsBackground = true, Name = "GTAMP-Http" };
        _thread.Start();
        Logger.Success($"HTTP Mods/Admin ativo em http://localhost:{_config.HttpPort}/ (Painel: /admin | Modpack: /modpack.json)");
    }

    private void ListenLoop()
    {
        while (_running)
        {
            try
            {
                var ctx = _listener.GetContext();
                ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
            }
            catch when (!_running) { break; }
            catch { }
        }
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            string path = ctx.Request.Url?.AbsolutePath ?? "/";

            if (path.Equals("/modpack.json", StringComparison.OrdinalIgnoreCase))
            {
                ServeModpackManifest(ctx);
                return;
            }

            if (path.StartsWith("/files/", StringComparison.OrdinalIgnoreCase))
            {
                ServeModpackFile(ctx, Uri.UnescapeDataString(path.Substring("/files/".Length)));
                return;
            }

            if (path.Equals("/admin", StringComparison.OrdinalIgnoreCase) || path == "/")
            {
                WriteText(ctx, 200, "text/html; charset=utf-8", AdminHtml);
                return;
            }

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsAuthorized(ctx))
                {
                    WriteJson(ctx, 401, new { error = "Token invalido" });
                    return;
                }

                if (path.Equals("/api/status", StringComparison.OrdinalIgnoreCase))
                {
                    WriteJson(ctx, 200, _getStatus());
                    return;
                }

                if (path.Equals("/api/players", StringComparison.OrdinalIgnoreCase))
                {
                    WriteJson(ctx, 200, _getPlayers());
                    return;
                }

                if (path.Equals("/api/command", StringComparison.OrdinalIgnoreCase) && ctx.Request.HttpMethod == "POST")
                {
                    using var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                    string body = sr.ReadToEnd();
                    using var doc = JsonDocument.Parse(body);
                    string cmd = doc.RootElement.TryGetProperty("command", out var p) ? p.GetString() : "";
                    if (!string.IsNullOrWhiteSpace(cmd))
                        _enqueueCommand(cmd);
                    WriteJson(ctx, 200, new { ok = true, command = cmd });
                    return;
                }
            }

            WriteText(ctx, 404, "text/plain", "404 Not Found");
        }
        catch (Exception ex)
        {
            try { WriteText(ctx, 500, "text/plain", "500 Internal Error: " + ex.Message); } catch { }
        }
        finally
        {
            try { ctx.Response.OutputStream.Close(); } catch { }
        }
    }

    private bool IsAuthorized(HttpListenerContext ctx)
    {
        string token = ctx.Request.Headers["X-Admin-Token"] ?? ctx.Request.QueryString["token"] ?? "";
        return string.Equals(token, _config.AdminToken, StringComparison.Ordinal);
    }

    private void ServeModpackManifest(HttpListenerContext ctx)
    {
        string host = !string.IsNullOrWhiteSpace(_config.PublicHost)
            ? $"{_config.PublicHost}:{_config.HttpPort}"
            : (ctx.Request.UserHostName ?? $"127.0.0.1:{_config.HttpPort}");
        string baseUrl = $"http://{host}/files/";

        var files = new List<object>();
        if (Directory.Exists(_modpackDir))
        {
            foreach (string full in Directory.EnumerateFiles(_modpackDir, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(full).Equals("LEIA-ME.txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                string rel = Path.GetRelativePath(_modpackDir, full).Replace('\\', '/');
                string sha = GetOrComputeSha256(full);
                string encodedPath = string.Join("/", rel.Split('/').Select(Uri.EscapeDataString));

                files.Add(new
                {
                    Path = rel,
                    Url = baseUrl + encodedPath,
                    Sha256 = sha
                });
            }
        }

        var manifest = new
        {
            Name = _config.ServerName,
            Version = $"auto-{files.Count}",
            Files = files
        };

        WriteJson(ctx, 200, manifest);
    }

    private void ServeModpackFile(HttpListenerContext ctx, string relPath)
    {
        string root = Path.GetFullPath(_modpackDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string cleanRel = relPath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(_modpackDir, cleanRel));

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            WriteText(ctx, 404, "text/plain", "Arquivo nao encontrado");
            return;
        }

        var fi = new FileInfo(full);
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "application/octet-stream";
        ctx.Response.ContentLength64 = fi.Length;

        using var fs = File.OpenRead(full);
        fs.CopyTo(ctx.Response.OutputStream);
    }

    private string GetOrComputeSha256(string path)
    {
        var fi = new FileInfo(path);
        if (_hashCache.TryGetValue(path, out var cached) &&
            cached.Length == fi.Length &&
            cached.LastWriteUtcTicks == fi.LastWriteTimeUtc.Ticks)
        {
            return cached.Sha256;
        }

        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        string hex = Convert.ToHexString(sha.ComputeHash(fs));
        _hashCache[path] = new CachedHash(fi.Length, fi.LastWriteTimeUtc.Ticks, hex);
        return hex;
    }

    private static void WriteJson(HttpListenerContext ctx, int status, object payload)
    {
        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        WriteText(ctx, status, "application/json; charset=utf-8", json);
    }

    private static void WriteText(HttpListenerContext ctx, int status, string contentType, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    public void Dispose()
    {
        _running = false;
        try { _listener?.Stop(); _listener?.Close(); } catch { }
    }

    private const string AdminHtml = @"<!DOCTYPE html>
<html lang='pt-BR'>
<head>
<meta charset='utf-8'/>
<title>GTAMP Admin Panel</title>
<style>
  body { font-family: 'Segoe UI', sans-serif; background:#14171f; color:#e6edf3; margin:0; padding:24px; }
  .wrap { max-width:1000px; margin:0 auto; }
  h1 { margin:0 0 8px; color:#58a6ff; }
  .card { background:#1d222e; border:1px solid #2d3446; border-radius:10px; padding:16px; margin-bottom:16px; }
  .row { display:flex; gap:10px; flex-wrap:wrap; align-items:center; }
  input, button { background:#10131a; color:#fff; border:1px solid #384158; border-radius:6px; padding:8px 12px; font-size:14px; }
  button { background:#2b7de9; border:none; cursor:pointer; font-weight:600; }
  button:hover { background:#3a8cf5; }
  button.danger { background:#d94848; }
  button.sec { background:#3b4254; }
  table { width:100%; border-collapse:collapse; margin-top:10px; }
  th, td { text-align:left; padding:8px; border-bottom:1px solid #2d3446; font-size:14px; }
  .pill { display:inline-block; padding:3px 8px; border-radius:999px; background:#263042; font-size:12px; color:#8ecae6; }
</style>
</head>
<body>
<div class='wrap'>
  <h1>GTAMP — Painel Admin</h1>
  <div class='card row'>
    <input id='token' type='password' placeholder='AdminToken (server_config.json)' value='gtamp-admin'/>
    <button onclick='refreshAll()'>Conectar / Atualizar</button>
    <button class='sec' onclick=""sendCmd('reload')"">Recarregar Resources</button>
    <span id='msg' class='pill'>Aguardando...</span>
  </div>
  <div class='card' id='status'>Carregando status...</div>
  <div class='card'>
    <div class='row'>
      <input id='cmd' style='flex:1' placeholder='Comando do console (ex: say Ola! | weather CLEAR | time 12 0)'/>
      <button onclick='sendCmd()'>Executar</button>
    </div>
  </div>
  <div class='card'>
    <h3>Jogadores Online</h3>
    <table>
      <thead><tr><th>ID</th><th>Apelido</th><th>Ping</th><th>Voz/Radio</th><th>Posicao</th><th>Acoes</th></tr></thead>
      <tbody id='players'></tbody>
    </table>
  </div>
</div>
<script>
const hdr = () => ({ 'X-Admin-Token': document.getElementById('token').value });
async function refreshAll() {
  try {
    const s = await fetch('/api/status', { headers: hdr() });
    if (!s.ok) { document.getElementById('msg').textContent = 'Token invalido'; return; }
    const st = await s.json();
    document.getElementById('msg').textContent = 'Conectado';
    document.getElementById('status').innerHTML =
      `<b>${st.serverName}</b> &nbsp; <span class='pill'>${st.players}/${st.maxPlayers} online</span> ` +
      `<span class='pill'>Tick ${st.tickRate} Hz</span> <span class='pill'>Clima: ${st.weather} ${st.time}</span> ` +
      `<span class='pill'>Uptime: ${st.uptime}</span><br/><small>Resources: ${(st.resources||[]).join(', ') || 'nenhum'}</small>`;
    const p = await fetch('/api/players', { headers: hdr() });
    const list = await p.json();
    document.getElementById('players').innerHTML = list.map(pl =>
      `<tr><td>#${pl.id}</td><td>${pl.nickname}</td><td>${pl.ping} ms</td><td>${pl.voiceMode}${pl.radio>0?' (R:'+pl.radio+')':''}</td><td>${pl.pos}</td>` +
      `<td><button class='danger' onclick=""sendCmd('kick ${pl.id} Expulso via painel')"">Kick</button></td></tr>`
    ).join('') || '<tr><td colspan=6>Nenhum jogador online.</td></tr>';
  } catch (e) { document.getElementById('msg').textContent = 'Erro: ' + e; }
}
async function sendCmd(fixed) {
  const inp = document.getElementById('cmd');
  const command = fixed || inp.value.trim();
  if (!command) return;
  await fetch('/api/command', { method:'POST', headers:{ ...hdr(), 'Content-Type':'application/json' }, body: JSON.stringify({ command }) });
  if (!fixed) inp.value = '';
  setTimeout(refreshAll, 300);
}
refreshAll();
setInterval(refreshAll, 5000);
</script>
</body>
</html>";
}
