using TrackLauncher.Core.Models;

namespace TrackLauncher.Core.Engine;

public sealed record StepResult(bool Ok, string? Message)
{
    public static StepResult Success(string? message = null) => new(true, message);
    public static StepResult Fail(string message) => new(false, message);
}

/// <summary>Всё, что нужно шагу во время выполнения.</summary>
public sealed class StepContext
{
    public required TrackEngine Engine { get; init; }
    public required TrackSession Session { get; init; }

    /// <summary>Трек, которому принадлежит шаг (для вложенных — вложенный трек).</summary>
    public required Track Track { get; init; }

    public required int StepIndex { get; init; }

    /// <summary>true — шаг из раздела «При завершении».</summary>
    public required bool IsEndPhase { get; init; }

    /// <summary>Цепочка запущенных треков — защита от бесконечной вложенности.</summary>
    public required IReadOnlySet<string> Chain { get; init; }

    public void Log(string message) => Logger.Info($"[{Track.Id} #{StepIndex + 1}] {message}");
}

/// <summary>То, что лаунчер умеет делать «снаружи» ядра: уведомления и события для интерфейса.</summary>
public interface ILauncherHost
{
    void Notify(string title, string message);

    /// <summary>Событие для интерфейса. Может вызываться из любого потока.</summary>
    void Event(string name, object? data);
}
