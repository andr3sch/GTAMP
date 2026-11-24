using System.Runtime.InteropServices;
using System.Text;
using GTAMP.Server;
using GTAMP.Shared;

// =====================================================================
//  GTAMP - Servidor Dedicado (alvo do cliente: GTA V 1.0.463.1 / 1.0.505.2 + SHVDN 2.x)
// =====================================================================

// Desativa o QuickEdit do console do Windows para que clicar na janela NUNCA congele o servidor!
if (OperatingSystem.IsWindows())
    ConsoleHelper.DisableQuickEdit();

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("==============================================");
Console.WriteLine($"  GTAMP Dedicated Server  |  protocolo v{Protocol.Version}");
Console.WriteLine("  Alvo: GTA V 1.0.463.1 / ScriptHookVDotNet 2.x");
Console.WriteLine("==============================================");

string configPath = Path.Combine(AppContext.BaseDirectory, "server_config.json");
var config = ServerConfig.LoadOrCreate(configPath);

// Permite sobrescrever a porta via linha de comando: GTAMP.Server.exe --port 5000
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--port" && int.TryParse(args[i + 1], out int p) && p is > 0 and < 65536)
        config.Port = p;
}

var server = new GameServer(config);

// Ctrl+C -> desligamento limpo (avisa os clientes).
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    server.Stop();
};

// Thread de leitura do console: apenas enfileira; a execucao ocorre na thread do servidor.
var inputThread = new Thread(() =>
{
    while (true)
    {
        string line;
        try { line = Console.ReadLine(); }
        catch { break; }

        if (line == null) break;   // stdin fechado (ex.: rodando como servico)
        server.EnqueueCommand(line);
    }
})
{
    IsBackground = true,
    Name = "ConsoleInput"
};
inputThread.Start();

server.Run();

internal static class ConsoleHelper
{
    private const int STD_INPUT_HANDLE = -10;
    private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    private const uint ENABLE_EXTENDED_FLAGS = 0x0080;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    public static void DisableQuickEdit()
    {
        try
        {
            IntPtr handle = GetStdHandle(STD_INPUT_HANDLE);
            if (GetConsoleMode(handle, out uint mode))
            {
                mode &= ~ENABLE_QUICK_EDIT_MODE;
                mode |= ENABLE_EXTENDED_FLAGS;
                SetConsoleMode(handle, mode);
            }
        }
        catch { }
    }
}
