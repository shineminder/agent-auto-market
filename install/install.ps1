#Requires -Version 5.1
<#
.SYNOPSIS
  Installe ou met a jour CryptoCrypt Agent (service Windows). Aucune question n est posee.
.EXAMPLE
  .\install.ps1 -Code ABCD-EFGH -CleCoinbase "$env:USERPROFILE\Downloads\cdp_api_key.json" -SupprimerFichierCle
.EXAMPLE
  .\install.ps1            # mise a jour d une installation existante
#>
[CmdletBinding()]
param(
    [string]$Code = '',
    [string]$CleCoinbase = '',
    [string]$Serveur = 'https://gaplab.fr',
    [string]$Depot = 'shineminder/agent-auto-market',
    [string]$Version = 'latest',
    [decimal]$PlafondOrdre = 100,
    [decimal]$PlafondMois = 500,
    [switch]$SupprimerFichierCle
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$NomService       = 'CryptoCryptAgent'
$DossierProgramme = Join-Path $env:ProgramFiles 'CryptoCryptAgent'
$DossierDonnees   = Join-Path $env:ProgramData 'CryptoCryptAgent'
$Exe              = Join-Path $DossierProgramme 'cc-agent.exe'
$Fichier          = 'cc-agent-win-x64.exe'
$ClePubliqueB64   = 'MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEVkGn/aLeGIhfx8XLyj9rGP/r8+iZM2DMM6lT/JOSqVYuJa9TYpSXMZv6R1bGBdqiD+/kN9HwJeETbhsO1Q2szw=='

function Etape($texte) { Write-Host "==> $texte" -ForegroundColor Cyan }
function Echec($texte) { Write-Host "ERREUR : $texte" -ForegroundColor Red; exit 1 }

# ---- Elevation automatique -------------------------------------------------------------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if (-not $PSCommandPath) { Echec 'Relancez PowerShell en tant qu administrateur.' }
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    foreach ($p in $PSBoundParameters.GetEnumerator()) {
        if ($p.Value -is [switch]) {
            if ($p.Value) { $arguments += "-$($p.Key)" }
        } else {
            $arguments += "-$($p.Key)"; $arguments += "`"$($p.Value)`""
        }
    }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait
    exit
}

if ($Depot -like '__*' -or $Serveur -like '__*') { Echec 'Script non configure (depot ou serveur).' }

# ---- Verification ECDSA P-256 de SHA256SUMS (signature DER produite par OpenSSL) ----------
function Test-SignatureRelease([byte[]]$Donnees, [byte[]]$Der, [string]$SpkiB64) {
    $spki = [Convert]::FromBase64String($SpkiB64)
    if ($spki.Length -ne 91 -or $spki[26] -ne 4) { throw 'Cle publique de release inattendue.' }
    $blob = New-Object byte[] 72
    [BitConverter]::GetBytes([uint32]0x31534345).CopyTo($blob, 0)
    [BitConverter]::GetBytes([uint32]32).CopyTo($blob, 4)
    [Array]::Copy($spki, 27, $blob, 8, 64)
    $cle = [Security.Cryptography.CngKey]::Import($blob, [Security.Cryptography.CngKeyBlobFormat]::EccPublicBlob)
    $ecdsa = New-Object Security.Cryptography.ECDsaCng($cle)
    $ecdsa.HashAlgorithm = [Security.Cryptography.CngAlgorithm]::Sha256
    $i = 2
    if ($Der[1] -band 0x80) { $i = 2 + ($Der[1] -band 0x7F) }
    $brute = New-Object byte[] 64
    foreach ($k in 0, 1) {
        if ($Der[$i] -ne 2) { throw 'Signature mal formee.' }
        $long = $Der[$i + 1]; $debut = $i + 2
        $val = @($Der[$debut..($debut + $long - 1)])
        while ($val.Length -gt 32 -and $val[0] -eq 0) { $val = @($val[1..($val.Length - 1)]) }
        if ($val.Length -gt 32) { throw 'Signature mal formee.' }
        [Array]::Copy([byte[]]$val, 0, $brute, $k * 32 + (32 - $val.Length), $val.Length)
        $i = $debut + $long
    }
    return $ecdsa.VerifyData($Donnees, $brute)
}

# ---- Telechargement et verifications ------------------------------------------------------
Etape 'Recherche de la version'
if ($Version -eq 'latest') {
    $rel = Invoke-RestMethod "https://api.github.com/repos/$Depot/releases/latest" -Headers @{ 'User-Agent' = 'cryptocrypt-agent-install' }
    $Version = $rel.tag_name.TrimStart('v')
}
$base = "https://github.com/$Depot/releases/download/v$Version"
$tmp = Join-Path $env:TEMP ('cc-agent-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
    Etape "Telechargement de la version $Version"
    foreach ($nom in 'SHA256SUMS', 'SHA256SUMS.sig', $Fichier) {
        Invoke-WebRequest "$base/$nom" -OutFile (Join-Path $tmp $nom) -UseBasicParsing
    }

    Etape 'Verification de la signature de la release'
    $sommes = [IO.File]::ReadAllBytes((Join-Path $tmp 'SHA256SUMS'))
    $sig = [IO.File]::ReadAllBytes((Join-Path $tmp 'SHA256SUMS.sig'))
    if (-not (Test-SignatureRelease $sommes $sig $ClePubliqueB64)) { Echec 'Signature de la release invalide : installation annulee.' }

    Etape 'Verification de l empreinte'
    $ligne = [Text.Encoding]::UTF8.GetString($sommes) -split "`n" | Where-Object { $_ -match "\s\*?$([regex]::Escape($Fichier))\s*$" } | Select-Object -First 1
    if (-not $ligne) { Echec "Empreinte absente pour $Fichier." }
    $attendu = ($ligne.Trim() -split '\s+')[0].ToLowerInvariant()
    $obtenu = (Get-FileHash (Join-Path $tmp $Fichier) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($attendu -ne $obtenu) { Echec 'Empreinte invalide : installation annulee.' }

    $auth = Get-AuthenticodeSignature (Join-Path $tmp $Fichier)
    if ($auth.Status -eq 'Valid') {
        Write-Host "    Signature Authenticode : $($auth.SignerCertificate.Subject)"
    } elseif ($auth.Status -ne 'NotSigned') {
        Echec "Signature Authenticode invalide ($($auth.Status))."
    }

    # ---- Installation -------------------------------------------------------------------
    Etape 'Installation du service'
    $service = Get-Service -Name $NomService -ErrorAction SilentlyContinue
    if ($service -and $service.Status -ne 'Stopped') { Stop-Service $NomService -Force; Start-Sleep -Seconds 2 }
    New-Item -ItemType Directory -Force -Path $DossierProgramme, $DossierDonnees | Out-Null
    Copy-Item (Join-Path $tmp $Fichier) $Exe -Force
    if (-not $service) {
        New-Service -Name $NomService -BinaryPathName "`"$Exe`" run" -DisplayName 'CryptoCrypt Agent' `
            -Description 'Execute les ordres que vous avez autorises, avec votre cle, dans vos plafonds.' -StartupType Automatic | Out-Null
    }
    sc.exe config $NomService obj= "NT SERVICE\$NomService" | Out-Null
    sc.exe failure $NomService reset= 86400 actions= restart/60000/restart/60000/restart/600000 | Out-Null
    sc.exe failureflag $NomService 1 | Out-Null

    Etape 'Droits d acces (moindre privilege)'
    $compte = "NT SERVICE\$NomService"
    icacls $DossierDonnees /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' "${compte}:(OI)(CI)M" | Out-Null
    if ($LASTEXITCODE -ne 0) { Echec 'Droits du dossier de donnees non appliques.' }
    icacls $DossierProgramme /grant "${compte}:(OI)(CI)M" | Out-Null

    # ---- Configuration --------------------------------------------------------------------
    Etape 'Configuration'
    if ($Code) {
        & $Exe pair --code $Code --server $Serveur
        if ($LASTEXITCODE -ne 0) { Echec 'Appairage refuse : generez un nouveau code sur le site (page Mes acces).' }
    }
    if ($CleCoinbase) {
        if (-not (Test-Path $CleCoinbase)) { Echec "Fichier de cle introuvable : $CleCoinbase" }
        & $Exe key --file $CleCoinbase
        if ($LASTEXITCODE -ne 0) { Echec 'Cle Coinbase refusee (droits Consulter + Negocier, algorithme ECDSA).' }
        if ($SupprimerFichierCle) { Remove-Item $CleCoinbase -Force; Write-Host '    Fichier de cle supprime.' }
    }
    if ($PSBoundParameters.ContainsKey('PlafondOrdre') -or $PSBoundParameters.ContainsKey('PlafondMois') -or -not $service) {
        & $Exe limits --order $PlafondOrdre --month $PlafondMois | Out-Null
    }

    Etape 'Demarrage'
    Start-Service $NomService
    & $Exe verify
} finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host ''
Write-Host 'Installation terminee. Etat : & "C:\Program Files\CryptoCryptAgent\cc-agent.exe" status' -ForegroundColor Green
