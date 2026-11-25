using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TrackLauncher.Core.Engine;
using TrackLauncher.Core.Models;

namespace TrackLauncher.Core.Python;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
{
    public string LastLines(int count) => Tail(StdOut, count);
    public string LastErrorLines(int count) => Tail(StdErr, count);

    private static string Tail(string text, int count) =>
        string.Join(" | ", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).TakeLast(count));
}

/// <summary>Запуск внешнего процесса с тайм-аутом, чтением вывода и (необязательно) данными в stdin.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(ProcessStartInfo psi, string? stdin, TimeSpan timeout, CancellationToken ct, bool capture = true)
    {
        psi.UseShellExecute = false;
        if (capture)
        {
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
        }
        if (stdin is not null)
        {
            psi.RedirectStandardInput = true;
            psi.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        using var process = Process.Start(psi) ?? throw new LauncherException($"Не удалось запустить {psi.FileName}");

        var stdoutTask = capture ? process.StandardOutput.ReadToEndAsync(CancellationToken.None) : Task.FromResult("");
        var stderrTask = capture ? process.StandardError.ReadToEndAsync(CancellationToken.None) : Task.FromResult("");

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); } catch { /* уже завершился */ }
            if (!timedOut)
                throw;
        }

        return new ProcessResult(timedOut ? -1 : process.ExitCode, await stdoutTask, await stderrTask, timedOut);
    }
}

/// <summary>
/// Запуск Python-скриптов для шагов трека.
/// Протокол: лаунчер пишет в stdin JSON { track, trackName, phase, input },
/// скрипт печатает последней строкой JSON { "ok": true/false, "message": "..." }.
/// </summary>
public static class PythonRunner
{
    public static string ResolveScript(string script, string scriptsDir)
    {
        var path = PathUtil.Expand(script);
        return Path.IsPathRooted(path) ? path : Path.Combine(scriptsDir, path);
    }

    public static async Task<StepResult> RunScriptAsync(StepContext ctx, PythonStep step, CancellationToken ct)
    {
        var script = ResolveScript(step.Script, AppPaths.Scripts);
        if (!File.Exists(script))
            return StepResult.Fail($"Скрипт не найден: {script}");

        var psi = CreateStartInfo(ctx.Engine.Settings.PythonPath, $"{PathUtil.Quote(script)} {PathUtil.ExpandArgs(step.Args)}".Trim());
        psi.WorkingDirectory = Path.GetDirectoryName(script)!;

        var payload = JsonSerializer.Serialize(new
        {
            track = ctx.Track.Id,
            trackName = ctx.Track.Name,
            phase = ctx.IsEndPhase ? "end" : "start",
            scriptsDir = AppPaths.Scripts,
            input = step.Input,
        }, Json.Options);

        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(psi, payload, TimeSpan.FromSeconds(Math.Max(1, step.TimeoutSec)), ct);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return StepResult.Fail($"Python не найден («{ctx.Engine.Settings.PythonPath}»). Укажи путь в настройках.");
        }

        if (result.TimedOut)
            return StepResult.Fail($"Скрипт работал дольше {step.TimeoutSec} с и был остановлен");

        if (result.StdErr.Length > 0)
            ctx.Log($"stderr: {result.LastErrorLines(5)}");

        var answer = ParseAnswer(result.StdOut);
        if (answer is not null)
            return new StepResult(answer.Value.Ok && result.ExitCode == 0, answer.Value.Message);

        return result.ExitCode == 0
            ? StepResult.Success(result.LastLines(1))
            : StepResult.Fail($"Скрипт завершился с кодом {result.ExitCode}. {result.LastErrorLines(2)}".Trim());
    }

    /// <summary>«python --version» — для кнопки «Проверить» в настройках.</summary>
    public static async Task<string> GetVersionAsync(string pythonPath)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(CreateStartInfo(pythonPath, "--version"), null, TimeSpan.FromSeconds(10), CancellationToken.None);
            var text = (result.StdOut + result.StdErr).Trim();
            if (result.ExitCode != 0 || !text.StartsWith("Python", StringComparison.OrdinalIgnoreCase))
                throw new LauncherException($"«{pythonPath}» ответил не как Python: {text}");
            return text;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new LauncherException($"Не нашёл «{pythonPath}». Установи Python с python.org или укажи полный путь к python.exe.");
        }
    }

    private static ProcessStartInfo CreateStartInfo(string pythonPath, string arguments)
    {
        var psi = new ProcessStartInfo(PathUtil.Expand(string.IsNullOrWhiteSpace(pythonPath) ? "python" : pythonPath), arguments)
        {
            CreateNoWindow = true,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        return psi;
    }

    private static (bool Ok, string? Message)? ParseAnswer(string stdout)
    {
        var last = stdout.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
        if (last is null || !last.StartsWith('{'))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(last);
            var root = doc.RootElement;
            bool ok = !root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.False;
            string? message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            return (ok, message);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
