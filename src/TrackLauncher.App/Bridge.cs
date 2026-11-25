using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using TrackLauncher.Core;
using TrackLauncher.Core.Engine;
using TrackLauncher.Core.Models;
using TrackLauncher.Core.Python;
using TrackLauncher.Core.Stats;
using TrackLauncher.Core.Windows;

namespace TrackLauncher.App;

/// <summary>
/// Диспетчер команд от интерфейса. Каждая команда — один case.
/// Чтобы добавить новую: case "имя" => ..., а в JS — call("имя", {...}).
/// </summary>
public sealed class Bridge(TrackEngine engine, App app, MainWindow window)
{
    public async Task<object?> HandleAsync(string action, JsonElement payload)
    {
        switch (action)
        {
            case "getInit":
                return new
                {
                    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3),
                    tracks = engine.Tracks.LoadAll(),
                    loadErrors = LoadErrors(),
                    status = engine.GetStatus(),
                    settings = engine.Settings,
                    monitors = Monitors(),
                    browsers = Browsers.ShortNames.Where(b => Browsers.Resolve(b) is not null).ToList(),
                    dataFolder = AppPaths.Root,
                };

            case "getTracks":
                return new { tracks = engine.Tracks.LoadAll(), loadErrors = LoadErrors() };

            case "getTrack":
                return engine.Tracks.Get(Str(payload, "id")) ?? throw new LauncherException("Трек не найден");

            case "validateTrack":
                return TrackValidator.Validate(ReadTrack(payload), engine.Tracks, AppPaths.Scripts);

            case "saveTrack":
            {
                var track = ReadTrack(payload);
                var issues = TrackValidator.Validate(track, engine.Tracks, AppPaths.Scripts);
                if (issues.Any(i => i.Level == "error"))
                    return new { saved = false, issues };
                engine.Tracks.Save(track);
                return new { saved = true, track, issues };
            }

            case "deleteTrack":
            {
                var id = Str(payload, "id");
                if (engine.Active?.TrackId == id)
                    throw new LauncherException("Нельзя удалить трек, пока он запущен");
                engine.Tracks.Delete(id);
                return null;
            }

            case "duplicateTrack":
            {
                var source = engine.Tracks.Get(Str(payload, "id")) ?? throw new LauncherException("Трек не найден");
                source.Id = engine.Tracks.UniqueId(source.Id);
                source.Name += " (копия)";
                return engine.Tracks.Save(source);
            }

            case "exportTrack":
                return ExportTrack(Str(payload, "id"));

            case "importTrack":
                return ImportTrack();

            case "startTrack":
            {
                var id = Str(payload, "id");
                if (engine.Active is not null && Bool(payload, "endCurrent"))
                    await engine.EndAsync();
                await engine.StartAsync(id);
                if (engine.Settings.HideOnStart)
                    window.Hide();
                return engine.GetStatus();
            }

            case "endTrack":
                return await engine.EndAsync();

            case "getStatus":
                return engine.GetStatus();

            case "captureLayout":
                return new { updated = engine.CaptureLayout() };

            case "testStep":
            {
                var step = payload.GetProperty("step").Deserialize<Step>(Json.Options) ?? throw new LauncherException("Пустой шаг");
                var trackId = payload.TryGetProperty("trackId", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                return await engine.TestStepAsync(step, string.IsNullOrEmpty(trackId) ? null : trackId);
            }

            case "pickFile":
                return PickFile(Str(payload, "kind", "exe"));

            case "pickFolder":
                return PickFolder();

            case "getMonitors":
                return Monitors();

            case "getStats":
            {
                var colors = engine.Tracks.LoadAll().ToDictionary(t => t.Id, t => t.Color);
                return StatsReport.Build(engine.Stats, Int(payload, "days", 7), id => colors.GetValueOrDefault(id));
            }

            case "exportStatsCsv":
                return ExportStats();

            case "getSettings":
                return engine.Settings;

            case "saveSettings":
            {
                var settings = payload.Deserialize<Settings>(Json.Options) ?? throw new LauncherException("Пустые настройки");
                engine.SettingsStore.Save(settings);
                var warning = app.ApplySettings();
                return new { settings, warning };
            }

            case "checkPython":
                return await PythonRunner.GetVersionAsync(Str(payload, "path", engine.Settings.PythonPath));

            case "openFolder":
                OpenFolder(Str(payload, "kind", "data"));
                return null;

            case "getLog":
                return Logger.Recent();

            case "hideWindow":
                window.Hide();
                return null;

            case "quit":
                app.ExitApp();
                return null;

            default:
                throw new LauncherException($"Неизвестная команда: {action}");
        }
    }

