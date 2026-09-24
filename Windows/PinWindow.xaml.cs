using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ScreenPin.Core;
using ScreenPin.Native;

namespace ScreenPin.Windows;

/// <summary>
/// 置顶贴图窗口。左键拖动，右键关闭。
/// 定位策略：物理尺寸 = 截图区域 + 2×视觉外沿（阴影留白 + 描边），用 SetWindowPos 直接
/// 以物理像素放置；内部 Image 用 Stretch=Fill 铺满——不管 DPI 是多少，位图都和屏幕像素对齐。
/// 拖动也全程用物理坐标（GetCursorPos/SetWindowRect），不经过 DIP 换算。
/// 注意：AllowsTransparency 使本窗口成为分层窗口，截图服务已带 CAPTUREBLT，贴图可被再次圈选。
/// </summary>
public partial class PinWindow : Window
{
    /// <summary>视觉外沿（阴影留白 16 + 描边 1，DIP），须与 XAML 的 Margin/BorderThickness 同步。</summary>
    private const double EdgeDip = 16 + 1;

    private readonly CaptureRegion _region;
    private nint _hwnd;
    private bool _dragging;
    private Win32.POINT _dragCursor;
    private Win32.RECT _dragWindow;

    public PinWindow(BitmapSource shot, CaptureRegion region)
    {
        InitializeComponent();
        _region = region;
        Shot.Source = shot;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

        int ex = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
        _ = Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE,
            ex | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);

        double scale = Win32.GetDpiForWindow(_hwnd) / 96.0;
        int edge = (int)Math.Ceiling(EdgeDip * scale);
        // 登记 HWND 和外沿，钩子据此把落在贴图图像区上的右键放行给我们
        PinHitTest.Register(_hwnd, edge);
        _ = Win32.SetWindowPos(_hwnd, nint.Zero,
            _region.X - edge, _region.Y - edge,
            _region.Width + 2 * edge, _region.Height + 2 * edge,
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }
        if (Win32.GetCursorPos(out _dragCursor) &&
            Win32.GetWindowRect(_hwnd, out _dragWindow))
        {
            _dragging = true;
            CaptureMouse();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || !Win32.GetCursorPos(out var c))
        {
            return;
        }
        _ = Win32.SetWindowPos(_hwnd, nint.Zero,
            _dragWindow.Left + (c.X - _dragCursor.X),
            _dragWindow.Top + (c.Y - _dragCursor.Y),
            0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        ReleaseMouseCapture();
    }

    /// <summary>滚轮调整贴图透明度（30%~100%，每格 10%）。只改窗口显示，不动位图。</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        double next = Math.Clamp(Opacity + (e.Delta > 0 ? 0.1 : -0.1), 0.3, 1.0);
        if (Math.Abs(next - Opacity) < 0.001)
        {
            return; // 已到边界
        }
        Opacity = next;
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        PinHitTest.Unregister(_hwnd);
        base.OnClosed(e);
    }
}
