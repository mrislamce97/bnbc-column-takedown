[Setup]
AppName=BNBC Column Load Takedown (FPS)
AppVersion=1.0.0
DefaultDirName={commonappdata}\Autodesk\ApplicationPlugins\ColumnLoadTakedown.bundle
DisableDirPage=yes
OutputBaseFilename=BNBC_Column_Load_Plugin_Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin

[Files]
Source: "ColumnLoadTakedown.bundle\PackageContents.xml"; DestDir: "{app}"; Flags: ignoreversion
Source: "ColumnLoadTakedown.bundle\Contents\net48\*"; DestDir: "{app}\Contents\net48"; Flags: ignoreversion recursesubdirs
Source: "ColumnLoadTakedown.bundle\Contents\net8.0\*"; DestDir: "{app}\Contents\net8.0"; Flags: ignoreversion recursesubdirs