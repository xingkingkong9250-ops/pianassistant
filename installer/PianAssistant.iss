; 拉片助手（PianAssistant）安装包脚本 —— Inno Setup 6
; 编译：& ISCC.exe /DAppVersion=1.0.0 installer\PianAssistant.iss
; 源目录依赖门禁产物：artifacts\staging\PianAssistant_v{AppVersion}\
; 发布形态：self-contained win-x64（内置 .NET 运行时 + LibVLC + ffmpeg），目标机免环境

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "拉片助手"
#define AppNameEn "PianAssistant"
#define AppPublisher "PianAssistant"
#define AppExeName "PianAssistant.exe"
#define SourceDir "..\artifacts\staging\PianAssistant_v" + AppVersion

[Setup]
AppId={{8F2A3B91-5C6D-4E7F-9A0B-1C2D3E4F5A6B}
AppName={#AppName} ({#AppNameEn})
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppNameEn}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename={#AppNameEn}_Setup_v{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
