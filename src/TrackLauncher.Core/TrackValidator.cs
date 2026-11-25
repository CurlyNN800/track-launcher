using TrackLauncher.Core.Models;
using TrackLauncher.Core.Windows;

namespace TrackLauncher.Core;

public sealed record ValidationIssue(string Level, string Message, string? Phase = null, int? StepIndex = null);

/// <summary>Проверка трека перед сохранением: ошибки не дают сохранить, предупреждения — просто показываются.</summary>
public static class TrackValidator
{
    public static List<ValidationIssue> Validate(Track track, TrackRepository repository, string scriptsDir)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(track.Name))
            issues.Add(new("error", "У трека нет названия"));
        if (track.Steps.Count == 0)
            issues.Add(new("warning", "В треке нет ни одного шага"));

        CheckSteps(track.Steps, "steps", issues, scriptsDir);
        CheckSteps(track.OnEnd, "onEnd", issues, scriptsDir);
        CheckCycles(track, repository, issues);

        return issues;
    }

    private static void CheckSteps(List<Step> steps, string phase, List<ValidationIssue> issues, string scriptsDir)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            void Error(string m) => issues.Add(new("error", m, phase, i));
            void Warn(string m) => issues.Add(new("warning", m, phase, i));

            switch (steps[i])
            {
                case AppStep app:
                    var exe = PathUtil.Expand(app.Path);
                    if (exe.Length == 0) Error("Не указан путь к программе");
                    else if (Path.IsPathRooted(exe) && !File.Exists(exe)) Warn($"Файл не найден: {exe}");
                    break;
                case UrlStep url:
                    if (url.Urls.All(string.IsNullOrWhiteSpace)) Error("Не указано ни одного сайта");
                    if (!string.IsNullOrWhiteSpace(url.Browser) && Browsers.Resolve(url.Browser) is null)
                        Warn($"Браузер «{url.Browser}» не найден на этом компьютере");
                    break;
                case FolderStep folder:
                    var dir = PathUtil.Expand(folder.Path);
                    if (dir.Length == 0) Error("Не указана папка");
                    else if (!Directory.Exists(dir)) Warn($"Папка не найдена: {dir}");
                    break;
                case CommandStep command:
                    if (string.IsNullOrWhiteSpace(command.Command)) Error("Команда пустая");
                    break;
                case PythonStep python:
                    if (string.IsNullOrWhiteSpace(python.Script)) Error("Не указан скрипт");
                    else if (!File.Exists(Python.PythonRunner.ResolveScript(python.Script, scriptsDir)))
                        Warn($"Скрипт не найден: {python.Script}");
                    break;
                case TimerStep timer:
                    if (timer.Minutes <= 0) Error("Таймер должен быть больше нуля");
                    break;
                case TrackStep nested:
                    if (string.IsNullOrWhiteSpace(nested.TrackId)) Error("Не выбран вложенный трек");
                    break;
            }
        }
    }

    /// <summary>Трек A вызывает B, B вызывает A — такое сохранять нельзя.</summary>
    private static void CheckCycles(Track edited, TrackRepository repository, List<ValidationIssue> issues)
    {
        Track? Load(string id) => id == edited.Id ? edited : repository.Get(id);

        var visiting = new HashSet<string>();
        bool HasCycle(Track track, List<string> path)
        {
            if (!visiting.Add(track.Id))
                return true;
            path.Add(track.Name);
            foreach (var step in track.Steps.Concat(track.OnEnd).OfType<TrackStep>())
            {
                var next = Load(step.TrackId);
                if (next is null)
                {
                    if (ReferenceEquals(track, edited))
                        issues.Add(new("error", $"Вложенный трек «{step.TrackId}» не найден"));
                    continue;
                }
                if (HasCycle(next, path))
                    return true;
            }
            visiting.Remove(track.Id);
            path.RemoveAt(path.Count - 1);
            return false;
        }

        var cyclePath = new List<string>();
        if (string.IsNullOrEmpty(edited.Id))
            edited.Id = "__new";
        try
        {
            if (HasCycle(edited, cyclePath))
                issues.Add(new("error", $"Треки вызывают друг друга по кругу: {string.Join(" → ", cyclePath)} → …"));
        }
        finally
        {
            if (edited.Id == "__new")
                edited.Id = "";
        }
    }
}
