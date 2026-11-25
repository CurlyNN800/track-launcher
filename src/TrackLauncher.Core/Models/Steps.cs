using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrackLauncher.Core.Engine;
using TrackLauncher.Core.Python;
using TrackLauncher.Core.Windows;
using IOPath = System.IO.Path;

namespace TrackLauncher.Core.Models;

/// <summary>
/// Базовый шаг трека. Тип шага в JSON задаётся полем "type".
/// Чтобы добавить новый тип: унаследоваться от Step, реализовать ExecuteAsync и Describe
/// и дописать строчку [JsonDerivedType] ниже. Плюс описать поля в ui/js/schema.js.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AppStep), "app")]
[JsonDerivedType(typeof(UrlStep), "url")]
[JsonDerivedType(typeof(FolderStep), "folder")]
[JsonDerivedType(typeof(CommandStep), "command")]
[JsonDerivedType(typeof(PythonStep), "python")]
[JsonDerivedType(typeof(WaitStep), "wait")]
[JsonDerivedType(typeof(NotifyStep), "notify")]
[JsonDerivedType(typeof(TimerStep), "timer")]
[JsonDerivedType(typeof(TrackStep), "track")]
public abstract class Step
{
    /// <summary>Подпись шага в интерфейсе. Если пусто — генерируется из параметров.</summary>
    public string? Label { get; set; }

    /// <summary>Выключенный шаг пропускается.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Если шаг упал — идти дальше (true) или остановить трек (false).</summary>
    public bool ContinueOnError { get; set; } = true;

    public abstract Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct);

    /// <summary>Короткое описание для интерфейса и логов.</summary>
    public abstract string Describe();

    public string DisplayName() => string.IsNullOrWhiteSpace(Label) ? Describe() : Label!;
}

/// <summary>Шаг, который открывает окно: его можно расставить и закрыть при завершении трека.</summary>
public abstract class WindowStep : Step
{
    public Layout? Layout { get; set; }

    /// <summary>Не закрывать окно этого шага при «Завершить трек».</summary>
    public bool KeepOpen { get; set; }
}

// ───────────────────────────── Открыть программу ─────────────────────────────

public sealed class AppStep : WindowStep
{
    public string Path { get; set; } = "";
    public string? Args { get; set; }
    public string? WorkingDir { get; set; }
    public WindowMatch? WindowMatch { get; set; }

    /// <summary>Сколько секунд ждать появления окна (Unity с большим проектом — долго).</summary>
    public int WindowTimeoutSec { get; set; } = 60;

    /// <summary>Пауза после появления окна перед тем, как его двигать: многие программы сами себя переставляют на старте.</summary>
    public int SettleMs { get; set; } = 1000;

    /// <summary>Не переходить к следующему шагу, пока окно не появится.</summary>
    public bool Blocking { get; set; }

    public override string Describe() =>
        $"Открыть {IOPath.GetFileNameWithoutExtension(PathUtil.Expand(Path))}" + (string.IsNullOrWhiteSpace(Args) ? "" : $" {Args}");

    public override async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var path = PathUtil.Expand(Path);
        if (path.Length == 0)
            return StepResult.Fail("Не указан путь к программе");

        var before = WindowManager.SnapshotHandles();
        var psi = new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Arguments = PathUtil.ExpandArgs(Args),
        };

        var workingDir = PathUtil.Expand(WorkingDir);
        if (workingDir.Length > 0)
            psi.WorkingDirectory = workingDir;
        else if (File.Exists(path))
            psi.WorkingDirectory = IOPath.GetDirectoryName(path)!;

        try
        {
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            return StepResult.Fail($"Не удалось запустить {path}: {ex.Message}");
        }

        var query = new WindowQuery(
            string.IsNullOrWhiteSpace(WindowMatch?.Process) ? IOPath.GetFileNameWithoutExtension(path) : WindowMatch!.Process!,
            WindowMatch?.Title);

        var tracking = ctx.Engine.TrackWindowAsync(ctx, query, before, Layout,
            TimeSpan.FromSeconds(Math.Max(1, WindowTimeoutSec)), SettleMs, KeepOpen);

        if (!Blocking)
            return StepResult.Success("Запущено, окно ищется в фоне");

        var found = await tracking;
        return StepResult.Success(found ? "Окно открылось" : "Запущено, но новое окно не найдено");
    }
}

// ───────────────────────────── Открыть сайты ─────────────────────────────

public sealed class UrlStep : WindowStep
{
    public List<string> Urls { get; set; } = [];

