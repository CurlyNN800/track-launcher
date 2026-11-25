using System.Diagnostics;
using TrackLauncher.Core.Models;
using TrackLauncher.Core.Stats;
using TrackLauncher.Core.Windows;

namespace TrackLauncher.Core.Engine;

/// <summary>
/// Сердце лаунчера: запускает и завершает треки, следит за окнами и таймерами, пишет статистику.
/// Одновременно может идти только один трек.
/// </summary>
public sealed class TrackEngine : IDisposable
{
    private static readonly TimeSpan HeartbeatEvery = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ExistingWindowGrace = TimeSpan.FromSeconds(6);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _saveLock = new();
    private readonly Timer _tick;
    private DateTime _lastHeartbeat = DateTime.MinValue;

    public TrackEngine(ILauncherHost host, string? samplesDir)
    {
        Host = host;
        AppPaths.EnsureCreated(samplesDir);
        SettingsStore.Load();
        Tracks = new TrackRepository(AppPaths.Tracks);
        Stats = new StatsStore(AppPaths.StatsDb);
        RestoreSession();
        _tick = new Timer(_ => OnTick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public ILauncherHost Host { get; }
    public TrackRepository Tracks { get; }
    public StatsStore Stats { get; }
    public SettingsStore SettingsStore { get; } = new();
    public Settings Settings => SettingsStore.Current;

    public TrackSession? Active { get; private set; }

    public object GetStatus() => Active?.ToStatus() ?? new { active = false };

    // ───────────────────────────── запуск ─────────────────────────────

    public async Task StartAsync(string trackId)
    {
        Track track;
        TrackSession session;

        await _gate.WaitAsync();
        try
        {
            if (Active is not null)
                throw new LauncherException($"Сейчас идёт трек «{Active.TrackName}». Сначала заверши его.");

            track = Tracks.Get(trackId) ?? throw new LauncherException($"Трек «{trackId}» не найден");
            var now = DateTime.UtcNow;
            session = new TrackSession
            {
                TrackId = track.Id,
                TrackName = track.Name,
                StartedAtUtc = now,
                LastSeenUtc = now,
            };
            session.StatsId = Stats.BeginSession(track.Id, track.Name, now);
            for (int i = 0; i < track.Steps.Count; i++)
                session.SetProgress(i, track.Steps[i].DisplayName(), track.Steps[i].Enabled ? "pending" : "skipped", null);

            Active = session;
            session.Running = true;
            SaveSession();
        }
        finally
        {
            _gate.Release();
        }

        Logger.Info($"Старт трека «{track.Name}»");
        Host.Event("trackStarted", new { trackId = track.Id, trackName = track.Name });
        RaiseStatus();

        // Шаги выполняются в фоне, чтобы интерфейс сразу получил ответ, а «Завершить» можно было нажать в любой момент.
        _ = RunMainAsync(session, track);
    }

    private async Task RunMainAsync(TrackSession session, Track track)
    {
        try
        {
            await RunStepsAsync(session, track, track.Steps, new HashSet<string> { track.Id }, isEndPhase: false,
                reportProgress: true, session.Cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Трек завершили раньше, чем он успел запуститься целиком.
        }
        catch (Exception ex)
        {
            Logger.Error($"Трек «{track.Name}» упал", ex);
        }
        finally
        {
            session.Running = false;
            RaiseStatus();
            if (ReferenceEquals(Active, session))
                Host.Event("trackReady", new { trackId = track.Id });
        }
    }

    /// <summary>Выполняет шаги по порядку. Возвращает false, если шаг упал и у него ContinueOnError = false.</summary>
    private async Task<bool> RunStepsAsync(TrackSession session, Track track, IList<Step> steps, IReadOnlySet<string> chain,
        bool isEndPhase, bool reportProgress, CancellationToken ct)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = steps[i];
            if (!step.Enabled)
                continue;

            var label = step.DisplayName();
            if (reportProgress)
                ReportProgress(session, i, label, "running", null);

            var ctx = new StepContext
            {
                Engine = this,
                Session = session,
                Track = track,
                StepIndex = i,
                IsEndPhase = isEndPhase,
                Chain = chain,
            };

            StepResult result;
            try
            {
                result = await step.ExecuteAsync(ctx, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = StepResult.Fail(ex.Message);
            }

            if (result.Ok)
                ctx.Log($"{label}: {result.Message ?? "ок"}");
            else
                Logger.Warn($"[{track.Id} #{i + 1}] {label}: {result.Message}");

            if (reportProgress)
                ReportProgress(session, i, label, result.Ok ? "done" : "failed", result.Message);

            if (!result.Ok && !step.ContinueOnError)
            {
                Host.Notify(track.Name, $"Трек остановлен на шаге «{label}»: {result.Message}");
                return false;
            }
        }
        return true;
    }

    internal async Task<bool> RunNestedAsync(StepContext parent, Track nested, CancellationToken ct)
    {
        var chain = new HashSet<string>(parent.Chain) { nested.Id };
        return await RunStepsAsync(parent.Session, nested, nested.Steps, chain, parent.IsEndPhase, reportProgress: false, ct);
    }

    private void ReportProgress(TrackSession session, int index, string label, string status, string? message)
    {
        session.SetProgress(index, label, status, message);
        if (!session.IsTest)
            Host.Event("stepProgress", new { trackId = session.TrackId, index, label, status, message });
    }

    // ───────────────────────────── окна ─────────────────────────────

    /// <summary>
    /// Ждёт новое окно, которое подходит под запрос, расставляет его и запоминает в сессии.
    /// Никогда не бросает исключений — его можно запускать «в фоне».
    /// </summary>
    internal async Task<bool> TrackWindowAsync(StepContext ctx, WindowQuery query, HashSet<IntPtr> before, Layout? layout,
        TimeSpan timeout, int settleMs, bool keepOpen)
    {
        var session = ctx.Session;
        var ct = session.Cancellation.Token;
        Interlocked.Increment(ref session.PendingWindows);
        RaiseStatus(session);

        try
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                ct.ThrowIfCancellationRequested();

                var candidate = WindowManager.GetTopLevelWindows()
                    .FirstOrDefault(w => !before.Contains(w.Handle) && query.Matches(w) && session.TryClaim(w.Handle));

                if (candidate is null)
                {
                    // Программа уже была открыта и не создаёт новых окон (Discord, Telegram)? Тогда просто расставим старое.
                    if (sw.Elapsed > ExistingWindowGrace && ArrangeExistingWindow(ctx, query, before, layout))
                        return false;

                    // ConfigureAwait(false): опрос окон идёт в фоновом потоке и не тормозит интерфейс.
                    await Task.Delay(300, ct).ConfigureAwait(false);
                    continue;
                }

                if (settleMs > 0)
                    await Task.Delay(settleMs, ct).ConfigureAwait(false);

                // Окно-заставка могло закрыться, а заголовок — поменяться. Тогда ищем дальше.
                var title = WindowManager.GetTitle(candidate.Handle);
                bool titleOk = string.IsNullOrWhiteSpace(query.Title) || title.Contains(query.Title, StringComparison.OrdinalIgnoreCase);
                if (!WindowManager.IsAlive(candidate.Handle) || !titleOk)
                {
                    session.Release(candidate.Handle);
                    before.Add(candidate.Handle);
                    continue;
                }

                if (layout is not null && !WindowManager.ApplyLayout(candidate.Handle, layout))
                    ctx.Log($"Не удалось расставить окно «{title}». Возможно, программа запущена от администратора.");

                session.AddWindow(new TrackedWindow
                {
                    Handle = candidate.Handle.ToInt64(),
                    ProcessId = candidate.ProcessId,
                    ProcessName = candidate.ProcessName,
                    Title = title,
                    TrackId = ctx.Track.Id,
                    StepIndex = ctx.IsEndPhase ? -1 : ctx.StepIndex,
                    KeepOpen = keepOpen,
                });
                ctx.Log($"Окно найдено: «{title}» ({candidate.ProcessName})");
                if (!session.IsTest)
                    SaveSession();
                return true;
            }

            ctx.Log($"Новое окно {query.Process} не появилось за {timeout.TotalSeconds:0} с");
            ArrangeExistingWindow(ctx, query, before, layout);
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error($"Ошибка при поиске окна {query.Process}", ex);
            return false;
        }
        finally
        {
            Interlocked.Decrement(ref session.PendingWindows);
            RaiseStatus(session);
        }
    }

