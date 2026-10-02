; GLB invest Agent - installateur Windows (Inno Setup 6.3 ou plus recent).
; Compile par le workflow de release :
;   iscc /DAppVersion=1.2.3 /DSiteUrl=https://... /DSourceExe=...\cc-agent-win-x64.exe setup.iss
; Le code d appairage est lu dans le nom du fichier : ...installer-ABCD-EFGH.exe

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SiteUrl
  #define SiteUrl ""
#endif
#ifndef SourceExe
  #define SourceExe "..\..\out\win-x64\cc-agent.exe"
#endif
#define AppName "GLB invest Agent"
#define ServiceName "CryptoCryptAgent"

[Setup]
AppId={{8F4C2A7E-5B1D-4E3A-9C6F-2D7B1A0E4F93}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=GLB invest
AppPublisherURL=https://github.com/shineminder/agent-auto-market
AppSupportURL=https://github.com/shineminder/agent-auto-market/issues
VersionInfoVersion={#AppVersion}
DefaultDirName={commonpf64}\CryptoCryptAgent
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyMemo=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=agent-setup-windows-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\cc-agent.exe
CloseApplications=no
SetupLogging=yes

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "cc-agent.exe"; Flags: ignoreversion

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\CryptoCryptAgent"
Type: filesandordirs; Name: "{app}"

[CustomMessages]
fr.PageCompteTitre=Connexion à votre compte
fr.PageCompteDesc=Ces informations relient cet ordinateur à votre compte {#AppName}.
fr.ChampSite=Adresse de votre site (ex. https://exemple.com) :
fr.ChampCode=Code d'appairage (page Mes accès, valable 10 minutes) :
fr.PageCleTitre=Votre clé Coinbase
fr.PageCleDesc=Elle reste sur cet ordinateur, chiffrée, et n'est jamais envoyée au site.
fr.ChampCle=Fichier cdp_api_key.json (laisser vide pour l'ajouter plus tard) :
fr.FiltreCle=Clé Coinbase (*.json)|*.json|Tous les fichiers|*.*
fr.SupprimerCle=Supprimer le fichier téléchargé après l'installation (recommandé)
fr.ErreurSite=L'adresse du site doit commencer par https://
fr.ErreurCode=Indiquez le code d'appairage affiché sur la page Mes accès de votre site.
fr.ErreurCle=Fichier de clé introuvable. Choisissez le fichier cdp_api_key.json téléchargé chez Coinbase.
fr.EtapeService=Installation du service...
fr.EtapeAppairage=Appairage de cet ordinateur...
fr.EtapeCle=Vérification et enregistrement de la clé Coinbase...
fr.DescriptionService=Exécute les ordres que vous avez autorisés, avec votre clé, dans vos plafonds.
fr.EchecAppairage=L'appairage a été refusé. Générez un nouveau code sur la page Mes accès de votre site, puis relancez cet installateur.%n%nDétail : %1
fr.EchecCle=La clé Coinbase a été refusée. Vérifiez qu'elle a les droits Consulter et Négocier, et l'algorithme ECDSA.%n%nDétail : %1
fr.Termine=L'agent est installé et démarre avec Windows.%n%nRetournez sur votre site : la page Mes accès doit indiquer que l'agent est connecté.
fr.TermineAvecAlerte=L'agent est installé, mais une étape demande votre attention :%n%n%1
fr.Desinstalle=L'agent a été désinstallé.%n%nPensez à révoquer cet appareil sur la page Mes accès de votre site et à supprimer la clé chez Coinbase.
en.PageCompteTitre=Connect to your account
en.PageCompteDesc=This links this computer to your {#AppName} account.
en.ChampSite=Your site address (e.g. https://example.com):
en.ChampCode=Pairing code (Access page, valid for 10 minutes):
en.PageCleTitre=Your Coinbase key
en.PageCleDesc=It stays on this computer, encrypted, and is never sent to the site.
en.ChampCle=cdp_api_key.json file (leave empty to add it later):
en.FiltreCle=Coinbase key (*.json)|*.json|All files|*.*
en.SupprimerCle=Delete the downloaded file after installation (recommended)
en.ErreurSite=The site address must start with https://
en.ErreurCode=Enter the pairing code shown on the Access page of your site.
en.ErreurCle=Key file not found. Choose the cdp_api_key.json file downloaded from Coinbase.
en.EtapeService=Installing the service...
en.EtapeAppairage=Pairing this computer...
en.EtapeCle=Checking and saving the Coinbase key...
en.DescriptionService=Executes the orders you authorized, with your key, within your limits.
en.EchecAppairage=Pairing was refused. Generate a new code on the Access page of your site, then run this installer again.%n%nDetails: %1
en.EchecCle=The Coinbase key was refused. Check that it has the View and Trade permissions and the ECDSA algorithm.%n%nDetails: %1
en.Termine=The agent is installed and starts with Windows.%n%nGo back to your site: the Access page should show the agent as connected.
en.TermineAvecAlerte=The agent is installed, but one step needs your attention:%n%n%1
en.Desinstalle=The agent has been uninstalled.%n%nRemember to revoke this device on the Access page of your site and to delete the key on Coinbase.

[Code]
var
  PageCompte: TInputQueryWizardPage;
  PageCle: TInputFileWizardPage;
  CaseSupprimer: TNewCheckBox;
  CodeAuto, Alertes: String;

function DejaInstalle(): Boolean;
begin
  Result := FileExists(ExpandConstant('{commonappdata}\CryptoCryptAgent\config.json'));
end;

{ Code d appairage lu dans le nom du fichier : ...installer-ABCD-EFGH.exe, ou ...installer-ABCD-EFGH (1).exe }
function CodeDepuisNomFichier(): String;
var
  Nom: String;
  P: Integer;
begin
  Result := '';
  Nom := ChangeFileExt(ExtractFileName(ExpandConstant('{srcexe}')), '');
  P := Pos(' (', Nom);
  if P > 0 then Nom := Copy(Nom, 1, P - 1);
  P := Pos('installer-', Lowercase(Nom));
  if P > 0 then Result := Uppercase(Trim(Copy(Nom, P + 10, Length(Nom))));
end;

function ServiceExiste(): Boolean;
var
  Code: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

procedure Systeme(const Fichier, Args: String);
var
  Code: Integer;
begin
  Exec(Fichier, Args, '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

{ Lance cc-agent et recupere son message, pour l afficher en cas d echec }
function Agent(const Args: String; var Sortie: String): Boolean;
var
  R: TExecOutput;
  Code, I: Integer;
begin
  Sortie := '';
  Result := ExecAndCaptureOutput(ExpandConstant('{app}\cc-agent.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code, R) and (Code = 0);
  for I := 0 to GetArrayLength(R.StdOut) - 1 do Sortie := Sortie + R.StdOut[I] + #13#10;
  for I := 0 to GetArrayLength(R.StdErr) - 1 do Sortie := Sortie + R.StdErr[I] + #13#10;
end;

procedure InitializeWizard();
var
  Defaut: String;
begin
  CodeAuto := CodeDepuisNomFichier();

  PageCompte := CreateInputQueryPage(wpWelcome, CustomMessage('PageCompteTitre'), CustomMessage('PageCompteDesc'), '');
  PageCompte.Add(CustomMessage('ChampSite'), False);
  PageCompte.Add(CustomMessage('ChampCode'), False);
  PageCompte.Values[0] := '{#SiteUrl}';
  PageCompte.Values[1] := CodeAuto;
  if '{#SiteUrl}' <> '' then
  begin
    PageCompte.PromptLabels[0].Visible := False;
    PageCompte.Edits[0].Visible := False;
  end;

  PageCle := CreateInputFilePage(PageCompte.ID, CustomMessage('PageCleTitre'), CustomMessage('PageCleDesc'), '');
  PageCle.Add(CustomMessage('ChampCle'), CustomMessage('FiltreCle'), '.json');
  Defaut := ExpandConstant('{%USERPROFILE}\Downloads\cdp_api_key.json');
  if FileExists(Defaut) then PageCle.Values[0] := Defaut;

  CaseSupprimer := TNewCheckBox.Create(PageCle);
  CaseSupprimer.Parent := PageCle.Surface;
  CaseSupprimer.Top := PageCle.Edits[0].Top + PageCle.Edits[0].Height + ScaleY(16);
  CaseSupprimer.Width := PageCle.SurfaceWidth;
  CaseSupprimer.Caption := CustomMessage('SupprimerCle');
  CaseSupprimer.Checked := True;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = PageCompte.ID) and ('{#SiteUrl}' <> '') and (CodeAuto <> '');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Fichier: String;
begin
  Result := True;
  if CurPageID = PageCompte.ID then
  begin
    if Copy(Lowercase(Trim(PageCompte.Values[0])), 1, 8) <> 'https://' then
    begin
      MsgBox(CustomMessage('ErreurSite'), mbError, MB_OK);
      Result := False;
    end
    else if (Trim(PageCompte.Values[1]) = '') and not DejaInstalle() then
    begin
      MsgBox(CustomMessage('ErreurCode'), mbError, MB_OK);
      Result := False;
    end;
  end;
  if CurPageID = PageCle.ID then
  begin
    Fichier := Trim(PageCle.Values[0]);
    if (Fichier <> '') and not FileExists(Fichier) then
    begin
      MsgBox(CustomMessage('ErreurCle'), mbError, MB_OK);
      Result := False;
    end;
  end;
end;

{ Mise a jour : arret du service avant le remplacement de l executable }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if ServiceExiste() then Systeme(ExpandConstant('{sys}\net.exe'), 'stop {#ServiceName}');
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Exe, Donnees, Compte, Sc, Sortie, CodeAppairage, Cle: String;
begin
  if CurStep <> ssPostInstall then Exit;
  Exe := ExpandConstant('{app}\cc-agent.exe');
  Donnees := ExpandConstant('{commonappdata}\CryptoCryptAgent');
  Compte := 'NT SERVICE\{#ServiceName}';
  Sc := ExpandConstant('{sys}\sc.exe');

  { Service sous compte virtuel, relance automatique en cas d arret, droits minimaux }
  WizardForm.StatusLabel.Caption := CustomMessage('EtapeService');
  ForceDirectories(Donnees);
  if not ServiceExiste() then
    Systeme(Sc, 'create {#ServiceName} binPath= "\"' + Exe + '\" run" start= auto DisplayName= "{#AppName}"');
  Systeme(Sc, 'description {#ServiceName} "' + CustomMessage('DescriptionService') + '"');
  Systeme(Sc, 'config {#ServiceName} obj= "' + Compte + '"');
  Systeme(Sc, 'failure {#ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/600000');
  Systeme(Sc, 'failureflag {#ServiceName} 1');
  Systeme(ExpandConstant('{sys}\icacls.exe'), '"' + Donnees + '" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F "' + Compte + ':(OI)(CI)M"');
  Systeme(ExpandConstant('{sys}\icacls.exe'), '"' + ExpandConstant('{app}') + '" /grant "' + Compte + ':(OI)(CI)M"');

  CodeAppairage := Trim(PageCompte.Values[1]);
  if CodeAppairage <> '' then
  begin
    WizardForm.StatusLabel.Caption := CustomMessage('EtapeAppairage');
    if not Agent('pair --code "' + CodeAppairage + '" --server "' + Trim(PageCompte.Values[0]) + '"', Sortie) then
      Alertes := Alertes + FmtMessage(CustomMessage('EchecAppairage'), [Trim(Sortie)]) + #13#10#13#10;
  end;

  Cle := Trim(PageCle.Values[0]);
  if Cle <> '' then
  begin
    WizardForm.StatusLabel.Caption := CustomMessage('EtapeCle');
    if Agent('key --file "' + Cle + '"', Sortie) then
    begin
      if CaseSupprimer.Checked then DeleteFile(Cle);
    end
    else
      Alertes := Alertes + FmtMessage(CustomMessage('EchecCle'), [Trim(Sortie)]) + #13#10#13#10;
  end;

  Systeme(ExpandConstant('{sys}\net.exe'), 'start {#ServiceName}');
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    if Alertes = '' then
      WizardForm.FinishedLabel.Caption := CustomMessage('Termine')
    else
      WizardForm.FinishedLabel.Caption := FmtMessage(CustomMessage('TermineAvecAlerte'), [Trim(Alertes)]);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    Systeme(ExpandConstant('{sys}\net.exe'), 'stop {#ServiceName}');
    Systeme(ExpandConstant('{sys}\sc.exe'), 'delete {#ServiceName}');
  end;
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() then
    MsgBox(CustomMessage('Desinstalle'), mbInformation, MB_OK);
end;
