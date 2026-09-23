using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace ScreenPin.Core;

/// <summary>
/// 用户设置：JSON 持久化到 %APPDATA%\ScreenPin\settings.json，改动即时保存。
/// 手势阈值会同步进 GestureConfig（钩子线程每次读的都是最新值）。
/// </summary>
public sealed class AppSettings
{
    public bool AutoCopyImage { get; set; } = true;
    public bool AutoCopyText { get; set; } = true;
    public bool ShowToast { get; set; } = true;
    public int ArmThreshold { get; set; } = 25;

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenPin");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    /// <summary>进程启动时加载一次；所有线程读这个实例。</summary>
    public static AppSettings Current { get; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"读取设置失败，使用默认值: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppLog.Write($"保存设置失败: {ex.Message}");
        }
    }

    /// <summary>把当前设置应用到运行中的各组件。</summary>
    public static void ApplyToRuntime()
    {
        GestureConfig.ArmThreshold = Current.ArmThreshold;
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ScreenPin";

    /// <summary>开机自启的注册表开关（HKCU，无需管理员）。</summary>
    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null)
            {
                return;
            }
            if (enable)
            {
                key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"设置开机自启失败: {ex.Message}");
        }
    }

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValueName) is not null;
        }
        catch
        {
            return false;
        }
    }
}
