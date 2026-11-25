using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using TrackLauncher.Core;
using TrackLauncher.Core.Engine;

namespace TrackLauncher.App;

/// <summary>
/// Окно лаунчера. Внутри — WebView2, который показывает ui/index.html.
/// Общение с интерфейсом: JS шлёт { id, action, payload }, C# отвечает { id, ok, data | error }.
/// События от ядра уходят в JS как { event, data }.
/// </summary>
public partial class MainWindow : Window
{
    private const string HostName = "launcher.local";

    private readonly TrackEngine _engine;
    private readonly Bridge _bridge;
    private bool _webReady;
    private bool _initStarted;
    private bool _allowClose;

    public MainWindow(TrackEngine engine, App app)
    {
        InitializeComponent();
        _engine = engine;
        _bridge = new Bridge(engine, app, this);

        WebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 15, 17, 23);
        SourceInitialized += (_, _) => UseDarkTitleBar();
        Loaded += async (_, _) => await InitializeWebViewAsync();
        Closing += OnClosing;
    }

    public void AllowClose() => _allowClose = true;

    public void ShowAndActivate()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        // Windows не всегда даёт вывести окно на передний план из фона — небольшой трюк с Topmost.
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        WebView.Focus();
        PostEvent("windowShown", null);
    }

    public void PostEvent(string name, object? data)
    {
        if (!_webReady)
            return;
        try
        {
            var json = JsonSerializer.Serialize(new { @event = name, data }, Json.Options);
            WebView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            Logger.Error($"Не удалось отправить событие {name} в интерфейс", ex);
        }
    }

    private async Task InitializeWebViewAsync()
    {
        if (_initStarted)
            return;
        _initStarted = true;

        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewData);
            await WebView.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            LoadingText.Text = "Не найден WebView2 Runtime. Установи его с сайта Microsoft (Evergreen Runtime) и перезапусти лаунчер.";
            return;
        }
        catch (Exception ex)
        {
            Logger.Error("WebView2 не запустился", ex);
            LoadingText.Text = "Интерфейс не запустился: " + ex.Message;
            return;
        }

        var core = WebView.CoreWebView2;
        var uiFolder = Path.Combine(AppContext.BaseDirectory, "ui");
        core.SetVirtualHostNameToFolderMapping(HostName, uiFolder, CoreWebView2HostResourceAccessKind.Allow);

        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
#if DEBUG
        // В Debug-сборке работают F12 (DevTools) и контекстное меню «Проверить».
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.AreDefaultContextMenusEnabled = true;
#else
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
#endif

        core.WebMessageReceived += OnWebMessageReceived;

        // Все внешние ссылки — в обычный браузер, а не внутрь лаунчера.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };
        core.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Host != HostName)
            {
                e.Cancel = true;
                OpenExternal(e.Uri);
            }
        };
        core.NavigationCompleted += (_, e) =>
        {
            _webReady = e.IsSuccess;
            LoadingText.Visibility = Visibility.Collapsed;
            if (!e.IsSuccess)
                Logger.Error($"Интерфейс не загрузился: {e.WebErrorStatus}");
        };

        core.Navigate($"https://{HostName}/index.html");
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        long id = 0;
        object response;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            id = root.TryGetProperty("id", out var idElement) ? idElement.GetInt64() : 0;
            var action = root.GetProperty("action").GetString() ?? "";
            var payload = root.TryGetProperty("payload", out var p) ? p.Clone() : default;

            var data = await _bridge.HandleAsync(action, payload);
            response = new { id, ok = true, data };
        }
        catch (Exception ex)
        {
            if (ex is not LauncherException)
                Logger.Error("Ошибка при обработке команды интерфейса", ex);
            response = new { id, ok = false, error = ex.Message };
        }

        try
        {
            WebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response, Json.Options));
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось ответить интерфейсу", ex);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && _engine.Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (!_allowClose)
        {
            // Крестик без «сворачивать в трей» — значит, выходим из лаунчера совсем.
            e.Cancel = true;
            ((App)Application.Current).ExitApp();
        }
    }

    internal static void OpenExternal(string uri)
    {
        try
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp))
                Process.Start(new ProcessStartInfo(parsed.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Error("Не удалось открыть ссылку", ex);
        }
    }

    private void UseDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int enabled = 1;
            DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref enabled, sizeof(int));
        }
        catch
        {
            // Старые версии Windows — оставим светлый заголовок.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
