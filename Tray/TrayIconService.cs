using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ScreenPin.Native;

namespace ScreenPin.Tray;

/// <summary>
/// 系统托盘（WPF 没有内置托盘控件，用 WinForms 的 NotifyIcon）。
/// 菜单：暂停/启用（同一项切换文字）+ 退出。双击图标也可切换。
/// 图标用 GDI+ 现画一个图钉，免去随包携带 .ico 资源。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly nint _hicon;

    public TrayIconService(Action onToggle, Action onExit, Action onSettings, Action onUnpinAll)
    {
        var settingsItem = new ToolStripMenuItem("设置...");
        settingsItem.Click += (_, _) => onSettings();

        var unpinAllItem = new ToolStripMenuItem("取消全部窗口置顶");
        unpinAllItem.Click += (_, _) => onUnpinAll();

        _toggleItem = new ToolStripMenuItem("暂停 ScreenPin");
        _toggleItem.Click += (_, _) => onToggle();

        var exitItem = new ToolStripMenuItem("退出 ScreenPin");
        exitItem.Click += (_, _) => onExit();

        _menu = new ContextMenuStrip();
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(unpinAllItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_toggleItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _hicon = CreateIconHandle();
        _icon = new NotifyIcon
        {
            Text = "ScreenPin",
            Icon = Icon.FromHandle(_hicon),
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => onToggle();
    }

    /// <summary>刷新菜单文字以反映启用状态（回调可能来自非 UI 线程）。</summary>
    public void SetEnabledState(bool enabled)
    {
        Action update = () => _toggleItem.Text = enabled ? "暂停 ScreenPin" : "启用 ScreenPin";
        if (_menu.InvokeRequired)
        {
            _menu.BeginInvoke(update);
        }
        else
        {
            update();
        }
    }

    private static nint CreateIconHandle()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bg = RoundedRect(2, 2, 28, 28, 7))
            using (var brush = new SolidBrush(Color.FromArgb(0x3D, 0x5A, 0xFE)))
            {
                g.FillPath(brush, bg);
            }
            using var white = new SolidBrush(Color.White);
            // 图钉：圆头 + 针脚
            g.FillEllipse(white, 11, 7, 10, 10);
            g.FillPolygon(white, new[]
            {
                new Point(16, 16),
                new Point(21, 27),
                new Point(16, 23),
                new Point(11, 27),
            });
        }
        return bmp.GetHicon();
    }

    private static GraphicsPath RoundedRect(int x, int y, int w, int h, int r)
    {
        var p = new GraphicsPath();
        p.AddArc(x, y, 2 * r, 2 * r, 180, 90);
        p.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
        p.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
        p.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
        p.CloseFigure();
        return p;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        if (_hicon != nint.Zero)
        {
            _ = Win32.DestroyIcon(_hicon);
        }
    }
}
