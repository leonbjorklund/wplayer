using System.IO;

namespace WPlayer;

public static class AppLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Sync = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WPlayer",
        "wplayer.log");

    public static void Error(string context, Exception exception)
        => Write("ERROR", context, exception);

    public static void Info(string context)
        => Write("INFO", context, null);

    private static void Write(string level, string context, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                var directory = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(directory);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= MaxBytes)
                {
                    File.Move(LogPath, Path.Combine(directory, "wplayer.previous.log"), true);
                }

                var details = exception is null ? "" : $"{Environment.NewLine}{exception}";
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} [{level}] {context}{details}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
        }
    }
}
