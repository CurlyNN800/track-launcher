using System.Diagnostics;
using System.Text.Json.Serialization;

namespace TrackLauncher.Core.Engine;

public sealed class TrackedWindow
{
    public long Handle { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>Какой трек и какой шаг открыли окно (нужно для «Записать раскладку»).</summary>
    public string TrackId { get; set; } = "";
    public int StepIndex { get; set; } = -1;

    public bool KeepOpen { get; set; }
}

public sealed class TrackedProcess
{
    public int ProcessId { get; set; }

    /// <summary>Время старта процесса — чтобы не убить чужой процесс с тем же PID после перезагрузки.</summary>
    public DateTime StartTimeUtc { get; set; }
}

public sealed class TrackTimer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime DueAtUtc { get; set; }
}

public sealed class StepProgress
{
    public int Index { get; set; }
    public string Label { get; set; } = "";

    /// <summary>pending, running, done, failed, skipped</summary>
    public string Status { get; set; } = "pending";
    public string? Message { get; set; }
}

/// <summary>
/// Запущенный трек: что он открыл, когда начался, какие таймеры тикают.
/// Сохраняется в session.json, чтобы «Завершить трек» работал даже после перезапуска лаунчера.
/// </summary>
public sealed class TrackSession
{
    private readonly object _sync = new();
    private readonly HashSet<long> _claimed = [];

    public string TrackId { get; set; } = "";
    public string TrackName { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public long StatsId { get; set; }

    public List<TrackedWindow> Windows { get; set; } = [];
    public List<TrackedProcess> Processes { get; set; } = [];
    public List<TrackTimer> Timers { get; set; } = [];

    // ── то, что живёт только в памяти ──

    [JsonIgnore] public CancellationTokenSource Cancellation { get; set; } = new();
    [JsonIgnore] public bool Running { get; set; }
    [JsonIgnore] public int PendingWindows;
    [JsonIgnore] public List<StepProgress> Progress { get; } = [];

    /// <summary>Временная сессия для «Проверить шаг» — не сохраняется и не попадает в статистику.</summary>
    [JsonIgnore] public bool IsTest { get; init; }

    /// <summary>Занимает окно, чтобы два шага (например, два окна Chrome) не схватили одно и то же.</summary>
    public bool TryClaim(IntPtr handle)
    {
        lock (_sync)
        {
            if (Windows.Any(w => w.Handle == handle.ToInt64()))
                return false;
            return _claimed.Add(handle.ToInt64());
        }
    }

    public void Release(IntPtr handle)
    {
        lock (_sync)
            _claimed.Remove(handle.ToInt64());
    }

    public void AddWindow(TrackedWindow window)
    {
        lock (_sync)
        {
            _claimed.Remove(window.Handle);
            Windows.Add(window);
        }
    }

    public void AddProcess(Process process)
    {
        DateTime start;
        try { start = process.StartTime.ToUniversalTime(); }
        catch { start = DateTime.UtcNow; }

        lock (_sync)
            Processes.Add(new TrackedProcess { ProcessId = process.Id, StartTimeUtc = start });
    }

    public void AddTimer(TrackTimer timer)
    {
        lock (_sync)
            Timers.Add(timer);
    }

    /// <summary>Забирает таймеры, которым пора сработать.</summary>
    public List<TrackTimer> TakeDueTimers(DateTime nowUtc)
    {
        lock (_sync)
        {
            var due = Timers.Where(t => t.DueAtUtc <= nowUtc).ToList();
            Timers.RemoveAll(t => t.DueAtUtc <= nowUtc);
            return due;
        }
    }

    public void SetProgress(int index, string label, string status, string? message)
    {
        lock (_sync)
        {
            var item = Progress.FirstOrDefault(p => p.Index == index);
            if (item is null)
            {
                item = new StepProgress { Index = index };
                Progress.Add(item);
            }
            item.Label = label;
            item.Status = status;
            item.Message = message;
        }
    }

    public object ToStatus()
    {
        lock (_sync)
        {
            return new
            {
                active = true,
                trackId = TrackId,
                trackName = TrackName,
                startedAt = StartedAtUtc,
                running = Running,
                pendingWindows = PendingWindows,
                windows = Windows.Count,
                processes = Processes.Count,
                timers = Timers.OrderBy(t => t.DueAtUtc).Select(t => new { t.Id, t.Label, t.Message, dueAt = t.DueAtUtc }).ToList(),
                progress = Progress.OrderBy(p => p.Index).Select(p => new { p.Index, p.Label, p.Status, p.Message }).ToList(),
            };
        }
    }

    /// <summary>Копия списков под замком — чтобы безопасно сохранить в файл.</summary>
    public TrackSession Snapshot()
    {
        lock (_sync)
        {
            return new TrackSession
            {
                TrackId = TrackId,
                TrackName = TrackName,
                StartedAtUtc = StartedAtUtc,
                LastSeenUtc = LastSeenUtc,
                StatsId = StatsId,
                Windows = [.. Windows],
                Processes = [.. Processes],
                Timers = [.. Timers],
            };
        }
    }
}
