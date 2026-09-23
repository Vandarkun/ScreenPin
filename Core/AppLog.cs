using System.IO;

namespace ScreenPin.Core;

/// <summary>
/// 轻量诊断日志：异步追加到 %TEMP%\ScreenPin.log。
/// 写盘在后台线程，钩子线程零开销；用于排查“按键序列去了哪里”这类问题。
/// </summary>
internal static class AppLog
{
    internal static readonly string LogPath = Path.Combine(Path.GetTempPath(), "ScreenPin.log");

    internal static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}";
        _ = Task.Run(() =>
        {
            try { File.AppendAllText(LogPath, line); } catch { /* 日志失败不影响功能 */ }
        });
    }
}
