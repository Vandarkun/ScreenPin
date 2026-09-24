using System.Text;
using ScreenPin.Native;

namespace ScreenPin.Core;

/// <summary>
/// 鼠标坐标 → 外部目标窗口（供 WindowPin / WindowOpacity 共用）。
/// 排除本程序自己的窗口（按进程 PID）和桌面。
/// 只能在 UI 线程调用：WindowFromPoint 可能向目标窗口的线程同步发消息，
/// 绝不能放进钩子回调（目标一卡会拖慢全系统鼠标）。
/// </summary>
internal static class WindowLocator
{
    private const uint GaRoot = 2;

    /// <summary>返回鼠标下的外部顶层窗口 HWND；无效目标（自己/桌面/无窗口）返回 0。</summary>
    public static nint FindExternalTarget(int x, int y)
    {
        var pt = new Win32.POINT { X = x, Y = y };
        nint child = Win32.WindowFromPoint(pt);
        if (child == nint.Zero)
        {
            return nint.Zero;
        }
        nint root = Win32.GetAncestor(child, GaRoot);
        if (root == nint.Zero)
        {
            root = child;
        }

        _ = Win32.GetWindowThreadProcessId(root, out uint pid);
        if (pid == (uint)Environment.ProcessId)
        {
            return nint.Zero; // 本程序自己的窗口
        }

        var cls = new StringBuilder(64);
        _ = Win32.GetClassName(root, cls, 64);
        if (cls.ToString() is "Progman" or "WorkerW")
        {
            return nint.Zero; // 桌面
        }
        return root;
    }
}
