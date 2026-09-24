using ScreenPin.Native;
using static ScreenPin.Native.Win32;

namespace ScreenPin.Core;

public enum HookMouseKind
{
    Move,
    RightDown,
    RightUp,
    MiddleDown,
    MiddleUp,
    Wheel
}

public readonly record struct HookMouseEventArgs(HookMouseKind Kind, int X, int Y, bool Injected, int WheelDelta = 0);

/// <summary>
/// WH_MOUSE_LL 全局鼠标钩子。
/// 低级钩子要求安装它的线程有消息泵，且 UI 线程卡顿时系统会直接摘除钩子，
/// 所以钩子跑在独立线程上；暂停时不停钩子、只在回调里放行，行为等价且更稳。
/// </summary>
public sealed class MouseHook : IDisposable
{
    private readonly LowLevelMouseProc _proc; // 字段引用防止委托被 GC
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private uint _threadId;
    private volatile nint _hook;
    private volatile bool _enabled;

    /// <summary>回调在钩子线程上执行，必须快速返回。返回 true 表示吞掉该事件。</summary>
    public event Func<HookMouseEventArgs, bool>? Event;

    public MouseHook()
    {
        _proc = HookProc;
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public void Start()
    {
        if (_thread is { IsAlive: true })
        {
            _enabled = true;
            return;
        }
        _thread = new Thread(Run)
        {
            Name = "ScreenPin.MouseHook",
            IsBackground = true,
        };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3)); // 等钩子真正装好再视为启用
        _enabled = true;
    }

    public void Stop() => _enabled = false;

    private void Run()
    {
        _threadId = Win32.GetCurrentThreadId();
        _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _proc, Win32.GetModuleHandle(null), 0);
        _ready.Set();
        if (_hook == nint.Zero)
        {
            return; // 安装失败：钩子不生效，程序其余部分照常
        }

        // 低级钩子的心跳就是这个消息循环；WM_QUIT 退出并卸载钩子
        while (Win32.GetMessage(out _, nint.Zero, 0, 0) > 0) { }

        Win32.UnhookWindowsHookEx(_hook);
        _hook = nint.Zero;
    }

    private nint HookProc(int nCode, nuint wParam, ref MSLLHOOKSTRUCT lParam)
    {
        if (nCode >= 0 && _enabled && Event is not null)
        {
            // 我们自己回放的右键必须无条件放行，否则会递归吞掉
            bool injected = (lParam.flags & (Win32.LLMHF_INJECTED | Win32.LLMHF_LOWERIL_INJECTED)) != 0;
            HookMouseKind? kind = (int)wParam switch
            {
                Win32.WM_MOUSEMOVE => HookMouseKind.Move,
                Win32.WM_RBUTTONDOWN => HookMouseKind.RightDown,
                Win32.WM_RBUTTONUP => HookMouseKind.RightUp,
                Win32.WM_MBUTTONDOWN => HookMouseKind.MiddleDown,
                Win32.WM_MBUTTONUP => HookMouseKind.MiddleUp,
                Win32.WM_MOUSEWHEEL => HookMouseKind.Wheel,
                _ => (HookMouseKind?)null,
            };
            if (kind is { } k && !injected)
            {
                // 滚轮增量在 mouseData 的高 16 位，一格 ±120
                int wheel = k == HookMouseKind.Wheel ? (short)((lParam.mouseData >> 16) & 0xFFFF) : 0;
                var args = new HookMouseEventArgs(k, lParam.pt.X, lParam.pt.Y, injected, wheel);
                if (Event(args))
                {
                    return 1; // 吞掉，不进入系统输入链
                }
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, ref lParam);
    }

    public void Dispose()
    {
        _enabled = false;
        if (_threadId != 0)
        {
            Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, 0, 0);
        }
        _thread?.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
