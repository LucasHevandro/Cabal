; Instalador do cliente (Inno Setup 6). Gerar com: powershell -File Tools/instalador/gerar-instalador.ps1
; Instala na pasta do usuário (sem pedir administrador), cria os atalhos e aparece em "Adicionar ou remover programas".
; Rodar uma versão nova por cima atualiza o jogo.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#define BuildDir "..\..\Client\Builds\Windows"

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
UninstallDisplayIcon={app}\TerrasDeAstralis.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Excludes: "*_BackUpThisFolder_*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Terras de Astralis"; Filename: "{app}\TerrasDeAstralis.exe"
Name: "{group}\Desinstalar Terras de Astralis"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Terras de Astralis"; Filename: "{app}\TerrasDeAstralis.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TerrasDeAstralis.exe"; Description: "{cm:LaunchProgram,Terras de Astralis}"; Flags: nowait postinstall skipifsilent