    // ───────────── файлы и диалоги ─────────────

    private object? ExportTrack(string id)
    {
        var track = engine.Tracks.Get(id) ?? throw new LauncherException("Трек не найден");
        var dialog = new SaveFileDialog
        {
            Title = "Экспорт трека",
            FileName = $"{track.Id}.json",
            Filter = "Трек (*.json)|*.json",
        };
        if (dialog.ShowDialog(window) != true)
            return null;
        File.Copy(engine.Tracks.FileFor(track.Id), dialog.FileName, overwrite: true);
        return dialog.FileName;
    }

    private Track? ImportTrack()
    {
        var dialog = new OpenFileDialog { Title = "Импорт трека", Filter = "Трек (*.json)|*.json" };
        if (dialog.ShowDialog(window) != true)
            return null;

        Track track;
        try
        {
            track = Json.Read<Track>(dialog.FileName) ?? throw new LauncherException("Файл пустой");
        }
        catch (JsonException ex)
        {
            throw new LauncherException($"Это не похоже на трек: {ex.Message}");
        }

        var baseId = TrackRepository.IsValidId(track.Id) ? track.Id : TrackRepository.Slugify(track.Name);
        track.Id = engine.Tracks.UniqueId(baseId);
        if (string.IsNullOrWhiteSpace(track.Name))
            track.Name = track.Id;
        return engine.Tracks.Save(track);
    }

    private string? ExportStats()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Экспорт статистики",
            FileName = $"tracks-stats-{DateTime.Now:yyyy-MM-dd}.csv",
            Filter = "CSV (*.csv)|*.csv",
        };
        if (dialog.ShowDialog(window) != true)
            return null;
        // BOM нужен, чтобы Excel правильно открыл кириллицу.
        File.WriteAllText(dialog.FileName, engine.Stats.ExportCsv(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return dialog.FileName;
    }

    private string? PickFile(string kind)
    {
        var dialog = new OpenFileDialog
        {
            Title = kind == "script" ? "Выбери Python-скрипт" : "Выбери программу",
            Filter = kind switch
            {
                "script" => "Python (*.py)|*.py|Все файлы|*.*",
                "python" => "python.exe|python*.exe;py.exe|Все файлы|*.*",
                "exe" => "Программы (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Все файлы|*.*",
                _ => "Все файлы|*.*",
            },
            DereferenceLinks = false,
        };
        if (kind == "script")
            dialog.InitialDirectory = AppPaths.Scripts;
        if (dialog.ShowDialog(window) != true)
            return null;

        // Скрипты из папки scripts пишем коротким именем — так трек переносится на другой комп.
        if (kind == "script" && Path.GetDirectoryName(dialog.FileName)?.Equals(AppPaths.Scripts, StringComparison.OrdinalIgnoreCase) == true)
            return Path.GetFileName(dialog.FileName);
        return dialog.FileName;
    }

    private string? PickFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Выбери папку" };
        return dialog.ShowDialog(window) == true ? dialog.FolderName : null;
    }

    private static void OpenFolder(string kind)
    {
        var path = kind switch
        {
            "scripts" => AppPaths.Scripts,
            "logs" => AppPaths.Logs,
            "tracks" => AppPaths.Tracks,
            _ => AppPaths.Root,
        };
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false })?.Dispose();
    }

    // ───────────── мелочи ─────────────

    private object LoadErrors() =>
        engine.Tracks.LastErrors.Select(e => new { file = e.File, error = e.Error }).ToList();

    private static object Monitors() =>
        WindowManager.GetMonitors().Select(m => new
        {
            index = m.Index,
            primary = m.IsPrimary,
            width = m.Bounds.Width,
            height = m.Bounds.Height,
            label = $"{m.Index + 1}{(m.IsPrimary ? " — основной" : "")} ({m.Bounds.Width}×{m.Bounds.Height})",
        }).ToList();

    private static Track ReadTrack(JsonElement payload)
    {
        var element = payload.TryGetProperty("track", out var t) ? t : payload;
        try
        {
            return element.Deserialize<Track>(Json.Options) ?? throw new LauncherException("Пустой трек");
        }
        catch (JsonException ex)
        {
            throw new LauncherException($"Трек записан неправильно: {ex.Message}");
        }
    }

    private static string Str(JsonElement payload, string name, string? fallback = null)
    {
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString()!;
        return fallback ?? throw new LauncherException($"Не передан параметр {name}");
    }

    private static int Int(JsonElement payload, string name, int fallback) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : fallback;

    private static bool Bool(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
