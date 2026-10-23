; LicenseManager — OWNER-ONLY installer (Stone's IT Consulting's own PC). Built by: .\build.ps1 -Installer
; Never give this file to a customer. It contains no key: the keys are created on the first start and kept
; (encrypted with the owner's password) in %LOCALAPPDATA%\StonesHotspot.LicenseManager, never in the install folder.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\owner"
#endif

[Setup]
AppId={{A3D0E9B4-2C77-4B1E-8F42-7E5C3A9D1B60}
AppName=Stone's Hotspot LicenseManager
AppVersion={#AppVersion}
AppPublisher=Stone's IT Consulting
DefaultDirName={autopf}\Stone's Hotspot LicenseManager
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=LicenseManager-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\LicenseManager\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\docs\licensing.md"; DestDir: "{app}\docs"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Stone's Hotspot LicenseManager"; Filename: "{app}\StonesHotspot.LicenseManager.exe"
Name: "{autodesktop}\LicenseManager"; Filename: "{app}\StonesHotspot.LicenseManager.exe"
