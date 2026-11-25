using System.IO;
using Forms = System.Windows.Forms;

namespace TrackLauncher.App.Services;

/// <summary>Значок в трее: открыть окно, завершить трек, выйти. Через него же показываются уведомления.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _endItem;

    public TrayIcon(Action show, Func<bool> hasActiveTrack, Action endTrack, Action exit)
    {
        _endItem = new Forms.ToolStripMenuItem("Завершить трек", null, (_, _) => endTrack());

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("Открыть лаунчер", null, (_, _) => show()) { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(_endItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Выход", null, (_, _) => exit()));
        menu.Opening += (_, _) => _endItem.Enabled = hasActiveTrack();

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Лаунчер треков",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                show();
        };
        _icon.BalloonTipClicked += (_, _) => show();
    }

    public void ShowBalloon(string title, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            message = " ";
        _icon.ShowBalloonTip(5000, title, message, Forms.ToolTipIcon.Info);
    }

    /// <summary>Подсказка при наведении (Windows ограничивает её 127 символами).</summary>
    public void SetTooltip(string text) => _icon.Text = text.Length > 127 ? text[..127] : text;

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "TrackLauncher.exe");
            if (File.Exists(path))
                return System.Drawing.Icon.ExtractAssociatedIcon(path) ?? System.Drawing.SystemIcons.Application;
        }
        catch
        {
            // ниже — значок по умолчанию
        }
        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
