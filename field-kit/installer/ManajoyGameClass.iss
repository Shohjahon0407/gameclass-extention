; Manajoy GameClass — Windows installer (Inno Setup 6).
;
; Built by field-kit/build-field-kit.sh (ISCC under Wine on Linux, or ISCC.exe on Windows):
;   iscc /DAppVersion=0.7.0 /DSourceDir=<publish folder> ManajoyGameClass.iss
;
; What it does: the usual wizard (language, terms and privacy, folder, shortcuts), copies the app folder,
; records the folder in HKLM32\Software\Manajoy\GameClassBridge and opens the app, whose own window then
; connects the GameClass database, registers the Windows service and pairs with Manajoy.
; Updating over an installed bridge stops its service first and starts it again afterwards; the login,
; secrets and pairing in the folder are kept. Uninstall removes the service, files, secrets and logs.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\installer-files"
#endif
#define AppName "Manajoy GameClass"
#define AppExe "Manajoy.GameClass.Bridge.exe"
#define ServiceName "ManajoyGameClassBridge"
#define InstallerKey "Software\Manajoy\GameClassBridge"

[Setup]
AppId={{8F3C2A51-6D4B-4E7A-9C1F-3B2E5D7A9C41}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Manajoy
AppPublisherURL=https://www.manajoy.uz
AppSupportURL=https://www.manajoy.uz
AppUpdatesURL=https://www.manajoy.uz
AppCopyright=Copyright © 2026 Manajoy
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany=Manajoy
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=Copyright © 2026 Manajoy
; The folder an older (pre-installer) bridge service runs from, otherwise Program Files\Manajoy\GameClassBridge.
DefaultDirName={code:DefaultInstallDir}
UsePreviousAppDir=yes
UsePreviousTasks=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.14393
WizardStyle=modern
; The branded welcome page with the wizard picture (Inno hides it by default).
DisableWelcomePage=no
WizardImageFile=images\wizard-100.bmp,images\wizard-125.bmp,images\wizard-150.bmp,images\wizard-200.bmp
WizardSmallImageFile=images\small-100.bmp,images\small-125.bmp,images\small-150.bmp,images\small-200.bmp
SetupIconFile=..\..\src\Manajoy.GameClass.Bridge\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\out
OutputBaseFilename=ManajoyGameClass-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
; A separate 64-bit compressor process: the 64 MB dictionary does not fit the 32-bit compiler (also under Wine).
LZMAUseSeparateProcess=yes
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
SetupLogging=yes

[Languages]
Name: "uz"; MessagesFile: "lang\Uzbek.isl"; LicenseFile: "text\terms-uz.txt"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"; LicenseFile: "text\terms-ru.txt"
Name: "en"; MessagesFile: "compiler:Default.isl"; LicenseFile: "text\terms-en.txt"

[Messages]
uz.WizardLicense=Shartlar va maxfiylik
ru.WizardLicense=Условия и конфиденциальность
en.WizardLicense=Terms and privacy
uz.FinishedLabel=[name] kompyuteringizga o'rnatildi.%n%nKeyingi qadam — dastur oynasida GameClass bazasiga va Manajoy'ga ulash (taxminan 5 daqiqa). Kerak bo'lganda oynani «Manajoy GameClass» yorlig'idan ochasiz.
ru.FinishedLabel=[name] установлен на ваш компьютер.%n%nДальше — в окне программы подключить базу GameClass и Manajoy (около 5 минут). Позже окно открывается ярлыком «Manajoy GameClass».
en.FinishedLabel=[name] is installed on your computer.%n%nNext, the program window connects the GameClass database and Manajoy (about 5 minutes). Later, open the window with the "Manajoy GameClass" shortcut.

[CustomMessages]
uz.LaunchAfter=Manajoy GameClass'ni ochish va sozlashni boshlash
ru.LaunchAfter=Открыть Manajoy GameClass и начать настройку
en.LaunchAfter=Open Manajoy GameClass and start the setup
uz.StoppingService=Ishlab turgan bridge xizmati to'xtatilmoqda...
ru.StoppingService=Остановка работающей службы bridge...
en.StoppingService=Stopping the running bridge service...
uz.AutoStartTask=Kompyuter yoqilganda Manajoy GameClass panelini &ochish
ru.AutoStartTask=&Открывать панель Manajoy GameClass при включении компьютера
en.AutoStartTask=&Open the Manajoy GameClass panel when the PC starts
uz.AutoStartGroup=Avtomatik ishga tushirish (bridge xizmatining o'zi har doim avtomatik ishlaydi):
ru.AutoStartGroup=Автозапуск (сама служба bridge запускается автоматически всегда):
en.AutoStartGroup=Autostart (the bridge service itself always starts automatically):

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "{cm:AutoStartTask}"; GroupDescription: "{cm:AutoStartGroup}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "appsettings.json,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
; The owner may have edited it: an update keeps the existing one.
Source: "{#SourceDir}\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; The app reads this to know the installer owns the files and the shortcuts (SetupEnvironment.InstallerDir).
Root: HKLM32; Subkey: "{#InstallerKey}"; ValueType: string; ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKLM32; Subkey: "{#InstallerKey}"; ValueType: string; ValueName: "Version"; ValueData: "{#AppVersion}"

[Run]
; A sign-in task opens the panel without a UAC prompt (a Run key cannot start an exe that needs admin rights).
Filename: "{app}\{#AppExe}"; Parameters: "--autostart-on"; Flags: runhidden waituntilterminated; Tasks: autostart; Check: ApplyAutoStart
Filename: "{app}\{#AppExe}"; Parameters: "--autostart-off"; Flags: runhidden waituntilterminated; Tasks: not autostart; Check: ApplyAutoStart
; shellexec: the app asks for administrator rights in its manifest.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchAfter}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; Stops and deletes the Windows service before the files go (the read-only SQL login stays; the app's own
; "Uninstall" button can drop it with the sa password).
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall-service"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveService"

[UninstallDelete]
; Only what the app itself creates in its folder (never the whole folder: it could be a shared one).
Type: files; Name: "{app}\*.secret"
Type: files; Name: "{app}\manajoy.json"
Type: files; Name: "{app}\layout.json"
Type: files; Name: "{app}\appsettings.json"
Type: files; Name: "{app}\*.new"
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{app}\output"
Type: dirifempty; Name: "{app}"
Type: filesandordirs; Name: "{commonappdata}\Manajoy\GameClassBridge"
Type: dirifempty; Name: "{commonappdata}\Manajoy"

[Code]
const
  ServiceKey = 'SYSTEM\CurrentControlSet\Services\{#ServiceName}';

var
  ServiceWasRunning: Boolean;

function ApplyAutoStart: Boolean;
begin
  { Online updates preserve the existing sign-in task, even after an older manual install. }
  Result := ExpandConstant('{param:AUTOUPDATE|0}') <> '1';
end;

function VersionPart(var Value: String): Integer;
var P: Integer; Part: String;
begin
  P := Pos('.', Value);
  if P = 0 then begin Part := Value; Value := ''; end
  else begin Part := Copy(Value, 1, P - 1); Delete(Value, 1, P); end;
  Result := StrToIntDef(Part, 0);
end;

function InitializeSetup: Boolean;
var Existing, Incoming: String; A, B, I: Integer;
begin
  Result := True;
  if RegQueryStringValue(HKLM32, '{#InstallerKey}', 'Version', Existing) then
  begin
    Incoming := '{#AppVersion}';
    for I := 1 to 4 do
    begin
      A := VersionPart(Existing); B := VersionPart(Incoming);
      if A < B then Exit;
      if A > B then
      begin
        SuppressibleMsgBox('A newer Manajoy GameClass version is already installed. Open it from the desktop shortcut.', mbError, MB_OK, IDOK);
        Result := False; Exit;
      end;
    end;
  end;
end;

function ServiceExists: Boolean;
begin
  Result := RegKeyExists(HKLM, ServiceKey);
end;

{ "C:\Program Files\Manajoy\GameClassBridge\Manajoy.GameClass.Bridge.exe" -> its folder, or '' }
function ServiceFolder: String;
var
  ImagePath: String;
  P: Integer;
begin
  Result := '';
  if not RegQueryStringValue(HKLM, ServiceKey, 'ImagePath', ImagePath) then
    Exit;
  ImagePath := Trim(ImagePath);
  if Copy(ImagePath, 1, 1) = '"' then
  begin
    Delete(ImagePath, 1, 1);
    P := Pos('"', ImagePath);
    if P > 0 then
      ImagePath := Copy(ImagePath, 1, P - 1);
  end;
  Result := ExtractFileDir(ImagePath);
end;

function DefaultInstallDir(Param: String): String;
begin
  Result := ServiceFolder;
  if Result = '' then
    Result := ExpandConstant('{autopf}\Manajoy\GameClassBridge');
end;

{ An installed bridge service keeps its folder: the folder page is not offered then. }
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = wpSelectDir) and (ServiceFolder <> '');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  ServiceWasRunning := False;
  if ServiceExists then
  begin
    WizardForm.PreparingLabel.Caption := CustomMessage('StoppingService');
    { net stop waits until the service has stopped; exit code 2 = it was not running. }
    if Exec(ExpandConstant('{sys}\net.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      ServiceWasRunning := (ResultCode = 0);
    Sleep(1500);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and ServiceWasRunning then
    Exec(ExpandConstant('{sys}\net.exe'), 'start {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
