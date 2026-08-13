; Inno Setup 脚本：把 PixelLyric8BitFix 的自包含发布产物打成一个双击安装的 Setup.exe
; 用法：
;   1. dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
;      -p:IncludeNativeLibrariesForSelfExtract=true -o PixelLyric8BitFix\publish
;   2. 用 Inno Setup 编译本文件（ISCC.exe ZipPlay.iss），产物在 installer\output\ 下面
;
; 每发新版本记得同步改这里的 AppVersion 和 csproj 里的 <Version>，保持一致，
; 这样应用内的更新检测（跟 GitHub Release 的 tag 比大小）才会准。

#define MyAppName "ZipPlay"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Hong005-byte"
#define MyAppURL "https://github.com/Hong005-byte/ZipPlay"
#define MyAppExeName "PixelLyric8BitFix.exe"

[Setup]
AppId={{73EB0A1A-DB8A-4ADF-B4ED-C58002CE5C9F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
; 装到当前用户目录下，不需要管理员权限，不会弹 UAC——小工具类 app 更适合这种免打扰安装方式
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=ZipPlay-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\PixelLyric8BitFix\Icon.ico

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\PixelLyric8BitFix\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
