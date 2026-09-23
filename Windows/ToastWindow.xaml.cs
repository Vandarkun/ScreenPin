using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ScreenPin.Core;
using ScreenPin.Native;

namespace ScreenPin.Windows;

/// <summary>
/// 右下角通知（QQ 来消息样式）：从屏幕右缘滑入，停留 2.2 秒后淡出，点击立即关闭。
/// 同一时间只显示一条，新的通知直接替换旧的。
/// </summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(2200) };
    private bool _dismissed;

    public ToastWindow(string title, string? preview, bool success)
    {
        InitializeComponent();
        Mark.Text = success ? "\u2713" : "\u26A0";
        Mark.Foreground = new SolidColorBrush(success
            ? Color.FromRgb(0x4C, 0xAF, 0x50)
            : Color.FromRgb(0xFF, 0xA7, 0x26));
        TitleText.Text = title;
        if (string.IsNullOrEmpty(preview))
        {
            Preview.Visibility = Visibility.Collapsed;
        }
        else
        {
            Preview.Text = preview;
        }
        _timer.Tick += (_, _) => Dismiss();
        MouseLeftButtonDown += (_, _) => Dismiss();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        _ = Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE,
            ex | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
    }

    /// <summary>显示：先在屏幕外布局，就位后滑入工作区右下角。</summary>
    public void ShowToast()
    {
        Left = SystemParameters.WorkArea.Right; // 屏幕外，等布局完成再定位
        Top = SystemParameters.WorkArea.Bottom;
        Show();
        Dispatcher.BeginInvoke(() =>
        {
            double w = ActualWidth;
            Left = SystemParameters.WorkArea.Right - w - 12;
            Top = SystemParameters.WorkArea.Bottom - ActualHeight - 12;
            var slide = new DoubleAnimation(w + 20, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Slide.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
            _timer.Start();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Dismiss()
    {
        if (_dismissed)
        {
            return;
        }
        _dismissed = true;
        _timer.Stop();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>无动画立即关闭（被新通知替换时用）。</summary>
    public void ForceClose()
    {
        _dismissed = true;
        _timer.Stop();
        Close();
    }
}

/// <summary>通知管理：同一时刻一条，后到的替换先到的。必须在 UI 线程调用。</summary>
internal static class ToastService
{
    private static ToastWindow? _current;

    public static void Show(string title, string? preview = null, bool success = true)
    {
        if (!AppSettings.Current.ShowToast)
        {
            return; // 设置里关掉了通知
        }
        _current?.ForceClose();
        var toast = new ToastWindow(title, preview, success);
        _current = toast;
        toast.Closed += (_, _) =>
        {
            if (ReferenceEquals(_current, toast))
            {
                _current = null;
            }
        };
        toast.ShowToast();
    }
}
