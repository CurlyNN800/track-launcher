using System.IO;
using System.Windows;
using System.Windows.Threading;
using TrackLauncher.App.Services;
using TrackLauncher.Core;
using TrackLauncher.Core.Engine;

namespace TrackLauncher.App;

/// <summary>
/// Точка входа. Следит, чтобы лаунчер был запущен в одном экземпляре,
/// держит значок в трее, горячую клавишу и ядро (TrackEngine).
/// </summary>
public partial class App : Application, ILauncherHost
{
    private const string MutexName = @"Local\TrackLauncher.SingleInstance";
    private const string ShowEventName = @"Local\TrackLauncher.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;
    private volatile bool _exiting;

    private TrackEngine? _engine;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private HotkeyManager? _hotkey;

    public TrackEngine Engine => _engine!;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Лаунчер уже запущен — просим его показать окно и выходим.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { /* старый экземпляр мог как раз закрываться */ }
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("Необработанная ошибка в задаче", args.Exception);
            args.SetObserved();
        };

        ListenForShowSignal();

        try
        {
            _engine = new TrackEngine(this, Path.Combine(AppContext.BaseDirectory, "samples"));
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось запустить ядро", ex);
            MessageBox.Show($"Лаунчер не смог запуститься:\n{ex.Message}", "Лаунчер треков", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _window = new MainWindow(_engine, this);
        _tray = new TrayIcon(ShowMainWindow, () => Engine.Active is not null, EndTrackFromTray, ExitApp);

        _hotkey = new HotkeyManager(_window, ShowMainWindow);
        ApplySettings();

        bool minimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        if (!minimized)
            ShowMainWindow();

        UpdateTrayTooltip();
        Logger.Info("Лаунчер запущен");
    }

    /// <summary>Горячая клавиша и автозапуск — вызывается при старте и после сохранения настроек.</summary>
    public string? ApplySettings()
    {
        var settings = Engine.Settings;
        string? warning = null;

        if (_hotkey is not null && !_hotkey.Register(settings.Hotkey) && !string.IsNullOrWhiteSpace(settings.Hotkey))
            warning = $"Горячая клавиша {settings.Hotkey} занята другой программой или записана неправильно";

        try
        {
            Autostart.Apply(settings.Autostart, settings.ShowOnLogin);
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось настроить автозапуск", ex);
            warning = "Не удалось настроить автозапуск: " + ex.Message;
        }

        if (warning is not null)
            Logger.Warn(warning);
        return warning;
    }

    public void ShowMainWindow()
    {
        if (_window is null)
            return;
        _window.ShowAndActivate();
    }

    public void HideMainWindow() => _window?.Hide();

    public void ExitApp()
    {
        _exiting = true;
        _window?.AllowClose();
        _window?.Close();
        Shutdown();
    }

    private async void EndTrackFromTray()
    {
        try
        {
            await Engine.EndAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось завершить трек", ex);
            Notify("Лаунчер треков", ex.Message);
        }
    }

    private void UpdateTrayTooltip()
    {
        var active = _engine?.Active;
        _tray?.SetTooltip(active is null ? "Лаунчер треков" : $"Лаунчер треков — {active.TrackName}");
    }

    // ───────────── ILauncherHost: ядро зовёт это из любого потока ─────────────

    public void Notify(string title, string message) =>
        Dispatcher.InvokeAsync(() => _tray?.ShowBalloon(title, message));

    public void Event(string name, object? data) =>
        Dispatcher.InvokeAsync(() =>
        {
            if (name is "trackStarted" or "trackEnded")
                UpdateTrayTooltip();
            _window?.PostEvent(name, data);
        });

    // ───────────── служебное ─────────────

    private void ListenForShowSignal()
    {
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (!_exiting)
            {
                if (_showSignal.WaitOne(TimeSpan.FromSeconds(1)) && !_exiting)
                    Dispatcher.InvokeAsync(ShowMainWindow);
            }
        })
        {
            IsBackground = true,
            Name = "ShowSignalListener",
        };
        thread.Start();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Необработанная ошибка", e.Exception);
        e.Handled = true;
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _exiting = true;
        _hotkey?.Dispose();
        _tray?.Dispose();
        _engine?.Dispose();
        _showSignal?.Dispose();
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch { /* не владеем — не страшно */ }
            _mutex.Dispose();
        }
    }
}
