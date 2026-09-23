using System.Diagnostics;
using System.Windows;
using ScreenPin.Core;

namespace ScreenPin.Windows;

/// <summary>
/// 设置窗口：改动即时生效并保存（无需确定按钮）。
/// 开机自启直接读写注册表；其余项写入 settings.json 并同步到运行中的组件。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Current;

    public SettingsWindow()
    {
        InitializeComponent();

        ChkAutoStart.IsChecked = AppSettings.IsAutoStartEnabled();
        ChkCopyImage.IsChecked = _settings.AutoCopyImage;
        ChkCopyText.IsChecked = _settings.AutoCopyText;
        ChkToast.IsChecked = _settings.ShowToast;
        SldArm.Value = _settings.ArmThreshold;
        UpdateArmLabel();

        ChkAutoStart.Click += (_, _) =>
        {
            bool on = ChkAutoStart.IsChecked == true;
            AppSettings.SetAutoStart(on);
            AppLog.Write($"设置：开机自启 {(on ? "开" : "关")}");
        };
        ChkCopyImage.Click += (_, _) => { _settings.AutoCopyImage = ChkCopyImage.IsChecked == true; _settings.Save(); };
        ChkCopyText.Click += (_, _) => { _settings.AutoCopyText = ChkCopyText.IsChecked == true; _settings.Save(); };
        ChkToast.Click += (_, _) => { _settings.ShowToast = ChkToast.IsChecked == true; _settings.Save(); };

        // 拖动中只更新运行值，保存延迟到窗口关闭，避免高频写盘
        SldArm.ValueChanged += (_, _) =>
        {
            _settings.ArmThreshold = (int)SldArm.Value;
            GestureConfig.ArmThreshold = _settings.ArmThreshold;
            UpdateArmLabel();
        };
        Closed += (_, _) => _settings.Save();

        BtnLog.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{AppLog.LogPath}\"")
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                AppLog.Write($"打开日志失败: {ex.Message}");
            }
        };
    }

    private void UpdateArmLabel()
    {
        LblArm.Text = $"{_settings.ArmThreshold} 像素（越小越灵敏）——按住右键移动超过该距离后进入手势模式";
    }
}
