using System.Threading;
using System.Windows;
using ScreenPin.Core;
using ScreenPin.Tray;
using ScreenPin.Windows;

namespace ScreenPin;

public partial class App : Application
{
    private Mutex? _mutex;
    private GestureController? _controller;
    private TrayIconService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例：双开会装两个全局钩子互相打架
        _mutex = new Mutex(initiallyOwned: true, @"Local\ScreenPin.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("ScreenPin 已在运行，请查看系统托盘。", "ScreenPin",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // 应用已保存的设置（手势阈值等）
        AppSettings.ApplyToRuntime();

        _controller = new GestureController();
        _tray = new TrayIconService(
            onToggle: () =>
            {
                bool enabled = _controller!.Toggle();
                _tray!.SetEnabledState(enabled);
            },
            onExit: ExitApp,
            onSettings: OpenSettings,
            onUnpinAll: () =>
            {
                int n = WindowPinService.UnpinAll();
                if (n == 0)
                {
                    ToastService.Show("当前没有由 ScreenPin 置顶的窗口");
                }
            });
        _controller.Start();
    }

    private SettingsWindow? _settingsWindow;

    private void OpenSettings()
    {
        if (_settingsWindow is { } existing)
        {
            existing.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void OnDispatcherUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"发生未处理错误：{e.Exception.Message}", "ScreenPin",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void ExitApp()
    {
        WindowPinService.UnpinAll(silent: true); // 退出前恢复所有由本软件设置的置顶
        _tray?.Dispose();
        _tray = null;
        _controller?.Dispose();
        _controller = null;
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* 未持有，忽略 */ }
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WindowPinService.UnpinAll(silent: true); // 兜底：任何退出路径都不留置顶残局
        _tray?.Dispose();
        _controller?.Dispose();
        base.OnExit(e);
    }
}
