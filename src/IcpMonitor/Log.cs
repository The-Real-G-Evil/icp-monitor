using System.Diagnostics;

namespace IcpMonitor;

/// <summary>Einfaches Datei-Log neben der exe. Rotiert ab 1 MB.</summary>
internal static class Log
{
    private const long MaxSize = 1_000_000;
    private static readonly object Sync = new();

    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "icp-monitor.log");

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
        Debug.Write(line);
        lock (Sync)
        {
            try
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxSize) File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, line);
            }
            catch
            {
                // Logging darf die App nie zum Absturz bringen.
            }
        }
    }
}
