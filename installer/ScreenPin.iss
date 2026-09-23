; ScreenPin 安装脚本（Inno Setup 6，在线版）
;
;   编译：ISCC ScreenPin.iss → dist\ScreenPin-Setup.exe（约 8.5MB）
;   检测到缺少 .NET 8 桌面运行时的机器会在安装过程中联网下载运行时（带进度条，
;   失败可返回重试）。升级安装前请先退出正在运行的 ScreenPin（托盘右键 → 退出）。

#define MyAppName "ScreenPin"
#define MyAppVersion "0.1.0"
#define MyAppExeName "ScreenPin.exe"
#define DotNetInstallerName "windowsdesktop-runtime-win-x64.exe"
#define DotNetUrl "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{7C2A9E41-6F4D-4E0B-9A77-3F1D8B5C9E02}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=ScreenPin
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=dist
OutputBaseFilename=ScreenPin-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin
MinVersion=10.0
SetupIconFile=..\ScreenPin.ico

[Languages]
Name: "chs"; MessagesFile: "assets\ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "开机自动启动 ScreenPin（进入系统托盘）"; GroupDescription: "附加选项:"

[Files]
Source: "..\publish-lite\ScreenPin.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "assets\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "ScreenPin"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 缺运行时的机器：先执行已下载的运行时安装器（在下方下载逻辑中落到 {tmp}），再运行程序
Filename: "{tmp}\{#DotNetInstallerName}"; Parameters: "/install /quiet /norestart"; \
    StatusMsg: "正在安装 .NET 8 桌面运行时（约需一两分钟）..."; \
    Flags: waituntilterminated skipifdoesntexist; Check: NeedsDotNetRuntime
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; \
    Flags: nowait postinstall skipifsilent

[Code]
// 检查 64 位 .NET 桌面运行时 8.x：Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\8.*
// （比注册表更可靠，这是运行时实际的安装布局）
function NeedsDotNetRuntime(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := True;
  if FindFirst(ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  try
    Result := False; // 找到任一 8.x 版本目录即可
  finally
    FindClose(FindRec);
  end;
end;

// 就绪页点击“安装”后、正式安装前，如缺运行时先联网下载（原生下载页带进度条）
var
  DownloadPage: TDownloadWizardPage;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
  DownloadPage.Add('{#DotNetUrl}', '{#DotNetInstallerName}', '');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and NeedsDotNetRuntime() then
  begin
    try
      DownloadPage.Show;
      try
        DownloadPage.Download; // 落到 {tmp}\{#DotNetInstallerName}，供 [Run] 使用
      finally
        DownloadPage.Hide;
      end;
    except
      Result := False; // 留在当前页，点“上一步”再回来可重试
      // 注意：Pascal 的 #13 不能顶行首写，会被预处理器当成指令
      SuppressibleMsgBox('下载 .NET 8 运行时失败：' + #13#10#13#10 + GetExceptionMessage + #13#10#13#10 + '请检查网络连接后重试（可点“上一步”再继续触发重新下载）。', mbError, MB_OK, IDOK);
    end;
  end;
end;
