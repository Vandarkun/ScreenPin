using System.Windows.Media.Imaging;
using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny;

namespace ScreenPin.Core;

/// <summary>
/// OCR：纯托管 PP-OCRv6 Tiny（中文）。模型嵌入 DLL，随程序加载；
/// 识别直接吃 BGRA32 像素内存——正是截图的格式。
/// Run 可能耗时几百毫秒且实例非线程安全，识别在后台线程串行执行。
/// </summary>
internal static class OcrService
{
    private static PaddleOcrAll? _ocr;
    private static readonly SemaphoreSlim _initLock = new(1, 1);
    private static readonly object _runGate = new();

    /// <summary>启动时后台预载模型（约 200ms / 7MB），首次识别零等待。</summary>
    public static void Warmup() => _ = Task.Run(() => _ = EnsureLoadedAsync());

    private static async Task<PaddleOcrAll?> EnsureLoadedAsync()
    {
        if (_ocr is not null)
        {
            return _ocr;
        }
        await _initLock.WaitAsync();
        try
        {
            if (_ocr is null)
            {
                _ocr = await PaddleOcrAll.LoadAsync(ChineseV6TinyModels.Default);
                AppLog.Write("OCR 模型加载完成");
            }
            return _ocr;
        }
        catch (Exception ex)
        {
            AppLog.Write($"OCR 模型加载失败: {ex.Message}");
            return null;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// 识别截图。返回 null = 引擎不可用；空串 = 没认出任何文字。
    /// </summary>
    public static async Task<string?> RecognizeAsync(BitmapSource image)
    {
        var ocr = await EnsureLoadedAsync();
        if (ocr is null)
        {
            return null;
        }

        int w = image.PixelWidth;
        int h = image.PixelHeight;
        int stride = w * 4;
        var buffer = new byte[stride * h];
        image.CopyPixels(buffer, stride, 0);

        return await Task.Run(() =>
        {
            lock (_runGate)
            {
                // GDI 截图的 Alpha 字节不可信，统一填满再喂（引擎按 BGRA32 读）
                for (int i = 3; i < buffer.Length; i += 4)
                {
                    buffer[i] = 255;
                }
                var result = ocr.Run(buffer, w, h, stride, ImagePixelFormat.Bgra32);
                return CleanText(result.Text);
            }
        });
    }

    /// <summary>清掉噪声行（如代码缩进里单独的 { } 被认成的一个符号）并修剪空白。</summary>
    private static string CleanText(string raw)
    {
        var lines = new List<string>();
        foreach (var line in raw.Split('\n'))
        {
            string t = line.Trim('\r', ' ', '\t');
            if (t.Length == 0)
            {
                continue;
            }
            if (t.Length == 1 && t[0] < 128 && char.IsPunctuation(t[0]))
            {
                continue; // 单个 ASCII 标点行按噪声丢弃；单个汉字/字母保留
            }
            lines.Add(t);
        }
        return string.Join("\n", lines);
    }
}
