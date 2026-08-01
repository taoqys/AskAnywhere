using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AskAnywhere.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event Action? DoubleClicked;
    public event Action? OpenRequested;
    public event Action? HistoryRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIconService()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = CreateAppIcon(),
            Text = "AskAnywhere - AI 助手",
            Visible = false
        };

        var menu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem("打开 AskAnywhere");
        openItem.Click += (_, _) => OpenRequested?.Invoke();
        menu.Items.Add(openItem);

        var historyItem = new ToolStripMenuItem("历史记录");
        historyItem.Click += (_, _) => HistoryRequested?.Invoke();
        menu.Items.Add(historyItem);

        var settingsItem = new ToolStripMenuItem("设置");
        settingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => DoubleClicked?.Invoke();
    }

    public void Show()
    {
        _notifyIcon.Visible = true;
    }

    private static Icon CreateAppIcon()
    {
        // Prefer the packaged app icon (Assets/app.ico).
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream != null)
            {
                using (stream)
                using (var icon = new Icon(stream))
                {
                    // Clone so the returned icon stays valid after the stream closes.
                    return (Icon)icon.Clone();
                }
            }
        }
        catch
        {
            // Fall back to the generated icon below.
        }

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bg = new SolidBrush(Color.FromArgb(37, 99, 235));
            using var path = RoundedRect(new RectangleF(1, 1, 30, 30), 7);
            g.FillPath(bg, path);

            using var font = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var fg = new SolidBrush(Color.White);
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString("A", font, fg, new RectangleF(1, 0, 30, 30), sf);
            sf.Dispose();
        }

        var hIcon = bmp.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(hIcon);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
