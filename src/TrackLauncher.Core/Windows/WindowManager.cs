using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TrackLauncher.Core.Models;
using static TrackLauncher.Core.Windows.NativeMethods;

namespace TrackLauncher.Core.Windows;

public sealed record WindowInfo(IntPtr Handle, int ProcessId, string ProcessName, string Title);

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

public sealed record MonitorInfo(int Index, string Device, bool IsPrimary, PixelRect Bounds, PixelRect WorkArea);

/// <summary>Что считать «нашим» окном: имя процесса и (необязательно) часть заголовка.</summary>
public sealed record WindowQuery(string Process, string? Title)
{
    public bool Matches(WindowInfo w) =>
        w.ProcessName.Equals(Process, StringComparison.OrdinalIgnoreCase)
        && (string.IsNullOrWhiteSpace(Title) || w.Title.Contains(Title, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Работа с окнами Windows: найти, расставить, закрыть.
/// Все координаты — в физических пикселях (приложение PerMonitorV2 DPI aware).
/// </summary>
public static class WindowManager
{
    /// <summary>Все «нормальные» окна верхнего уровня: видимые, с заголовком, не служебные.</summary>
    public static List<WindowInfo> GetTopLevelWindows()
    {
        var result = new List<WindowInfo>();
        var names = new Dictionary<uint, string>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsCandidate(hWnd))
                return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (!names.TryGetValue(pid, out var name))
            {
                name = GetProcessName((int)pid);
                names[pid] = name;
            }

            result.Add(new WindowInfo(hWnd, (int)pid, name, GetTitle(hWnd)));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    public static HashSet<IntPtr> SnapshotHandles() => GetTopLevelWindows().Select(w => w.Handle).ToHashSet();

    public static string GetTitle(IntPtr hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length <= 0)
            return "";
        var sb = new StringBuilder(length + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool IsAlive(IntPtr hWnd) => hWnd != IntPtr.Zero && IsWindow(hWnd);

    /// <summary>Окно живо и всё ещё принадлежит тому же процессу (хэндлы могут переиспользоваться).</summary>
    public static bool IsSameWindow(IntPtr hWnd, int processId)
    {
        if (!IsAlive(hWnd))
            return false;
        GetWindowThreadProcessId(hWnd, out uint pid);
        return pid == (uint)processId;
    }

    public static void Close(IntPtr hWnd) => PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Мониторы: сначала основной, потом слева направо.</summary>
    public static List<MonitorInfo> GetMonitors()
    {
        var raw = new List<(string Device, bool Primary, PixelRect Bounds, PixelRect Work)>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMonitor, ref info))
                raw.Add((info.szDevice, (info.dwFlags & MONITORINFOF_PRIMARY) != 0, ToRect(info.rcMonitor), ToRect(info.rcWork)));
            return true;
        }, IntPtr.Zero);

        return raw
            .OrderByDescending(m => m.Primary)
            .ThenBy(m => m.Bounds.X)
            .ThenBy(m => m.Bounds.Y)
            .Select((m, i) => new MonitorInfo(i, m.Device, m.Primary, m.Bounds, m.Work))
            .ToList();
    }

