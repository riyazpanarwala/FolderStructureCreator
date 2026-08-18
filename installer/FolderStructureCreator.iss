; Folder Structure Creator - Inno Setup script
; Builds a single FolderStructureCreatorSetup.exe that your team can double-click to install.
;
; Prerequisites before compiling this script:
;   1. Publish the app first (self-contained, single-file):
;        dotnet publish ..\src\FolderStructureCreator\FolderStructureCreator.csproj ^
;          -c Release -r win-x64 --self-contained true ^
;          -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
;          -o ..\publish
;      (or just run build-installer.ps1 in this folder, which does this for you)
;   2. Install Inno Setup: https://jrsoftware.org/isdl.php
;   3. Open this file in Inno Setup and click Compile, or run:
;        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" FolderStructureCreator.iss

#define MyAppName "Folder Structure Creator"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Riyaz"
#define MyAppExeName "FolderStructureCreator.exe"
#define MyPublishDir "..\publish"

[Setup]
AppId={{6F1B0E2A-3C4D-4E5F-9A6B-7C8D9E0F1A2B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user install by default so teammates without admin rights can still install.
PrivilegesRequired=lowest
OutputDir=..\installer-output
OutputBaseFilename=FolderStructureCreatorSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=AppIcon.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
; Pulls in everything from the publish output (exe + any side-by-side files).
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