    /// <summary>Расставляет уже открытое до трека окно. Такие окна не закрываются при завершении.</summary>
    private static bool ArrangeExistingWindow(StepContext ctx, WindowQuery query, HashSet<IntPtr> before, Layout? layout)
    {
        var existing = WindowManager.GetTopLevelWindows().FirstOrDefault(w => before.Contains(w.Handle) && query.Matches(w));
        if (existing is null)
            return false;

        if (layout is not null)
            WindowManager.ApplyLayout(existing.Handle, layout);
        ctx.Log($"{query.Process} уже был открыт — окно расставлено, но закрывать его лаунчер не будет");
        return true;
    }

    // ───────────────────────────── таймеры ─────────────────────────────

    internal void AddTimer(TrackSession session, string label, string message, TimeSpan after)
    {
        session.AddTimer(new TrackTimer { Label = label, Message = message, DueAtUtc = DateTime.UtcNow + after });
        if (!session.IsTest)
            SaveSession();
        RaiseStatus(session);
    }

    private void OnTick()
    {
        var session = Active;
        if (session is null)
            return;

        try
        {
            var now = DateTime.UtcNow;
            var due = session.TakeDueTimers(now);
            foreach (var timer in due)
            {
                Host.Notify(session.TrackName, string.IsNullOrWhiteSpace(timer.Message) ? timer.Label : timer.Message);
                Host.Event("timerFired", new { timer.Id, timer.Label, timer.Message });
            }

            if (now - _lastHeartbeat >= HeartbeatEvery)
            {
                _lastHeartbeat = now;
                session.LastSeenUtc = now;
                Stats.Touch(session.StatsId, now);
            }

            if (due.Count > 0)
            {
                SaveSession();
                RaiseStatus();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Ошибка в фоновом таймере", ex);
        }
    }

    // ───────────────────────────── завершение ─────────────────────────────

    public async Task<object?> EndAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var session = Active;
            if (session is null)
                return null;

            session.Cancellation.Cancel();
            var track = Tracks.Get(session.TrackId);

            if (track is not null && track.OnEnd.Count > 0)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                try
                {
                    await RunStepsAsync(session, track, track.OnEnd, new HashSet<string> { track.Id }, isEndPhase: true,
                        reportProgress: false, timeout.Token);
                }
                catch (Exception ex)
                {
                    Logger.Error("Шаги завершения выполнились не полностью", ex);
                }
            }