    /// <summary>Ставит окно по раскладке. Возвращает false, если не получилось (например, окно запущено от администратора).</summary>
    public static bool ApplyLayout(IntPtr hWnd, Layout layout)
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0 || !IsAlive(hWnd))
            return false;

        var monitor = layout.Monitor >= 0 && layout.Monitor < monitors.Count ? monitors[layout.Monitor] : monitors[0];
        var wa = monitor.WorkArea;

        if (IsIconic(hWnd) || IsZoomed(hWnd))
            ShowWindow(hWnd, SW_RESTORE);

        if (layout.Position == "maximized")
        {
            // Сначала переносим окно на нужный монитор, потом разворачиваем — иначе развернётся на старом.
            SetWindowPos(hWnd, IntPtr.Zero, wa.X + 40, wa.Y + 40, Math.Min(1200, wa.Width - 80), Math.Min(800, wa.Height - 80),
                SWP_NOZORDER | SWP_NOACTIVATE);
            return ShowWindow(hWnd, SW_MAXIMIZE) || IsZoomed(hWnd);
        }

        var target = CalculateTarget(layout, wa);

        // При разном DPI мониторов окно после первого переноса меняет размер само — поэтому ставим дважды.
        bool ok = false;
        for (int i = 0; i < 2; i++)
        {
            var adjusted = CompensateInvisibleBorders(hWnd, target);
            ok = SetWindowPos(hWnd, IntPtr.Zero, adjusted.X, adjusted.Y, adjusted.Width, adjusted.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        }
        return ok;
    }

    /// <summary>Считывает, где сейчас стоит окно, в виде раскладки (для «Записать раскладку»).</summary>
    public static Layout? CaptureLayout(IntPtr hWnd)
    {
        if (!IsAlive(hWnd))
            return null;

        var monitors = GetMonitors();
        var hMon = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        GetMonitorInfo(hMon, ref info);
        var monitor = monitors.FirstOrDefault(m => m.Device == info.szDevice) ?? monitors.FirstOrDefault();
        if (monitor is null)
            return null;

        if (IsZoomed(hWnd))
            return new Layout { Monitor = monitor.Index, Position = "maximized" };

        var visible = GetVisibleBounds(hWnd);
        var wa = monitor.WorkArea;
        return new Layout
        {
            Monitor = monitor.Index,
            Position = "custom",
            X = visible.X - wa.X,
            Y = visible.Y - wa.Y,
            Width = visible.Width,
            Height = visible.Height,
        };
    }

    internal static PixelRect CalculateTarget(Layout layout, PixelRect wa)
    {
        int halfW = wa.Width / 2, halfH = wa.Height / 2;
        return layout.Position switch
        {
            "left-half" => new(wa.X, wa.Y, halfW, wa.Height),
            "right-half" => new(wa.X + halfW, wa.Y, wa.Width - halfW, wa.Height),
            "top-half" => new(wa.X, wa.Y, wa.Width, halfH),
            "bottom-half" => new(wa.X, wa.Y + halfH, wa.Width, wa.Height - halfH),
            "top-left" => new(wa.X, wa.Y, halfW, halfH),
            "top-right" => new(wa.X + halfW, wa.Y, wa.Width - halfW, halfH),
            "bottom-left" => new(wa.X, wa.Y + halfH, halfW, wa.Height - halfH),
            "bottom-right" => new(wa.X + halfW, wa.Y + halfH, wa.Width - halfW, wa.Height - halfH),
            "center" => new(wa.X + wa.Width * 15 / 100, wa.Y + wa.Height * 10 / 100, wa.Width * 70 / 100, wa.Height * 80 / 100),
            "custom" => new(
                wa.X + (layout.X ?? 0),
                wa.Y + (layout.Y ?? 0),
                Math.Max(200, layout.Width ?? wa.Width / 2),
                Math.Max(150, layout.Height ?? wa.Height)),
            _ => new(wa.X, wa.Y, wa.Width, wa.Height),
        };
    }

    /// <summary>
    /// В Windows 10/11 у окон есть невидимая рамка ~7 px для изменения размера.
    /// Без поправки «левая половина» оставляет щели. Расширяем прямоугольник на размер этой рамки.
    /// </summary>
    private static PixelRect CompensateInvisibleBorders(IntPtr hWnd, PixelRect target)
    {
        if (!GetWindowRect(hWnd, out var window))
            return target;
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, Marshal.SizeOf<RECT>()) != 0)
            return target;

        int left = frame.Left - window.Left;
        int top = frame.Top - window.Top;
        int right = window.Right - frame.Right;
        int bottom = window.Bottom - frame.Bottom;
        return new PixelRect(target.X - left, target.Y - top, target.Width + left + right, target.Height + top + bottom);
    }

    private static PixelRect GetVisibleBounds(IntPtr hWnd)
    {
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var frame, Marshal.SizeOf<RECT>()) == 0)
            return ToRect(frame);
        GetWindowRect(hWnd, out var window);
        return ToRect(window);
    }

    private static bool IsCandidate(IntPtr hWnd)
    {
        if (!IsWindowVisible(hWnd) || GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
            return false;

        long exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        if ((exStyle & WS_EX_TOOLWINDOW) != 0 || (exStyle & WS_EX_NOACTIVATE) != 0)
            return false;

        if (GetWindowTextLength(hWnd) == 0)
            return false;

        // «Спрятанные» окна UWP-приложений видимы формально, но не на экране.
        if (DwmGetWindowAttributeInt(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;

        return true;
    }

    private static string GetProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    private static PixelRect ToRect(RECT r) => new(r.Left, r.Top, r.Width, r.Height);
}
