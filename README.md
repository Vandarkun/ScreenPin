<div align="center">

# ScreenPin

**右键一圈，把需要的内容留在眼前。**

Windows 鼠标手势效率工具 · 截图贴图 · 本地 OCR 取字 · 窗口置顶 · 透明度调节 · 屏幕取色

[![最新版本](https://img.shields.io/github/v/release/Vandarkun/ScreenPin?style=flat-square&color=7668FA&label=Release)](https://github.com/Vandarkun/ScreenPin/releases/latest)
[![Windows 10 / 11](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?style=flat-square)](#下载安装)
[![.NET 8 · WPF](https://img.shields.io/badge/.NET-8%20%7C%20WPF-512BD4?style=flat-square)](ScreenPin.csproj)
[![MIT License](https://img.shields.io/badge/License-MIT-25A890?style=flat-square)](LICENSE)

[**下载安装版**](https://github.com/Vandarkun/ScreenPin/releases/latest/download/ScreenPin-Setup.exe) · [查看操作](#gestures) · [反馈与建议](https://github.com/Vandarkun/ScreenPin/issues)

![ScreenPin 场景示意：右键圈选参考内容，生成悬浮贴图，并在本地识别文字](docs/images/hero.png)

<sub>功能场景插画；贴图内容与桌面窗口为示意。</sub>

</div>

## 少切一次窗口，多留一点思路

写代码时要对照接口文档，整理资料时要抄一段图片里的文字，做设计时想取一个屏幕上的颜色——这些小动作，常常会打断手头的事。

**ScreenPin 把它们放到鼠标旁边。** 按住右键圈出内容，松开后就得到一张置顶贴图；换个方向画 U，文字就进了剪贴板。程序常驻系统托盘，不占任务栏，普通右键单击仍可正常打开菜单。

| 你正在做的事 | ScreenPin 可以这样帮忙 |
| --- | --- |
| 💻 对照文档写代码 | 把参数表、示例代码圈成贴图，放在编辑器旁边；支持同时保留多张 |
| 📚 从图片、扫描件中摘录文字 | 用 OCR 手势圈出文字，在本地识别后直接粘贴到笔记里 |
| 📌 边看边做、边查边写 | 把教程、参考窗口置顶，或调低窗口透明度方便对照 |
| 🎨 调整页面与界面配色 | 鼠标移到目标颜色上，按下组合键，复制 `#RRGGBB` 色值 |

### 顺手，也留得住专注

- **截图即贴图**：生成可拖动的置顶图片，默认同时复制到剪贴板，方便继续粘贴分享。
- **本地 OCR**：内置 PP-OCRv6 Tiny 中文模型，识别在本机完成，无需配置云服务或 API Key。
- **窗口随手管理**：置顶、取消置顶、透明度调节，都直接作用于鼠标所在窗口。
- **按习惯调整**：自动复制、操作通知、手势触发距离和开机自启，都可在托盘设置中修改，即时生效。

<a id="gestures"></a>

## 手势，一看就会

**按住鼠标右键 → 画出手势 → 松开执行。** 两种 U 形手势的关键在于起笔方向。

![手势方向图：从左上起笔向下、向右、向上为截图贴图；从右上起笔向下、向左、向上为 OCR 取字](docs/images/gestures.png)

> 截图范围是手势轨迹的**外接矩形**，不是沿曲线裁剪。画圈也能截图；初次使用时，建议先按图中的 U 形练习。

| 操作 | 结果 |
| --- | --- |
| 按住右键**画圈** | 截图并生成置顶贴图，默认复制图片 |
| 按住右键，从**左上**画 U：`↓ → ↑` | 截图贴图，与画圈相同 |
| 按住右键，从**右上**画 U：`↓ ← ↑` | OCR 取字，默认复制文字并显示通知 |
| `Alt` + 鼠标中键 | 切换鼠标所在窗口的置顶状态 |
| `Alt` + 滚轮 | 调整鼠标所在窗口的不透明度：30%～100%，每格 10% |
| `Ctrl` + 鼠标中键 | 读取鼠标所在像素的颜色，复制 `#RRGGBB` |
| 双击托盘图标 | 暂停 / 启用 ScreenPin |

**贴图上的操作：** 左键拖动移动，右键关闭，滚轮调节不透明度（30%～100%）。透明度调整只影响显示，不改变原始截图。

## 下载安装

适用于 **Windows 10 / 11，64 位（x64）**。

下载 [ScreenPin-Setup.exe](https://github.com/Vandarkun/ScreenPin/releases/latest/download/ScreenPin-Setup.exe)（约 8.5MB）双击安装；缺少 .NET 8 桌面运行时时，安装器会自动联网下载并安装。安装时可选桌面快捷方式与开机自启，可在系统"设置 → 应用"中卸载。

其他版本和更新说明见 [Releases](https://github.com/Vandarkun/ScreenPin/releases)。首次安装若需补齐运行时，请保持联网；截图、取字和取色功能可在本地离线使用。

### 第一次使用

1. 运行 ScreenPin，在系统托盘找到图钉图标；若未看到，展开任务栏右侧的隐藏图标区域。
2. 找一块参考内容，按住右键，从左上画一个 `↓ → ↑` 的 U，松开得到贴图。
3. 左键把贴图拖到旁边；在需要粘贴图片的应用中按 `Ctrl + V`。
4. 想取字时，从右上画 `↓ ← ↑` 的 U，看到“文字已复制”提示后粘贴即可。

右键托盘图标 → **设置...**，可调整自动复制、操作通知、手势触发距离和开机自启。上述复制行为以默认开启自动复制为前提。

## 从源码运行与构建

需要 Windows 和 [.NET 8 SDK](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)。首次构建需联网还原 NuGet 依赖。

```powershell
git clone https://github.com/Vandarkun/ScreenPin.git
cd ScreenPin
dotnet run
```

发布为依赖 .NET 8 桌面运行时的 x64 单文件程序（包含 OCR 模型）：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish-lite
```

产物：`publish-lite/ScreenPin.exe`。

<details>
<summary><strong>制作 Windows 安装包</strong></summary>

先完成上面的发布步骤，再安装 [Inno Setup 6](https://jrsoftware.org/isinfo.php)，并确保 `ISCC` 可从命令行调用：

```powershell
ISCC installer/ScreenPin.iss
```

产物：`installer/dist/ScreenPin-Setup.exe`。安装脚本会检测 .NET 8 桌面运行时，并在缺失时下载。升级安装前，请先从托盘退出正在运行的 ScreenPin。

</details>

## 一起把顺手的小工具做好

欢迎通过 [Issues](https://github.com/Vandarkun/ScreenPin/issues) 分享使用场景、报告问题，或提交 [Pull Request](https://github.com/Vandarkun/ScreenPin/pulls) 改进代码与文档。

反馈问题时，附上 Windows 版本、复现步骤，以及涉及的屏幕缩放比例 / 多显示器情况，会更方便定位。若这个小工具帮你少切了几次窗口，也欢迎点一个 **Star**，让更多人发现它。

## 开源许可与致谢

ScreenPin 采用 [MIT License](LICENSE)。感谢 [Sdcb.SimdPaddleOCR](https://github.com/sdcb/SimdPaddleOCR) 与 [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) 提供本地 OCR 能力。

OCR 引擎与模型使用 Apache-2.0 许可，详见 [第三方组件声明](installer/assets/THIRD-PARTY-NOTICES.txt)。