            int closed = CloseWindows(session);
            int killed = KillProcesses(session);

            var now = DateTime.UtcNow;
            Stats.EndSession(session.StatsId, now, "user");
            Active = null;
            DeleteSessionFile();

            var minutes = (now - session.StartedAtUtc).TotalMinutes;
            Logger.Info($"Трек «{session.TrackName}» завершён: {minutes:0} мин, закрыто окон: {closed}, остановлено процессов: {killed}");
            var summary = new { trackId = session.TrackId, trackName = session.TrackName, minutes, closed, killed };
            Host.Event("trackEnded", summary);
            RaiseStatus();
            return summary;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static int CloseWindows(TrackSession session)
    {
        int count = 0;
        foreach (var window in session.Windows.Where(w => !w.KeepOpen))
        {
            var handle = new IntPtr(window.Handle);
            if (!WindowManager.IsSameWindow(handle, window.ProcessId))
                continue;
            // WM_CLOSE — как нажать на крестик: программа успеет спросить «Сохранить изменения?».
            WindowManager.Close(handle);
            count++;
        }
        return count;
    }

    private static int KillProcesses(TrackSession session)
    {
        int count = 0;
        foreach (var tracked in session.Processes)
        {
            try
            {
                using var process = Process.GetProcessById(tracked.ProcessId);
                if (Math.Abs((process.StartTime.ToUniversalTime() - tracked.StartTimeUtc).TotalSeconds) > 2)
                    continue; // PID уже занят другим процессом
                process.Kill(entireProcessTree: true);
                count++;
            }
            catch
            {
                // Процесс уже завершился сам.
            }
        }
        return count;
    }

