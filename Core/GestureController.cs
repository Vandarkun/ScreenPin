using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenPin.Native;
using ScreenPin.Windows;

namespace ScreenPin.Core;

/// <summary>
/// 组装整条链路：钩子事件 → 手势状态机 → 轨迹显示 → 截图 → 贴图窗口。
/// 钩子线程上只做轻量计算和 UI 投递；所有窗口操作都在 UI 线程。
/// 手势的完整生命周期（开始/判定/收尾）一律用 Send 优先级投递，
/// 保证与“下一次手势开始”之间严格按真实发生顺序执行，不会互相踩踏。
/// </summary>
public sealed class GestureController : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly MouseHook _hook = new();
    private readonly GestureTracker _tracker = new();
    private GestureOverlay? _activeOverlay;   // 当前手势正在使用的轨迹窗口
    private GestureOverlay? _standbyOverlay;  // 预备的新窗口（从未显示过，无旧画面缓存）
    private int _trailPending; // 0 = 空闲，1 = 已有一次排队中的轨迹刷新（合并高频移动）
    private volatile bool _swallowNextRightUp; // 贴图右键关闭后吞掉对应的松开，防止孤儿 RightUp 在下层窗口弹出菜单
    private volatile bool _swallowNextMiddleUp; // WindowPin 的 Alt+中键：按下吞掉后，松开也成对吞掉

    public bool Enabled { get; private set; } = true;

    /// <summary>必须在 UI 线程上构造。</summary>
    public GestureController()
    {
        _ui = Dispatcher.CurrentDispatcher;
        _hook.Event += OnHookEvent;
    }

    public void Start()
    {
        // 预备一个“从未显示过”的轨迹窗口：无旧画面（不会闪旧轨迹），
        // HWND 已提前建好（手势触发时只剩定位+显示，不会卡）
        _ui.BeginInvoke(() => _standbyOverlay ??= CreateStandbyOverlay());
        _hook.Start();
        OcrService.Warmup(); // 后台预载 OCR 模型
    }

    public bool SetEnabled(bool value)
    {
        if (Enabled == value)
        {
            return Enabled;
        }
        Enabled = value;
        if (!value)
        {
            _tracker.Reset(); // 暂停时正在画的手势直接作废
            _swallowNextRightUp = false;
            _ui.BeginInvoke(() =>
            {
                _activeOverlay?.CancelGesture();
                _activeOverlay = null;
            });
        }
        _hook.Enabled = value; // 暂停 = 钩子全部放行，原生右键完全恢复
        return Enabled;
    }

    public bool Toggle() => SetEnabled(!Enabled);

    /// <summary>钩子线程回调：吞放决策必须在这里同步做出，其余工作全部投递出去。</summary>
    private bool OnHookEvent(HookMouseEventArgs e)
    {
        try
        {
            switch (e.Kind)
            {
                case HookMouseKind.MiddleDown:
                {
                    // Alt+中键 = WindowPin（窗口置顶切换）；普通中键完全放行
                    bool altHeld = (Win32.GetAsyncKeyState(Win32.VK_MENU) & 0x8000) != 0;
                    if (!altHeld)
                    {
                        return false;
                    }
                    _swallowNextMiddleUp = true;
                    AppLog.Write($"MiddleDown @({e.X},{e.Y}): Alt+中键，WindowPin 切换");
                    // 钩子线程只做判定与投递；窗口查找/置顶在 UI 线程执行
                    int x = e.X, y = e.Y;
                    _ui.BeginInvoke(() => WindowPinService.ToggleAt(x, y), DispatcherPriority.Send);
                    return true;
                }

                case HookMouseKind.MiddleUp:
                {
                    if (_swallowNextMiddleUp)
                    {
                        _swallowNextMiddleUp = false;
                        return true; // 与被吞的按下配对，不留孤儿事件
                    }
                    return false;
                }

                case HookMouseKind.RightDown:
                    // 右键点在贴图上：整个右键事务由钩子接管——按下松开全部吞掉，
                    // 任何窗口都收不到不完整的按键序列；关闭动作由钩子主动发 WM_CLOSE。
                    if (PinHitTest.TryHit(e.X, e.Y, out var pin))
                    {
                        _swallowNextRightUp = true;
                        AppLog.Write($"RightDown @({e.X},{e.Y}): 贴图右键，吞下并请求关闭 pin=0x{pin:X}");
                        _ui.BeginInvoke(() => PinHitTest.RequestClose(pin));
                        return true;
                    }
                    AppLog.Write($"RightDown @({e.X},{e.Y}): 进入监听（吞下）");
                    return _tracker.OnRightDown(e.X, e.Y);

                case HookMouseKind.Move:
                {
                    var (appended, becameActive) = _tracker.OnMove(e.X, e.Y);
                    if (becameActive)
                    {
                        _ui.BeginInvoke(BeginGesture, DispatcherPriority.Send);
                    }
                    if (appended)
                    {
                        QueueTrailUpdate();
                    }
                    return false; // 移动事件绝不吞：吞掉会冻住光标
                }

                case HookMouseKind.RightUp:
                {
                    if (_swallowNextRightUp)
                    {
                        _swallowNextRightUp = false;
                        AppLog.Write("RightUp: 吞下（贴图关闭事务的松开）");
                        return true;
                    }
                    var r = _tracker.OnRightUp(e.X, e.Y);
                    if (r.ReplayClick)
                    {
                        AppLog.Write($"RightUp @({e.X},{e.Y}): 普通右键，回放点击");
                        // 不在钩子回调里直接注入输入，避免事件处理的同步重入
                        ThreadPool.QueueUserWorkItem(_ => ReplayRightClick());
                    }
                    else if (r.Action == GestureAction.CopyText)
                    {
                        var region = r.Region;
                        AppLog.Write($"RightUp @({e.X},{e.Y}): 取字手势 ↓←↑，区域 {region.Width}x{region.Height} @({region.X},{region.Y})");
                        _ui.BeginInvoke(() => CompleteOcrGesture(region), DispatcherPriority.Send);
                    }
                    else if (r.Action == GestureAction.Pin)
                    {
                        var region = r.Region;
                        AppLog.Write($"RightUp @({e.X},{e.Y}): 框选手势 {region.Width}x{region.Height} @({region.X},{region.Y})");
                        _ui.BeginInvoke(() => CompleteGesture(region), DispatcherPriority.Send);
                    }
                    else if (r.WasActive)
                    {
                        AppLog.Write("RightUp: 手势无效，轨迹淡出");
                        _ui.BeginInvoke(RejectGesture, DispatcherPriority.Send);
                    }
                    return r.Swallow;
                }

                default:
                    return false;
            }
        }
        catch
        {
            return false; // 钩子回调里绝不能让异常逃逸
        }
    }

    /// <summary>手势开始（UI 线程）：换上预备窗口。</summary>
    private void BeginGesture()
    {
        // 防御：上一个手势的窗口若因故还没收尾，直接清场
        if (_activeOverlay is { } stale)
        {
            stale.CancelGesture();
            _activeOverlay = null;
        }
        var overlay = _standbyOverlay ?? CreateStandbyOverlay();
        _standbyOverlay = null;
        _activeOverlay = overlay;
        overlay.ShowForGesture();
    }

    private void RejectGesture()
    {
        var overlay = _activeOverlay;
        _activeOverlay = null;
        overlay?.FinishGesture(accepted: false); // 轨迹淡出后窗口自毁
    }

    private async void CompleteGesture(CaptureRegion region)
    {
        // 必须先隐藏轨迹窗口再截图，否则轨迹会进入截图
        var overlay = _activeOverlay;
        _activeOverlay = null;
        overlay?.FinishGesture(accepted: true);
        try
        {
            await Task.Delay(50); // 等 DWM 把轨迹从屏幕上撤干净
            var shot = CaptureService.Capture(region);
            if (shot is null)
            {
                return;
            }
            new PinWindow(shot, region).Show();
            AppLog.Write($"贴图已创建 @({region.X},{region.Y}) {region.Width}x{region.Height}");
            if (AppSettings.Current.AutoCopyImage)
            {
                CopyToClipboard(shot);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ScreenPin: 截图失败 {ex.Message}");
        }
    }

    /// <summary>取字手势（↓←↑）：撤掉轨迹 → 截图 → OCR → 文字进剪贴板 → 右下角通知。</summary>
    private async void CompleteOcrGesture(CaptureRegion region)
    {
        var overlay = _activeOverlay;
        _activeOverlay = null;
        overlay?.FinishGesture(accepted: true);
        try
        {
            await Task.Delay(50); // 等 DWM 把轨迹从屏幕上撤干净
            var shot = CaptureService.Capture(region);
            if (shot is null)
            {
                return;
            }
            string? text = await OcrService.RecognizeAsync(shot);
            if (text is null)
            {
                AppLog.Write("OCR: 引擎不可用");
                ToastService.Show("OCR 不可用", "模型加载失败，请查看日志", success: false);
                return;
            }
            if (text.Length == 0)
            {
                AppLog.Write("OCR: 未识别到文字");
                ToastService.Show("未识别到文字", "这个区域里没有认出可复制的文字", success: false);
                return;
            }
            bool copied = false;
            if (AppSettings.Current.AutoCopyText)
            {
                CopyTextToClipboard(text);
                copied = true;
            }
            AppLog.Write($"OCR: {(copied ? "已复制" : "识别完成")} {text.Length} 字");
            ToastService.Show(copied ? "文字已复制" : "识别完成", text.Replace("\n", " "));
        }
        catch (Exception ex)
        {
            AppLog.Write($"OCR 失败: {ex.Message}");
            ToastService.Show("OCR 出错了", ex.Message, success: false);
        }
    }

    /// <summary>把识别出的文字写入剪贴板，带重试（剪贴板常被短暂占用）。</summary>
    private static void CopyTextToClipboard(string text)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (Exception) when (attempt < 4)
            {
                Thread.Sleep(30);
            }
            catch (Exception ex)
            {
                AppLog.Write($"复制文本失败: {ex.Message}");
                return;
            }
        }
    }

    /// <summary>合并高频移动：同一时刻最多排队一次轨迹刷新。</summary>
    private void QueueTrailUpdate()
    {
        if (Interlocked.CompareExchange(ref _trailPending, 1, 0) != 0)
        {
            return;
        }
        _ui.BeginInvoke(() =>
        {
            _trailPending = 0;
            _activeOverlay?.UpdateTrail(_tracker.PointsSnapshot());
        }, DispatcherPriority.Render);
    }

    /// <summary>创建备用轨迹窗口：从未显示过（无旧画面缓存），HWND 已提前建好。</summary>
    private GestureOverlay CreateStandbyOverlay()
    {
        var overlay = new GestureOverlay();
        overlay.Closed += (_, _) => ScheduleStandby(); // 用掉了就再备一个
        overlay.Prewarm();
        return overlay;
    }

    private void ScheduleStandby()
    {
        // 低优先级补备，维持“最多一个备用”的不变量
        _ui.BeginInvoke(() => _standbyOverlay ??= CreateStandbyOverlay(),
            DispatcherPriority.Background);
    }

    /// <summary>回放一次完整的普通右键（在当前光标位置）。</summary>
    private static void ReplayRightClick()
    {
        var inputs = new Win32.INPUT[2];
        inputs[0].type = (int)Win32.INPUT_MOUSE;
        inputs[0].mi.dwFlags = Win32.MOUSEEVENTF_RIGHTDOWN;
        inputs[1].type = (int)Win32.INPUT_MOUSE;
        inputs[1].mi.dwFlags = Win32.MOUSEEVENTF_RIGHTUP;
        _ = Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32.INPUT>());
    }

    /// <summary>
    /// 把截图写入系统剪贴板。剪贴板常被其他程序短暂占用（剪贴板管理器等），带重试；
    /// 同时放位图和 PNG 两种格式，浏览器/聊天软件等偏好 PNG 的程序粘贴质量更好。
    /// </summary>
    private static void CopyToClipboard(BitmapSource shot)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var data = new DataObject();
                data.SetImage(shot);
                var png = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(shot));
                encoder.Save(png);
                data.SetData("PNG", png);
                Clipboard.SetDataObject(data, true);
                AppLog.Write("截图已复制到剪贴板");
                return;
            }
            catch (Exception) when (attempt < 4)
            {
                Thread.Sleep(30); // 被占用，稍候重试
            }
            catch (Exception ex)
            {
                AppLog.Write($"复制到剪贴板失败: {ex.Message}");
                return;
            }
        }
    }

    public void Dispose()
    {
        _hook.Event -= OnHookEvent;
        _hook.Dispose();
        _ui.BeginInvoke(() =>
        {
            _activeOverlay?.Close();
            _standbyOverlay?.Close();
            _activeOverlay = null;
            _standbyOverlay = null;
        });
    }
}
