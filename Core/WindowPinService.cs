using System.Text;
using ScreenPin.Native;
using ScreenPin.Windows;

namespace ScreenPin.Core;

/// <summary>
/// WindowPin：Alt+中键 切换任意窗口的置顶状态。
/// 窗口查找与置顶操作全部在 UI 线程执行——WindowFromPoint 可能向目标窗口的线程
/// 同步发消息，绝不能放进钩子回调（目标一卡会拖慢全系统鼠标）。
/// 排除本程序自身的窗口（按进程 PID 判断，一网打尽）和桌面。
/// </summary>
internal static class WindowPinService
{
    private const uint GaRoot = 2;
    private static readonly object Gate = new();
    private static readonly HashSet<nint> Pinned = new();
    private static readonly HashSet<string> ExcludedClasses = new() { "Progman", "WorkerW" }; // 桌面

    /// <summary>切换鼠标位置下窗口的置顶状态（必须在 UI 线程调用）。</summary>
    public static void ToggleAt(int x, int y)
    {
        try
        {
            var pt = new Win32.POINT { X = x, Y = y };
            nint child = Win32.WindowFromPoint(pt);
            if (child == nint.Zero)
            {
                return;
            }
            nint root = Win32.GetAncestor(child, GaRoot);
            if (root == nint.Zero)
            {
                root = child;
            }

            // 排除本程序自己的窗口（轨迹/贴图/通知/设置全覆盖）
            _ = Win32.GetWindowThreadProcessId(root, out uint pid);
            if (pid == (uint)Environment.ProcessId)
            {
                return;
            }
            // 排除桌面
            var cls = new StringBuilder(64);
            _ = Win32.GetClassName(root, cls, 64);
            if (ExcludedClasses.Contains(cls.ToString()))
            {
                return;
            }

            bool pin;
            lock (Gate)
            {
                pin = !Pinned.Remove(root); // 已在列表中 = 这次是取消
                if (pin)
                {
                    Pinned.Add(root);
                }
            }
            Apply(root, pin);

            string title = GetTitle(root);
            AppLog.Write($"WindowPin: {(pin ? "置顶" : "取消置顶")} 0x{root:X} {title}");
            ToastService.Show(pin ? "窗口已置顶" : "已取消置顶", string.IsNullOrEmpty(title) ? null : title);
        }
        catch (Exception ex)
        {
            AppLog.Write($"WindowPin 失败: {ex.Message}");
        }
    }

    /// <summary>取消全部由本软件设置的置顶。返回恢复的窗口数。</summary>
    public static int UnpinAll(bool silent = false)
    {
        nint[] targets;
        lock (Gate)
        {
            targets = Pinned.ToArray();
            Pinned.Clear();
        }
        int count = 0;
        foreach (nint hwnd in targets)
        {
            if (Win32.IsWindow(hwnd))
            {
                Apply(hwnd, topmost: false);
                count++;
            }
        }
        if (count > 0)
        {
            AppLog.Write($"WindowPin: 批量取消置顶 {count} 个窗口");
        }
        if (!silent && count > 0)
        {
            ToastService.Show($"已恢复 {count} 个窗口", "全部取消置顶");
        }
        return count;
    }

    private static void Apply(nint hwnd, bool topmost)
    {
        _ = Win32.SetWindowPos(hwnd, topmost ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST,
            0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    private static string GetTitle(nint hwnd)
    {
        var sb = new StringBuilder(128);
        _ = Win32.GetWindowText(hwnd, sb, 128);
        return sb.ToString();
    }
}
