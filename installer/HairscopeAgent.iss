; Inno Setup script for the Hairscope Agent.
; Installs the agent as an auto-starting Windows Service (runs as LocalSystem,
; which has the rights USBPcap needs). The service keeps a localhost WebSocket
; listener open and starts USB button capture only while the web app is connected.

#define AppName "Hairscope Agent"
#define AppVersion "1.0.8"
#define AppPublisher "Hairscope"
#define AppExeName "HairscopeAgent.exe"
#define ServiceName "HairscopeAgent"

[Setup]
AppId={{B7E2D9A1-5C64-4F0E-9A3D-2E6B1C8F4A70}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
; Stamp the Setup.exe's own version-info resource (otherwise File version = 0.0.0.0).
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\Hairscope\HairscopeAgent
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=Output
OutputBaseFilename=HairscopeAgentSetup-{#AppVersion}
SetupIconFile=..\src\HairscopeAgent\app.ico
; Logo shown top-right on the interior wizard pages (base + 2x for HiDPI).
WizardSmallImageFile=assets\wizard-small.bmp,assets\wizard-small-2x.bmp
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
; Show the standard "restart now / restart later" choice on the finish page
; (the USBPcap driver needs a reboot to attach for button support).
AlwaysRestart=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
; Custom finish-page text explaining why a restart is needed.
FinishedRestartLabel=Setup has finished installing the Hairscope Agent.%n%nA restart is required for the hardware capture button to work properly (the USBPcap driver attaches on reboot). Would you like to restart your computer now?
FinishedRestartMessage=A restart is required for the hardware capture button to work properly.%n%nWould you like to restart your computer now?

[Files]
Source: "..\src\HairscopeAgent\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs createallsubdirs ignoreversion
; Bundled USBPcap installer (signed by the USBPcap author). Extracted to a temp
; folder and run silently when USBPcap isn't already present.
Source: "deps\USBPcapSetup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Run]
; Install USBPcap first (only if not already installed) — needed for button capture.
Filename: "{tmp}\USBPcapSetup.exe"; Parameters: "/S"; Flags: waituntilterminated; Check: NeedsUsbPcap; StatusMsg: "Installing USBPcap driver (required for the capture button)..."
; Remove any previous instance of the service, then (re)create and start it.
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden; StatusMsg: "Stopping existing service..."
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden; StatusMsg: "Removing existing service..."
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""{app}\{#AppExeName}"" start= auto DisplayName= ""{#AppName}"""; Flags: runhidden; StatusMsg: "Registering service..."
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Detects the trichoscopy probe hardware button and relays it to the Hairscope web app."""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden; StatusMsg: "Starting service..."

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden; RunOnceId: "StopSvc"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden; RunOnceId: "DelSvc"

[Code]
function UsbPcapInstalled(): Boolean;
begin
  Result := FileExists(ExpandConstant('{pf}\USBPcap\USBPcapCMD.exe')) or
            FileExists('C:\Program Files\USBPcap\USBPcapCMD.exe');
end;

function NeedsUsbPcap(): Boolean;
begin
  Result := not UsbPcapInstalled();
end;
