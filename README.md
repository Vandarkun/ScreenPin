# ScreenPin

> Windows 手势效率小工具：右键一圈截图贴图、U 形手势取字（OCR 复制）、Alt+中键窗口置顶。

ScreenPin 常驻系统托盘，不占用任务栏。按住鼠标右键随手一圈，圈住的区域立刻变成可拖动的置顶贴图；换个方向画 U，圈住的文字直接识别进剪贴板；Alt+中键一点，任意窗口钉在最上层。普通右键单击不受任何影响。

## 功能一览

| 操作 | 效果 |
|---|---|
| 普通右键单击 | 与系统一致，正常弹出菜单 |
| 按住右键**画圈** | 框选区域 → 置顶贴图 + 图片进剪贴板 |
| 从**左上角**起笔画 U（下→右→上） | 同上，框选贴图 |
| 从**右上角**起笔画 U（下→左→上） | 识别区域文字 → 自动复制（右下角通知提示） |
| 贴图上 **左键拖动 / 右键** | 移动 / 关闭，可同时存在多张 |
| **Alt + 鼠标中键** | 切换任意窗口的置顶状态（托盘菜单可全部取消） |
| 托盘图标**双击** | 快速暂停 / 启用全部手势 |

更多可调项（开机自启、自动复制开关、手势触发灵敏度、操作通知）在托盘菜单 **设置...** 中，改动即时生效。

## 安装

从 [Releases](../../releases) 下载 `ScreenPin-Setup.exe`（约 8.5MB）双击安装：

- 支持 Windows 10 / 11（64 位）
- 缺少 .NET 8 桌面运行时会在安装过程中自动联网安装
- 安装时可选：桌面快捷方式、开机自启
- 设置 → 应用 中可卸载（不会卸载 .NET 运行时）

## 从源码构建

需要 [.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0)：

```bash
git clone https://github.com/Vandarkun/ScreenPin.git
cd ScreenPin
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish-lite
```

产物为单文件 `publish-lite/ScreenPin.exe`（约 8MB，含 OCR 模型）。

制作安装包（可选）：安装 [Inno Setup 6](https://jrsoftware.org/isinfo.php) 后执行 `ISCC installer/ScreenPin.iss`，得到 `installer/dist/ScreenPin-Setup.exe`。

## 项目结构

```text
Core/       手势状态机、钩子、截屏、OCR、窗口置顶、设置
Windows/    轨迹层、贴图、通知、设置界面
Tray/       系统托盘
Native/     Win32 互操作
installer/  Inno Setup 安装脚本
```

## 许可

[MIT](LICENSE)。第三方组件（PP-OCRv6 推理引擎与模型，Apache-2.0）声明见 `installer/assets/THIRD-PARTY-NOTICES.txt`。
