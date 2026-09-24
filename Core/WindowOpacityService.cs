using System.Text;
using ScreenPin.Native;

namespace ScreenPin.Core;

/// <summary>
/// 外部窗口透明度：Alt+滚轮 每格 ±10%，范围 30%~100%。
/// 到 100% 即恢复原状（如果分层样式是我们加的就摘掉），并停止跟踪；
/// 退出时 RestoreAll 恢复所有仍处于调整状态的窗口。
/// 所有操作在 UI 线程执行（见 WindowLocator 的线程约束）。
/// </summary>
internal static class WindowOpacityService
{
    private sealed record Tracked(double Opacity, bool AddedLayered);

    private static readonly object Gate = new();
    private static readonly Dictionary<nint, Tracked> Adjusted = new();

    /// <summary>调整鼠标位置下窗口的透明度（wheelDelta &gt; 0 调高）。</summary>
    public static void AdjustAt(int x, int y, int wheelDelta)
    {
        try
        {
            nint hwnd = WindowLocator.FindExternalTarget(x, y);
            if (hwnd == nint.Zero || !Win32.IsWindow(hwnd))
            {
                return;
            }

            lock (Gate)
            {
                if (!Adjusted.TryGetValue(hwnd, out Tracked? state))
                {
                    // 若别的工具已经设过透明，从其实际值起步，避免跳变
                    int ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
                    bool layered = (ex & Win32.WS_EX_LAYERED) != 0;
                    double current = 1.0;
                    if (layered
                        && Win32.GetLayeredWindowAttributes(hwnd, out _, out byte alpha, out uint flags)
                        && (flags & Win32.LWA_ALPHA) != 0)
                    {
                        current = alpha / 255.0;
                    }
                    state = new Tracked(current, AddedLayered: !layered);
                }

                double next = Math.Clamp(state.Opacity + (wheelDelta > 0 ? 0.1 : -0.1), 0.3, 1.0);
                if (Math.Abs(next - state.Opacity) < 0.001)
                {
                    return; // 已到边界
                }

                if (state.AddedLayered)
                {
                    EnsureLayered(hwnd); // 我们负责加的分层样式：首次调整时补上
                }

                if (next >= 1.0)
                {
                    if (state.AddedLayered)
                    {
                        RemoveLayered(hwnd); // 恢复到彻底的原始状态
                    }
                    else
                    {
                        _ = Win32.SetLayeredWindowAttributes(hwnd, 0, 255, Win32.LWA_ALPHA);
                    }
                    Adjusted.Remove(hwnd);
                    AppLog.Write($"窗口透明度恢复 100%: 0x{hwnd:X} {Title(hwnd)}");
                }
                else
                {
                    _ = Win32.SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(next * 255), Win32.LWA_ALPHA);
                    Adjusted[hwnd] = state with { Opacity = next };
                    AppLog.Write($"窗口透明度 {next:P0}: 0x{hwnd:X} {Title(hwnd)}");
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"窗口透明度调整失败: {ex.Message}");
        }
    }

    /// <summary>恢复所有仍被调整过的窗口（程序退出时调用）。</summary>
    public static void RestoreAll()
    {
        KeyValuePair<nint, Tracked>[] snapshot;
        lock (Gate)
        {
            snapshot = Adjusted.ToArray();
            Adjusted.Clear();
        }
        foreach (var (hwnd, state) in snapshot)
        {
            if (!Win32.IsWindow(hwnd))
            {
                continue;
            }
            try
            {
                if (state.AddedLayered)
                {
                    RemoveLayered(hwnd);
                }
                else
                {
                    _ = Win32.SetLayeredWindowAttributes(hwnd, 0, 255, Win32.LWA_ALPHA);
                }
            }
            catch
            {
                // 恢复尽力而为，不因个别窗口失败中断
            }
        }
        if (snapshot.Length > 0)
        {
            AppLog.Write($"窗口透明度：退出前恢复 {snapshot.Length} 个窗口");
        }
    }

    private static void EnsureLayered(nint hwnd)
    {
        int ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        if ((ex & Win32.WS_EX_LAYERED) == 0)
        {
            _ = Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_LAYERED);
            ApplyFrameChanged(hwnd);
        }
    }

    private static void RemoveLayered(nint hwnd)
    {
        int ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        if ((ex & Win32.WS_EX_LAYERED) != 0)
        {
            _ = Win32.SetLayeredWindowAttributes(hwnd, 0, 255, Win32.LWA_ALPHA);
            _ = Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, ex & ~Win32.WS_EX_LAYERED);
            ApplyFrameChanged(hwnd);
        }
    }

    private static void ApplyFrameChanged(nint hwnd) =>
        _ = Win32.SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);

    private static string Title(nint hwnd)
    {
        var sb = new StringBuilder(128);
        _ = Win32.GetWindowText(hwnd, sb, 128);
        return sb.ToString();
    }
}
