namespace Launcher;

internal static class Log
{
    private static readonly string LogPath;
    private const long MaxBytes = 1 * 1024 * 1024;

    static Log()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Swift",
            "logs");
        Directory.CreateDirectory(dir);
        LogPath = Path.Combine(dir, "launcher.log");
    }

    internal static void Info(string message) { }
    internal static void Warn(string message) { }
    internal static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            Rotate();
            File.AppendAllText(LogPath,
                $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] [{level}] {message}{Environment.NewLine}");
        }
        catch { /* logging must never crash the launcher */ }
    }

    private static void Rotate()
    {
        var fi = new FileInfo(LogPath);
        if (fi.Exists && fi.Length > MaxBytes)
            File.Move(LogPath, LogPath + ".old", overwrite: true);
    }
}
