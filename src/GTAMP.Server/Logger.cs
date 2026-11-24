namespace GTAMP.Server;

/// <summary>Logger de console simples, colorido e thread-safe.</summary>
public static class Logger
{
    private static readonly object Sync = new();

    public static void Info(string msg) => Write("INFO ", msg, ConsoleColor.Gray);
    public static void Success(string msg) => Write("OK   ", msg, ConsoleColor.Green);
    public static void Warn(string msg) => Write("WARN ", msg, ConsoleColor.Yellow);
    public static void Error(string msg) => Write("ERRO ", msg, ConsoleColor.Red);
    public static void Chat(string msg) => Write("CHAT ", msg, ConsoleColor.Cyan);

    private static void Write(string level, string msg, ConsoleColor color)
    {
        lock (Sync)
        {
            var old = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
            Console.ForegroundColor = color;
            Console.Write(level);
            Console.ForegroundColor = old;
            Console.WriteLine(msg);
        }
    }
}
