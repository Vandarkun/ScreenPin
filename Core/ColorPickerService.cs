using ScreenPin.Native;
using ScreenPin.Windows;

namespace ScreenPin.Core;

/// <summary>
/// 屏幕取色：Ctrl+中键 读取鼠标处像素，#RRGGBB 复制到剪贴板并弹通知。
/// GetPixel 读的是 DWM 合成后的屏幕表面，不向任何窗口发消息，速度快。
/// 在 UI 线程执行（由钩子投递）。
/// </summary>
internal static class ColorPickerService
{
    public static void PickAt(int x, int y)
    {
        try
        {
            nint dc = Win32.GetDC(nint.Zero);
            uint color;
            try
            {
                color = Win32.GetPixel(dc, x, y); // COLORREF: 0x00BBGGRR
            }
            finally
            {
                _ = Win32.ReleaseDC(nint.Zero, dc);
            }
            if (color == 0xFFFFFFFF)
            {
                AppLog.Write($"取色失败 @({x},{y})");
                return;
            }

            int r = (int)(color & 0xFF);
            int g = (int)((color >> 8) & 0xFF);
            int b = (int)((color >> 16) & 0xFF);
            string hex = $"#{r:X2}{g:X2}{b:X2}";

            CopyTextWithRetry(hex);
            AppLog.Write($"取色 @({x},{y}) {hex}");
            ToastService.Show("颜色已复制", $"{hex}  RGB({r}, {g}, {b})");
        }
        catch (Exception ex)
        {
            AppLog.Write($"取色失败: {ex.Message}");
        }
    }

    private static void CopyTextWithRetry(string text)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return;
            }
            catch (Exception) when (attempt < 4)
            {
                Thread.Sleep(30); // 剪贴板被短暂占用，稍候重试
            }
            catch (Exception ex)
            {
                AppLog.Write($"复制颜色失败: {ex.Message}");
                return;
            }
        }
    }
}
