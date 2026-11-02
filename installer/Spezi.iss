; Inno Setup 7.1 or newer. The payload is the current self-contained dist folder.
#ifndef ProjectRoot
  #define ProjectRoot AddBackslash(SourcePath) + ".."
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifdef TestBuild
  #define AppName "Spezi Installer Test"
  #define AppId "{7C485931-704A-4C13-8B3F-9AB00E2D77E7}"
  #define AppMutexName "Local\Spezi.InstallerTest.SingleInstance"
  #define SetupMutexName "Local\Spezi.InstallerTest.Setup"
#else
  #define AppName "Spezi"
  #define AppId "{B08E4FA2-81F2-4CCA-A134-850D3A74519C}"
  #define AppMutexName "Local\Spezi.SingleInstance"
  #define SetupMutexName "Local\Spezi.Setup"
#endif
#define PayloadDir AddBackslash(ProjectRoot) + "dist"

[Setup]
AppId={{#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Spezi
AppComments=Gameplay clips and full recordings with game audio and voice chat.
#ifdef UpdateOnly
VersionInfoDescription=Spezi Updater
AppVerName=Spezi Update {#AppVersion}
DefaultDirName={code:GetUpdateDirectory}
Uninstallable=IsRegisteredUpdate
CreateAppDir=yes
DirExistsWarning=no
#else
VersionInfoDescription=Spezi Setup
DefaultDirName={localappdata}\Programs\{#AppName}
#endif
VersionInfoVersion={#AppVersion}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\Spezi.exe
AppMutex={#AppMutexName}
SetupMutex={#SetupMutexName}
CloseApplications=no
RestartApplications=no
DisableWelcomePage=no
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
WizardStyle=modern dark
WizardSizePercent=110
SetupIconFile={#ProjectRoot}\src\Spezi.App\Assets\Spezi.ico
Compression=lzma2/max
SolidCompression=yes
OutputDir=.
#ifdef UpdateOnly
OutputBaseFilename=Spezi-Updater
#else
OutputBaseFilename=Spezi-Setup
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
#ifdef UpdateOnly
WelcomeLabel1=Update Spezi to {#AppVersion}
WelcomeLabel2=Update an existing Spezi installation or portable folder.%n%nFinish any recording and exit Spezi from the system tray first. Your clips, audio configuration and other preferences will be kept.%n%nSelect the existing Spezi program folder on the next page. This updater works offline.
SelectDirLabel3=Select the existing folder that contains Spezi.exe. Your clips and preferences will be kept.
SelectDirBrowseLabel=Choose the installed or portable Spezi folder, then click Next.
ButtonInstall=&Update
FinishedLabel=Spezi has been updated to {#AppVersion}.%n%nYour clips and preferences have been kept. Audio and recording options are available in Settings.
#else
WelcomeLabel1=Install Spezi
WelcomeLabel2=Save the last 15 seconds to 5 minutes of your game or record a full session with game audio and voice chat.%n%nSetup installs Spezi for your Windows account. No separate .NET installation is required.%n%nConfigure recording, audio and hotkeys in Settings after opening Spezi.
FinishedLabel=Spezi has been installed.%n%nRecording and audio options are available in Settings. Your clips and preferences will be kept if you uninstall Spezi later.
#endif
SetupAppRunningError=Spezi is still running.%n%nFinish any recording and exit Spezi from the system tray. Then click OK to continue or Cancel to stop setup.
UninstallAppRunningError=Spezi is still running.%n%nFinish any recording and exit Spezi from the system tray before continuing the uninstall.

[CustomMessages]
CloseSpezi=Spezi is still running. Finish any recording and exit Spezi from the system tray, then run setup again.
DesktopShortcut=Create a desktop shortcut
LaunchSpezi=Launch Spezi now

#ifndef UpdateOnly
[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "Shortcuts:"; Flags: unchecked
#endif

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

#ifndef UpdateOnly
[Icons]
Name: "{autoprograms}\{#AppName}\Spezi"; Filename: "{app}\Spezi.exe"; WorkingDir: "{app}"; AppUserModelID: "Spezi.App"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Spezi.exe"; WorkingDir: "{app}"; Tasks: desktopicon; AppUserModelID: "Spezi.App"
#endif

[Run]
Filename: "{app}\Spezi.exe"; Description: "{cm:LaunchSpezi}"; Flags: nowait postinstall skipifsilent

[Code]
#ifdef UpdateOnly
function NormalizeDirectory(Value: String): String;
begin
  if Copy(Value, 1, 8) = '\\?\UNC\' then
    Value := '\\' + Copy(Value, 9, MaxInt)
  else if Copy(Value, 1, 4) = '\\?\' then
    Value := Copy(Value, 5, MaxInt);
  Result := RemoveBackslashUnlessRoot(ExpandFileName(Value));
end;

function RegisteredDirectory: String;
var
  Directory: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1', 'InstallLocation', Directory) then
  begin
    Directory := NormalizeDirectory(Directory);
    if FileExists(AddBackslash(Directory) + 'Spezi.exe') then
      Result := Directory;
  end;
end;

function GetUpdateDirectory(Param: String): String;
var
  Directory: String;
begin
  Result := RegisteredDirectory;
  if Result <> '' then Exit;
  Directory := ExpandConstant('{src}\..\dist');
  if FileExists(AddBackslash(Directory) + 'Spezi.exe') then
    Result := NormalizeDirectory(Directory)
  else
    Result := ExpandConstant('{localappdata}\Programs\{#AppName}');
end;

function IsRegisteredUpdate: Boolean;
var
  Directory: String;
begin
  Directory := RegisteredDirectory;
  Result := (Directory <> '') and (CompareText(Directory, NormalizeDirectory(ExpandConstant('{app}'))) = 0);
end;

function PackageVersion: Int64;
var
  Text: String;
  Parts: array[0..3] of Word;
  I, Dot: Integer;
begin
  Text := '{#AppVersion}';
  for I := 0 to 3 do
  begin
    Dot := Pos('.', Text);
    if Dot = 0 then
    begin
      Parts[I] := StrToIntDef(Text, 0);
      Text := '';
    end
    else
    begin
      Parts[I] := StrToIntDef(Copy(Text, 1, Dot - 1), 0);
      Delete(Text, 1, Dot);
    end;
  end;
  Result := PackVersionComponents(Parts[0], Parts[1], Parts[2], Parts[3]);
end;

function ValidateUpdateDirectory(Directory: String): String;
var
  CurrentVersion: Int64;
begin
  Result := '';
  Directory := AddBackslash(NormalizeDirectory(Directory));
  Log('Validating existing Spezi folder: ' + Directory);
  if not FileExists(Directory + 'Spezi.exe') or not FileExists(Directory + 'Spezi.dll') or
     not FileExists(Directory + 'Spezi.runtimeconfig.json') or not GetPackedVersion(Directory + 'Spezi.exe', CurrentVersion) then
    Result := 'No existing Spezi program was found in this folder. Select the folder containing Spezi.exe, or use Spezi-Setup.exe for a new installation.'
  else if ComparePackedVersion(CurrentVersion, PackageVersion) > 0 then
    Result := 'This folder contains a newer Spezi version. The updater will not downgrade it.';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Error: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    Error := ValidateUpdateDirectory(WizardDirValue);
    if Error <> '' then
    begin
      Log(Error);
      if not WizardSilent then MsgBox(Error, mbError, MB_OK);
      Result := False;
    end;
  end;
end;
#endif

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CheckForMutexes('{#AppMutexName}') then
    Result := CustomMessage('CloseSpezi');
#ifdef UpdateOnly
  if Result = '' then Result := ValidateUpdateDirectory(ExpandConstant('{app}'));
#endif
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
  Executable: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Only remove a startup entry that belongs to this installation.
    // Saved clips, preferences, caches and recovery data are never deleted.
    if RegQueryStringValue(HKCU64, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Spezi', Command) then
    begin
      Executable := ExpandConstant('{app}\Spezi.exe');
      if Copy(Executable, 1, 8) = '\\?\UNC\' then
        Executable := '\\' + Copy(Executable, 9, MaxInt)
      else if Copy(Executable, 1, 4) = '\\?\' then
        Executable := Copy(Executable, 5, MaxInt);
      Command := Trim(Command);
      if Copy(Command, 1, 9) = '"\\?\UNC\' then
        Command := '"\\' + Copy(Command, 10, MaxInt)
      else if Copy(Command, 1, 5) = '"\\?\' then
        Delete(Command, 2, 4);
      if CompareText(Command, '"' + Executable + '" --background') = 0 then
      begin
        if RegDeleteValue(HKCU64, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Spezi') then
          Log('Removed the startup entry owned by this Spezi installation.');
      end
      else
        Log('Retained the startup entry of another Spezi installation.');
    end;
  end;
end;