    // ───────────────────────────── «Записать раскладку» и проверка шага ─────────────────────────────

    /// <summary>Записывает текущее положение окон запущенного трека в его шаги.</summary>
    public int CaptureLayout()
    {
        var session = Active ?? throw new LauncherException("Нет запущенного трека");
        var track = Tracks.Get(session.TrackId) ?? throw new LauncherException("Трек не найден");

        int updated = 0;
        foreach (var window in session.Snapshot().Windows.Where(w => w.TrackId == track.Id && w.StepIndex >= 0))
        {
            if (window.StepIndex >= track.Steps.Count || track.Steps[window.StepIndex] is not WindowStep step)
                continue;
            var handle = new IntPtr(window.Handle);
            if (!WindowManager.IsSameWindow(handle, window.ProcessId))
                continue;
            var layout = WindowManager.CaptureLayout(handle);
            if (layout is null)
                continue;
            step.Layout = layout;
            updated++;
        }

        if (updated == 0)
            throw new LauncherException("Не нашлось ни одного открытого окна этого трека");

        Tracks.Save(track);
        Logger.Info($"Раскладка трека «{track.Name}» записана: {updated} окон");
        return updated;
    }

    /// <summary>Выполняет один шаг отдельно от трека — кнопка «Проверить» в конструкторе.</summary>
    public async Task<StepResult> TestStepAsync(Step step, string? trackId)
    {
        var track = (trackId is null ? null : Tracks.Get(trackId)) ?? new Track { Id = "__test", Name = "Проверка шага" };
        var session = new TrackSession { TrackId = track.Id, TrackName = track.Name, IsTest = true };
        var ctx = new StepContext
        {
            Engine = this,
            Session = session,
            Track = track,
            StepIndex = 0,
            IsEndPhase = false,
            Chain = new HashSet<string> { track.Id },
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            return await step.ExecuteAsync(ctx, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return StepResult.Fail("Шаг выполнялся дольше 2 минут и был остановлен");
        }
        catch (Exception ex)
        {
            return StepResult.Fail(ex.Message);
        }
    }

    // ───────────────────────────── сохранение сессии ─────────────────────────────

    private void SaveSession()
    {
        var session = Active;
        if (session is null)
            return;
        try
        {
            lock (_saveLock)
                Json.Write(AppPaths.SessionFile, session.Snapshot());
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось сохранить session.json", ex);
        }
    }

    private static void DeleteSessionFile()
    {
        try { File.Delete(AppPaths.SessionFile); } catch { /* не страшно */ }
    }

    /// <summary>
    /// После перезапуска лаунчера: если окна трека ещё открыты — продолжаем сессию,
    /// если комп выключали — закрываем её в статистике по последней отметке «ещё жив».
    /// </summary>
    private void RestoreSession()
    {
        TrackSession? saved = null;
        try
        {
            if (File.Exists(AppPaths.SessionFile))
                saved = Json.Read<TrackSession>(AppPaths.SessionFile);
        }
        catch (Exception ex)
        {
            Logger.Error("session.json повреждён", ex);
        }

        if (saved is not null)
        {
            bool alive = saved.Windows.Any(w => WindowManager.IsSameWindow(new IntPtr(w.Handle), w.ProcessId));
            if (alive)
            {
                Active = saved;
                Logger.Info($"Продолжаю трек «{saved.TrackName}» после перезапуска лаунчера");
            }
            else
            {
                Stats.EndSession(saved.StatsId, saved.LastSeenUtc, "restart");
                DeleteSessionFile();
                Logger.Info($"Трек «{saved.TrackName}» закрыт по последней отметке: окна уже закрыты");
            }
        }

        Stats.CloseDangling(Active?.StatsId);
    }

    private void RaiseStatus(TrackSession? session = null)
    {
        if (session is { IsTest: true })
            return;
        Host.Event("status", GetStatus());
    }

    public void Dispose()
    {
        _tick.Dispose();
        _gate.Dispose();
    }
}
