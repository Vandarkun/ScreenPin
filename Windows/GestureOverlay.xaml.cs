using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ScreenPin.Core;
using ScreenPin.Native;

namespace ScreenPin.Windows;

/// <summary>
/// 手势轨迹层。每个手势使用全新实例，结束时销毁——新窗口没有旧画面缓存，
/// 从根本上避免“Show 时闪现上一次轨迹”的 DWM 行为。
/// 坐标策略：Canvas 直接使用物理像素坐标，整体套 1/scale 的缩放映射到 WPF DIP——
/// 无论哪台显示器的 DPI 是多少，轨迹和鼠标都严格对齐。
/// </summary>
public partial class GestureOverlay : Window
{
    private readonly ScaleTransform _physicalToDip = new(1, 1);
    private nint _hwnd;

    public GestureOverlay()
    {
        InitializeComponent();
        Root.LayoutTransform = _physicalToDip;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // 鼠标穿透（不挡住下层窗口的悬停等消息）+ 不抢焦点 + 不进 Alt+Tab
        int ex = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
        _ = Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE,
            ex | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
    }

    /// <summary>每次显示时重新对准虚拟桌面（显示器可能在运行中被插拔）并校准 DPI。</summary>
    private void SyncToVirtualScreen()
    {
        int x = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
        int y = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
        int w = Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN);
        int h = Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN);
        _ = Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, w, h,
            Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);

        double scale = Win32.GetDpiForWindow(_hwnd) / 96.0;
        _physicalToDip.ScaleX = 1.0 / scale;
        _physicalToDip.ScaleY = 1.0 / scale;
        Root.Width = w;
        Root.Height = h;
    }

    /// <summary>提前创建 HWND（触发穿透/不抢焦点样式就位），手势触发时只剩定位+显示。</summary>
    public void Prewarm()
    {
        if (_hwnd == nint.Zero)
        {
            _ = new WindowInteropHelper(this).EnsureHandle();
        }
    }

    public void ShowForGesture()
    {
        Prewarm(); // 备用窗口已提前建好 HWND；防御性保留
        SyncToVirtualScreen();
        Show();
        UpdateLayout();
    }

    public void UpdateTrail(GPoint[] points)
    {
        if (Visibility != Visibility.Visible)
        {
            return;
        }
        var pc = new PointCollection(points.Length);
        foreach (var p in points)
        {
            pc.Add(new Point(p.X, p.Y));
        }
        Trail.Points = pc;
        TrailGlow.Points = pc;
    }

    /// <summary>松开右键：有效则立即隐藏（截图前必须撤掉），无效则快速淡出；随后窗口销毁。</summary>
    public void FinishGesture(bool accepted)
    {
        if (Visibility != Visibility.Visible || !IsLoaded)
        {
            if (IsLoaded) Close();
            return;
        }
        if (accepted)
        {
            Hide();
            Close();
            return;
        }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (!IsLoaded) return; // 淡出途中窗口可能已被清场销毁
            Hide();
            Close();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>外部取消（如托盘暂停）：立即消失并销毁。</summary>
    public void CancelGesture()
    {
        if (!IsLoaded)
        {
            return;
        }
        BeginAnimation(OpacityProperty, null);
        if (Visibility == Visibility.Visible)
        {
            Hide();
        }
        Close();
    }
}
