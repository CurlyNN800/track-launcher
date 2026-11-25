namespace TrackLauncher.Core;

public sealed class Settings
{
    /// <summary>Команда или путь к python.exe. Например "python", "py" или полный путь.</summary>
    public string PythonPath { get; set; } = "python";

    /// <summary>Запускать лаунчер вместе с Windows.</summary>
    public bool Autostart { get; set; }

    /// <summary>При входе в Windows сразу показывать окно с треками (иначе только значок в трее).</summary>
    public bool ShowOnLogin { get; set; } = true;

    /// <summary>Крестик прячет окно в трей, а не закрывает лаунчер.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Прятать окно лаунчера после запуска трека.</summary>
    public bool HideOnStart { get; set; } = true;

    /// <summary>Спрашивать подтверждение перед «Завершить трек».</summary>
    public bool ConfirmEnd { get; set; } = true;

    /// <summary>Глобальная горячая клавиша, показывает окно. Пусто — выключено.</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
}

public sealed class SettingsStore
{
    public Settings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                Current = Json.Read<Settings>(AppPaths.SettingsFile) ?? new Settings();
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось прочитать settings.json, беру настройки по умолчанию", ex);
            Current = new Settings();
        }
    }

    public void Save(Settings settings)
    {
        Current = settings;
        Json.Write(AppPaths.SettingsFile, settings);
    }
}