    /// <summary>chrome, edge, firefox, brave, yandex, opera или полный путь к exe. Пусто — браузер по умолчанию.</summary>
    public string? Browser { get; set; }

    /// <summary>Профиль браузера, например "Profile 1" (Chrome/Edge) или имя профиля Firefox.</summary>
    public string? Profile { get; set; }

    /// <summary>Открыть в отдельном окне — тогда его можно расставить и закрыть при завершении.</summary>
    public bool NewWindow { get; set; } = true;

    public override string Describe()
    {
        var first = Urls.FirstOrDefault() ?? "";
        try { first = new Uri(first).Host; } catch { /* оставим как есть */ }
        return Urls.Count > 1 ? $"Сайты: {first} и ещё {Urls.Count - 1}" : $"Сайт: {first}";
    }

    public override async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var urls = Urls.Select(u => u.Trim()).Where(u => u.Length > 0).ToList();
        if (urls.Count == 0)
            return StepResult.Fail("Не указано ни одного адреса");

        if (string.IsNullOrWhiteSpace(Browser))
        {
            foreach (var url in urls)
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            return StepResult.Success("Открыто в браузере по умолчанию (такие вкладки лаунчер не закроет)");
        }

        var exe = Browsers.Resolve(Browser);
        if (exe is null)
            return StepResult.Fail($"Браузер «{Browser}» не найден. Укажи полный путь к exe.");

        bool firefox = IOPath.GetFileNameWithoutExtension(exe).Equals("firefox", StringComparison.OrdinalIgnoreCase);
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(Profile))
            args.Add(firefox ? $"-P {PathUtil.Quote(Profile)}" : $"--profile-directory={PathUtil.Quote(Profile)}");
        if (NewWindow)
            args.Add(firefox ? "-new-window" : "--new-window");
        args.AddRange(urls.Select(PathUtil.Quote));

        var before = WindowManager.SnapshotHandles();
        try
        {
            Process.Start(new ProcessStartInfo(exe, string.Join(' ', args)) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex)
        {
            return StepResult.Fail($"Не удалось запустить браузер: {ex.Message}");
        }

        if (!NewWindow)
            return StepResult.Success("Открыто во вкладках");

        var query = new WindowQuery(IOPath.GetFileNameWithoutExtension(exe), null);
        var found = await ctx.Engine.TrackWindowAsync(ctx, query, before, Layout, TimeSpan.FromSeconds(20), 500, KeepOpen);
        return StepResult.Success(found ? "Окно браузера открыто" : "Открыто, но новое окно не найдено");
    }
}

// ───────────────────────────── Открыть папку ─────────────────────────────

public sealed class FolderStep : WindowStep
{
    public string Path { get; set; } = "";

    public override string Describe() => $"Папка {IOPath.GetFileName(PathUtil.Expand(Path).TrimEnd('\\', '/'))}";

    public override async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var path = PathUtil.Expand(Path);
        if (!Directory.Exists(path))
            return StepResult.Fail($"Папка не найдена: {path}");

        var before = WindowManager.SnapshotHandles();
        Process.Start(new ProcessStartInfo("explorer.exe", PathUtil.Quote(path)) { UseShellExecute = false })?.Dispose();

        var folderName = IOPath.GetFileName(path.TrimEnd('\\', '/'));
        var query = new WindowQuery("explorer", string.IsNullOrEmpty(folderName) ? null : folderName);
        var found = await ctx.Engine.TrackWindowAsync(ctx, query, before, Layout, TimeSpan.FromSeconds(10), 300, KeepOpen);
        return StepResult.Success(found ? "Папка открыта" : "Папка открыта, окно не найдено");
    }
}

// ───────────────────────────── Команда в терминале ─────────────────────────────

public sealed class CommandStep : Step
{
    public string Command { get; set; } = "";
    public string? WorkingDir { get; set; }

    /// <summary>Ждать завершения команды (git pull). false — запустить и оставить работать (npm run dev), убить при завершении трека.</summary>
    public bool Wait { get; set; } = true;

    /// <summary>Показывать окно консоли.</summary>
    public bool ShowWindow { get; set; }

    public int TimeoutSec { get; set; } = 120;

    public override string Describe() => $"Команда: {Command}";

