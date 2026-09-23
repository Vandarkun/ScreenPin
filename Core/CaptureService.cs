using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenPin.Native;

namespace ScreenPin.Core;

/// <summary>
/// GDI BitBlt 截屏。抓不到受 DRM 保护/部分硬件加速的内容，第一版够用。
/// 区域会先夹到虚拟桌面范围内。
/// </summary>
public static class CaptureService
{
    public static BitmapSource? Capture(CaptureRegion region)
    {
        int vx = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
        int vy = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
        int vw = Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN);
        int vh = Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN);

        int x = Math.Max(region.X, vx);
        int y = Math.Max(region.Y, vy);
        int right = Math.Min(region.X + region.Width, vx + vw);
        int bottom = Math.Min(region.Y + region.Height, vy + vh);
        int w = right - x;
        int h = bottom - y;
        if (w <= 0 || h <= 0)
        {
            return null;
        }

        nint screen = Win32.GetDC(nint.Zero);
        nint mem = Win32.CreateCompatibleDC(screen);
        nint bmp = Win32.CreateCompatibleBitmap(screen, w, h);
        nint old = Win32.SelectObject(mem, bmp);
        try
        {
            if (!Win32.BitBlt(mem, 0, 0, w, h, screen, x, y, Win32.SRCCOPY | Win32.CAPTUREBLT))
            {
                return null;
            }

            int stride = w * 4;
            var buffer = new byte[stride * h];
            var bmi = new Win32.BITMAPINFO
            {
                Header = new Win32.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                    biWidth = w,
                    biHeight = -h, // 负值 = top-down，行序与 WPF 一致
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0, // BI_RGB
                },
            };
            if (Win32.GetDIBits(mem, bmp, 0, (uint)h, buffer, bmi, 0) == 0)
            {
                return null;
            }

            // 屏幕 DC 的 Alpha 通道不可信，用 Bgr32 忽略之，避免整张图变透明
            var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, buffer, stride);
            source.Freeze(); // 之后会跨线程/长久持有
            return source;
        }
        finally
        {
            Win32.SelectObject(mem, old);
            Win32.DeleteObject(bmp);
            Win32.DeleteDC(mem);
            Win32.ReleaseDC(nint.Zero, screen);
        }
    }
}
