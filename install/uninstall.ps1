#Requires -Version 5.1
# Desinstalle CryptoCrypt Agent. -ConserverDonnees garde la configuration et la cle chiffree.
[CmdletBinding()]
param([switch]$ConserverDonnees)
$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Write-Host 'Relancez en tant qu administrateur.' -ForegroundColor Red; exit 1 }

$NomService = 'CryptoCryptAgent'
if (Get-Service -Name $NomService -ErrorAction SilentlyContinue) {
    Stop-Service $NomService -Force -ErrorAction SilentlyContinue
    sc.exe delete $NomService | Out-Null
}
Remove-Item (Join-Path $env:ProgramFiles 'CryptoCryptAgent') -Recurse -Force -ErrorAction SilentlyContinue
if (-not $ConserverDonnees) { Remove-Item (Join-Path $env:ProgramData 'CryptoCryptAgent') -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host 'Agent desinstalle. Pensez a revoquer l appareil sur le site (Mes acces) et a supprimer la cle chez Coinbase.' -ForegroundColor Green
