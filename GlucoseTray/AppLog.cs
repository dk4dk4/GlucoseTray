namespace GlucoseTray;

// Minimal file logger. This app has no console and runs silently in the tray,
// so without this, failures (like a poll that keeps returning stale data) leave no trace.
public static class AppLog
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "glucosetray.log");
    private static readonly object Lock = new();
    private const long MaxSizeBytes = 2 * 1024 * 1024;

    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception ex) => Write("ERROR", $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxSizeBytes)
                    File.Delete(LogPath);

                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app it's trying to diagnose.
        }
    }
}
