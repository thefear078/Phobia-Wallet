; Inno Setup script for Phobia Wallet (desktop; formerly Umbrella Wallet).
; Produces a Windows installer that lets the user choose the install folder,
; create a desktop / Start-menu shortcut, and uninstall cleanly.

#define AppName "Phobia Wallet"
#define AppPublisher "the fear"
#define AppExe "Phobia.exe"

; SourceDir = the published `app` folder; DistDir = where the installer is written.
; Both default to the local D:\umbrella-dist layout but can be overridden on CI, e.g.
;   ISCC /DSourceDir=dist\app /DDistDir=dist desktop\installer\phobia.iss
#ifndef SourceDir
  #define SourceDir "D:\umbrella-dist\app"
#endif
#ifndef DistDir
  #define DistDir "D:\umbrella-dist"
#endif

; The version is PASSED IN by release-windows.ps1 (ISCC /DAppVersion=...), which reads it from the
; .csproj — the single source of truth. It used to be a literal here that had to be kept in sync by
; hand, and it drifted: the 4.6.0 build produced an installer still named "Setup-4.5.0".
;
; The fallback reads it out of the published exe, so compiling the .iss directly still names the
; installer after the binary it actually contains rather than after a stale literal. Defined AFTER
; SourceDir because it reads the exe from there.
#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(SourceDir + "\" + AppExe)
#endif

#define AppUrl "https://t.me/UmbrellaWallet"
#define AppReleases "https://github.com/thefear078/UmbrellaWallet/releases"

[Setup]
; The AppId is the Umbrella-era one on purpose: it is what makes this an upgrade of the installed wallet
; rather than a second program beside it.
AppId={{7C1B0E2A-0B7E-4E9A-9C2E-UMBRELLA0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#AppReleases}
AppContact={#AppUrl}
AppComments=Self-custody, non-custodial crypto wallet. Your keys are generated and encrypted on this device and never leave it.
VersionInfoDescription={#AppName} — self-custody crypto wallet
VersionInfoProductName={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
DefaultDirName={autopf}\Phobia Wallet
DefaultGroupName=Phobia Wallet
DisableProgramGroupPage=no
AllowNoIcons=yes
; This is what gives the "choose where to install" page:
DisableDirPage=no
; Show the licence so a first-time downloader sees the terms before installing.
LicenseFile=..\..\LICENSE
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir={#DistDir}
; The release attaches this file under its Umbrella-era name too, for copies from before the rename.
OutputBaseFilename=PhobiaWallet-Setup-{#AppVersion}
SetupIconFile=..\src\Umbrella.Wallet.App\Assets\phobia.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
; Excludes: never ship a runtime `data` folder — that would overwrite the user's wallets/settings on
; update. The user's data lives in {app}\data (portable) or %APPDATA%\UmbrellaWallet and is never touched.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "data\*,data"; Flags: recursesubdirs createallsubdirs ignoreversion

[InstallDelete]
; An upgrade from Umbrella Wallet: its shortcuts go, so the Start menu and desktop show one wallet.
Type: files; Name: "{group}\Umbrella Wallet.lnk"
Type: files; Name: "{group}\Uninstall Umbrella Wallet.lnk"
Type: files; Name: "{autodesktop}\Umbrella Wallet.lnk"
; ...and its program: the wallet is Phobia.exe now, so the old exe would only be a second, stale copy.
Type: files; Name: "{app}\Umbrella.exe"

[Icons]
Name: "{group}\Phobia Wallet"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall Phobia Wallet"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Phobia Wallet"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch Phobia Wallet"; Flags: nowait postinstall skipifsilent

[Code]
// A wallet must never silently destroy funds on uninstall. We deliberately leave the encrypted
// vault in place (so a reinstall restores everything) and tell the user exactly where it is and
// how to erase it themselves if they truly want to.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    MsgBox('Phobia Wallet has been removed.' + #13#10 + #13#10 +
      'Your wallet data was NOT deleted — it stays encrypted on this PC in one of:' + #13#10 +
      '   • ' + ExpandConstant('{app}\data') + #13#10 +
      '   • ' + ExpandConstant('{userappdata}\UmbrellaWallet') + #13#10 + #13#10 +
      'Reinstalling restores your wallets, theme and settings automatically.' + #13#10 + #13#10 +
      'To erase everything, delete that folder yourself — but first make sure your 24-word ' +
      'recovery phrase is written down, or the funds in that wallet are lost forever.',
      mbInformation, MB_OK);
  end;
end;
