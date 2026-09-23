using ScreenPin.Native;

namespace ScreenPin.Windows;

/// <summary>
/// 在世贴图的登记与命中。
/// 故意不用 WindowFromPoint：它做命中判断时可能向目标窗口的线程同步发消息，
/// 目标一卡就会拖住钩子线程（进而全系统的鼠标输入）。
/// 这里只用 GetWindowRect / EnumWindows：纯读窗口管理器数据，不发消息。
/// </summary>
internal static class PinHitTest
{
    private static readonly Dictionary<nint, int> _pins = new(); // hwnd → 视觉外沿（物理像素）

    internal static void Register(nint hwnd, int edge)
    {
        lock (_pins) _pins[hwnd] = edge;
    }

    internal static void Unregister(nint hwnd)
    {
        lock (_pins) _pins.Remove(hwnd);
    }

    /// <summary>
    /// 物理坐标 (x, y) 是否落在某张贴图的图像区域（不含阴影留白）。
    /// 多张贴图重叠时返回最上面的那张（EnumWindows 按 Z 序从顶往下枚举）。
    /// </summary>
    internal static bool TryHit(int x, int y, out nint hwnd)
    {
        hwnd = nint.Zero;
        KeyValuePair<nint, int>[] snapshot;
        lock (_pins) snapshot = _pins.ToArray();
        if (snapshot.Length == 0)
        {
            return false;
        }

        nint found = nint.Zero;
        Win32.EnumWindows((h, _) =>
        {
            foreach (var (pin, edge) in snapshot)
            {
                if (pin != h || !Win32.GetWindowRect(h, out var r))
                {
                    continue;
                }
                if (x >= r.Left + edge && x < r.Right - edge &&
                    y >= r.Top + edge && y < r.Bottom - edge)
                {
                    found = h;
                    return false; // 命中最上层贴图，停止枚举
                }
            }
            return true;
        }, nint.Zero);

        if (found == nint.Zero)
        {
            return false;
        }
        hwnd = found;
        return true;
    }

    /// <summary>请求关闭贴图。PostMessage 线程安全，WPF 收到 WM_CLOSE 会走正常关闭流程。</summary>
    internal static void RequestClose(nint hwnd)
    {
        _ = Win32.PostMessage(hwnd, Win32.WM_CLOSE, 0, 0);
    }
}
