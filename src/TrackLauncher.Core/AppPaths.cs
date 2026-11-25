namespace TrackLauncher.Core;

/// <summary>
/// Все пути, где лаунчер хранит данные: %AppData%\TrackLauncher\...
/// </summary>
public static class AppPaths
{
    public static string Root { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrackLauncher");

    public static string Tracks => Path.Combine(Root, "tracks");
    public static string Scripts => Path.Combine(Root, "scripts");
    public static string Logs => Path.Combine(Root, "logs");
    public static string WebViewData => Path.Combine(Root, "webview");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string SessionFile => Path.Combine(Root, "session.json");
    public static string StatsDb => Path.Combine(Root, "stats.db");

    /// <summary>Позволяет переопределить корень (например, для портативного режима или тестов).</summary>
    public static void OverrideRoot(string root) => Root = root;

    /// <summary>
    /// Создаёт папки и при первом запуске копирует примеры треков и скриптов.
    /// </summary>
    public static void EnsureCreated(string? samplesDir)
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Scripts);

        bool firstRun = !Directory.Exists(Tracks);
        Directory.CreateDirectory(Tracks);

        if (samplesDir is null || !Directory.Exists(samplesDir))
            return;

        var sampleTracks = Path.Combine(samplesDir, "tracks");
        if (firstRun && Directory.Exists(sampleTracks))
        {
            foreach (var file in Directory.GetFiles(sampleTracks, "*.json"))
                File.Copy(file, Path.Combine(Tracks, Path.GetFileName(file)), overwrite: false);
        }

        // Скрипты докладываем всегда, если каких-то не хватает (новые версии лаунчера могут приносить новые).
        var sampleScripts = Path.Combine(samplesDir, "scripts");
        if (Directory.Exists(sampleScripts))
        {
            foreach (var file in Directory.GetFiles(sampleScripts, "*.py"))
            {
                var target = Path.Combine(Scripts, Path.GetFileName(file));
                if (!File.Exists(target))
                    File.Copy(file, target);
            }
        }
    }
}

/// <summary>Хелперы для путей, которые пишет пользователь.</summary>
public static class PathUtil
{
    /// <summary>Раскрывает %ПЕРЕМЕННЫЕ% и ~ (домашняя папка), убирает кавычки и пробелы по краям.</summary>
    public static string Expand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var s = value.Trim().Trim('"');
        if (s == "~" || s.StartsWith("~/") || s.StartsWith("~\\"))
            s = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + s[1..];

        return Environment.ExpandEnvironmentVariables(s);
    }

    /// <summary>Раскрывает переменные в строке аргументов, ничего не трогая в кавычках.</summary>
    public static string ExpandArgs(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : Environment.ExpandEnvironmentVariables(value.Trim());

    /// <summary>Оборачивает значение в кавычки для командной строки, если нужно.</summary>
    public static string Quote(string value)
    {
        if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"'))
            return value;
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
