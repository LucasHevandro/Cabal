; Instalador do cliente (Inno Setup 6). Gerar com: powershell -File Tools/instalador/gerar-instalador.ps1
; Instala na pasta do usuário (sem pedir administrador), cria os atalhos e aparece em "Adicionar ou remover programas".
; Os atalhos abrem o launcher, que mantém o jogo atualizado (Tools/launcher/publicar.ps1 publica as versões novas).

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define BuildDir "..\..\Client\Builds\Windows"
#define LauncherExe "..\..\artifacts\bin\Game.Launcher\release\TerrasDeAstralisLauncher.exe"

[Setup]
; Não mude o AppId: é por ele que o Windows reconhece que a versão nova substitui a antiga.
AppId={{6E3C2B1A-7F4D-4E8B-9A2C-5D1F0B7A3E91}
AppName=Terras de Astralis
AppVersion={#AppVersion}
AppPublisher=Astralis
DefaultDirName={autopf}\Terras de Astralis
DefaultGroupName=Terras de Astralis
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\Client\Builds
OutputBaseFilename=InstalarTerrasDeAstralis
SetupIconFile=icone.ico
UninstallDisplayIcon={app}\TerrasDeAstralisLauncher.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
VersionInfoVersion=1.0.0.0
VersionInfoProductTextVersion={#AppVersion}

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Excludes: "*_BackUpThisFolder_*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#LauncherExe}"; DestDir: "{app}"; Flags: ignoreversion

[UninstallDelete]
; O que o launcher cria ou baixa depois da instalação.
Type: files; Name: "{app}\versao-instalada.json"
Type: files; Name: "{app}\launcher.log"
Type: files; Name: "{app}\*.antigo"
Type: files; Name: "{app}\*.baixando"
Type: filesandordirs; Name: "{app}\TerrasDeAstralis_Data"

[Icons]
Name: "{group}\Terras de Astralis"; Filename: "{app}\TerrasDeAstralisLauncher.exe"
Name: "{group}\Desinstalar Terras de Astralis"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Terras de Astralis"; Filename: "{app}\TerrasDeAstralisLauncher.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TerrasDeAstralisLauncher.exe"; Description: "{cm:LaunchProgram,Terras de Astralis}"; Flags: nowait postinstall skipifsilent
