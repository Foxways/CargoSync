; ============================================================================
;  CargoSync - Inno Setup installer script
;  Builds a single CargoSync-Setup.exe that lets the user install anywhere,
;  shows the Service Agreement / MIT license, and creates shortcuts.
;
;  Build:  publish the app first (see Installer\build-installer.ps1), then:
;          "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" CargoSync.iss
; ============================================================================

#define MyAppName "CargoSync"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Kishan Manohar"
#define MyAppContact "kishanmanohar@gmail.com"
#define MyAppExeName "CargoSync.exe"
; Published, self-contained app folder (created by build-installer.ps1)
#define PubDir "..\bin\publish"

[Setup]
; A unique AppId keeps CargoSync distinct from every other product in Add/Remove Programs.
AppId={{8F3C2A91-5E47-4B6D-9C0A-CA52D9F0C7B1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppContact={#MyAppContact}
AppPublisherURL=mailto:{#MyAppContact}
AppSupportURL=mailto:{#MyAppContact}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoVersion={#MyAppVersion}

; Let the user install to ANY folder. Default is a per-user, writable location so the
; app's local database (logins, credentials) can be written without administrator rights.
DefaultDirName={autopf}\{#MyAppName}
DisableDirPage=no
UsePreviousAppDir=yes
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=no
AllowNoIcons=yes

; Per-user install (no admin prompt). Works for the writable default location.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Service Agreement / MIT license shown on its own "I accept" page.
LicenseFile=LICENSE.txt

; Output
OutputDir=.\Output
OutputBaseFilename=CargoSync-Setup
SetupIconFile={#PubDir}\appicon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; Everything from the published, self-contained folder. The shipped data.db is only a
; read-only seed - on first run the app copies it to %AppData%\OrganizationImportTool\data.db
; (a safe, per-user, writable location), so the user's real database is never in Program Files
; and is never touched by re-installs or uninstalls.
Source: "{#PubDir}\*"; DestDir: "{app}"; \
    Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall skipifsilent
