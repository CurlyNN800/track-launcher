namespace TrackLauncher.Core;

/// <summary>
/// Простой лог: пишет в %AppData%\TrackLauncher\logs\yyyy-MM-dd.log
/// и держит последние строки в памяти, чтобы показать их в настройках.
/// </summary>
public static class Logger
{
    private const int MaxRecent = 300;
    private static readonly object Sync = new();
    private static readonly LinkedList<string> RecentLines = new();

    public static event Action<string>? LineWritten;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    public static IReadOnlyList<string> Recent()
    {
        lock (Sync)
            return RecentLines.ToList();
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";
        lock (Sync)
        {
            RecentLines.AddLast(line);
            while (RecentLines.Count > MaxRecent)
                RecentLines.RemoveFirst();

            try
            {
                Directory.CreateDirectory(AppPaths.Logs);
                File.AppendAllText(Path.Combine(AppPaths.Logs, $"{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
            }
            catch
            {
                // Лог не должен ронять приложение.
            }
        }

        try { LineWritten?.Invoke(line); } catch { /* подписчики сами разберутся */ }
    }
}

/// <summary>Ошибка, текст которой можно показать пользователю как есть.</summary>
public sealed class LauncherException(string message) : Exception(message);