    public override async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Command))
            return StepResult.Fail("Команда пустая");

        // /s — cmd снимет внешние кавычки и выполнит строку как есть; chcp 65001 — вывод в UTF-8.
        var keep = !Wait && ShowWindow ? "/k" : "/c";
        var psi = new ProcessStartInfo("cmd.exe", $"/d /s {keep} \"chcp 65001>nul & {Command}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = !ShowWindow,
        };
        var workingDir = PathUtil.Expand(WorkingDir);
        if (workingDir.Length > 0)
        {
            if (!Directory.Exists(workingDir))
                return StepResult.Fail($"Рабочая папка не найдена: {workingDir}");
            psi.WorkingDirectory = workingDir;
        }

        if (!Wait)
        {
            var started = Process.Start(psi);
            if (started is null)
                return StepResult.Fail("Процесс не запустился");
            ctx.Session.AddProcess(started);
            started.Dispose();
            return StepResult.Success("Запущено в фоне, остановится при завершении трека");
        }

        var result = await ProcessRunner.RunAsync(psi, stdin: null, TimeSpan.FromSeconds(Math.Max(1, TimeoutSec)), ct, capture: !ShowWindow);
        if (result.TimedOut)
            return StepResult.Fail($"Команда не уложилась в {TimeoutSec} с и была остановлена");

        var output = result.LastLines(3);
        if (output.Length > 0)
            ctx.Log(output);
        return result.ExitCode == 0
            ? StepResult.Success(output.Length > 0 ? output : "Готово")
            : StepResult.Fail($"Код выхода {result.ExitCode}. {result.LastErrorLines(3)}".Trim());
    }
}

// ───────────────────────────── Python-скрипт ─────────────────────────────

public sealed class PythonStep : Step
{
    /// <summary>Имя скрипта в папке scripts или полный путь.</summary>
    public string Script { get; set; } = "";

    /// <summary>Аргументы командной строки скрипта.</summary>
    public string? Args { get; set; }

    /// <summary>Параметры, которые скрипт получит в stdin как JSON (поле "input").</summary>
    public JsonElement? Input { get; set; }

    public int TimeoutSec { get; set; } = 60;

    public override string Describe() => $"Python: {IOPath.GetFileName(Script)}";

    public override async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        var result = await PythonRunner.RunScriptAsync(ctx, this, ct);
        return result;
    }
}

// ───────────────────────────── Подождать ─────────────────────────────

public sealed class WaitStep : Step
{
    public double Seconds { get; set; } = 5;

    public override string Describe() => $"Подождать {Seconds:0.#} с";

    public override async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(Seconds, 0, 3600)), ct);
        return StepResult.Success();
    }
}

// ───────────────────────────── Уведомление ─────────────────────────────

public sealed class NotifyStep : Step
{
    public string Title { get; set; } = "Лаунчер треков";
    public string Message { get; set; } = "";

    public override string Describe() => $"Уведомление: {Message}";

    public override Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        ctx.Engine.Host.Notify(string.IsNullOrWhiteSpace(Title) ? ctx.Track.Name : Title, Message);
        return Task.FromResult(StepResult.Success());
    }
}

// ───────────────────────────── Таймер ─────────────────────────────

public sealed class TimerStep : Step
{
    public double Minutes { get; set; } = 50;
    public string Message { get; set; } = "Время вышло — сделай перерыв";

    public override string Describe() => $"Таймер на {Minutes:0.#} мин";

    public override Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        if (Minutes <= 0)
            return Task.FromResult(StepResult.Fail("Таймер должен быть больше нуля"));
        ctx.Engine.AddTimer(ctx.Session, DisplayName(), Message, TimeSpan.FromMinutes(Minutes));
        return Task.FromResult(StepResult.Success($"Сработает в {DateTime.Now.AddMinutes(Minutes):HH:mm}"));
    }
}

// ───────────────────────────── Вложенный трек ─────────────────────────────

public sealed class TrackStep : Step
{
    public string TrackId { get; set; } = "";

    public override string Describe() => $"Трек «{TrackId}»";

    public override async Task<StepResult> ExecuteAsync(StepContext ctx, CancellationToken ct)
    {
        if (ctx.Chain.Contains(TrackId))
            return StepResult.Fail($"Трек «{TrackId}» уже выполняется выше по цепочке — это был бы бесконечный цикл");

        var nested = ctx.Engine.Tracks.Get(TrackId);
        if (nested is null)
            return StepResult.Fail($"Трек «{TrackId}» не найден");

        var ok = await ctx.Engine.RunNestedAsync(ctx, nested, ct);
        return ok ? StepResult.Success($"Трек «{nested.Name}» выполнен") : StepResult.Fail($"Трек «{nested.Name}» остановился с ошибкой");
    }
}
