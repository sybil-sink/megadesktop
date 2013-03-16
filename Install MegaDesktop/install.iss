[Setup]
AppName=Mega Desktop
AppVersion=0.7
DefaultDirName={pf}\Mega Desktop
DefaultGroupName=Mega Desktop
UninstallDisplayIcon={app}\MegaDesktop.exe
Compression=lzma2
SolidCompression=yes
AppPublisher=The Mega Desktop Team
AppPublisherURL=http://megadesktop.com/
AppContact=Mega Desktop Support
AppSupportURL=http://megadesktop.uservoice.com/forums/191321-general



[Files]
Source: Synchronization-v2.1-x64-ENU.msi; DestDir: {tmp}; Flags: deleteafterinstall;
Source: Synchronization-v2.1-x86-ENU.msi; DestDir: {tmp}; Flags: deleteafterinstall;
Source: ProviderServices-v2.1-x64-ENU.msi; DestDir: {tmp}; Flags: deleteafterinstall;
Source: ProviderServices-v2.1-x86-ENU.msi; DestDir: {tmp}; Flags: deleteafterinstall; 
Source: "MegaDesktop.exe"; DestDir: "{app}"; BeforeInstall: BeforeInstall()
Source: "MegaApi.dll"; DestDir: "{app}"
Source: "MegaSync.exe"; DestDir: "{app}"
Source: "Newtonsoft.Json.dll"; DestDir: "{app}"
Source: "SyncLib.dll"; DestDir: "{app}"

[Icons]
Name: "{group}\Mega Desktop"; Filename: "{app}\MegaDesktop.exe";
Name: "{group}\Mega Sync"; Filename: "{app}\MegaSync.exe";
;Name: "{group}\Uninstall MegaDesktop v0.7b"; Filename: "{uninstallexe}";

Name: "{commondesktop}\Mega Desktop"; Filename: "{app}\MegaDesktop.exe";
Name: "{commondesktop}\Mega Sync"; Filename: "{app}\MegaSync.exe";

[Code]
var
  ErrorCode: Integer;

procedure BeforeInstall();
begin
  if IsWin64 then
  begin
    ShellExec('', 'msiexec',
      ExpandConstant('/I "{tmp}\Synchronization-v2.1-x64-ENU.msi" /qn'),
      '', SW_SHOWNORMAL, ewWaitUntilTerminated, ErrorCode);
    ShellExec('', 'msiexec',
      ExpandConstant('/I "{tmp}\ProviderServices-v2.1-x64-ENU.msi" /qn'),
      '', SW_SHOWNORMAL, ewWaitUntilTerminated, ErrorCode);
  end else
  begin
    ShellExec('', 'msiexec',
      ExpandConstant('/I "{tmp}\Synchronization-v2.1-x86-ENU.msi" /qn'),
      '', SW_SHOWNORMAL, ewWaitUntilTerminated, ErrorCode);
    ShellExec('', 'msiexec',
      ExpandConstant('/I "{tmp}\ProviderServices-v2.1-x86-ENU.msi" /qn'),
      '', SW_SHOWNORMAL, ewWaitUntilTerminated, ErrorCode);
  end;
end;
