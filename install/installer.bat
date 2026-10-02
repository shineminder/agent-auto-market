@echo off
setlocal EnableExtensions
rem ===================================================================
rem  CryptoCrypt Agent - installateur Windows (double-clic)
rem  Le site peut fournir ce fichier pre-rempli : SITE et CODE ci-dessous.
rem ===================================================================
set "SITE="
set "CODE="
set "DEPOT=shineminder/agent-auto-market"
title Installation de CryptoCrypt Agent

rem ---- Droits administrateur : une seule demande Windows ----
net session >nul 2>&1
if errorlevel 1 (
    echo Windows va demander l autorisation administrateur...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    if errorlevel 1 (
        echo Autorisation refusee : installation annulee.
        pause
    )
    exit /b
)

echo.
echo   ==============================================
echo     Installation de CryptoCrypt Agent
echo   ==============================================
echo.

:site
if not "%SITE%"=="" goto verifsite
set /p "SITE=Adresse de votre site (ex. https://exemple.com) : "
:verifsite
if "%SITE:~-1%"=="/" set "SITE=%SITE:~0,-1%"
if /i not "%SITE:~0,8%"=="https://" (
    echo L adresse doit commencer par https://
    set "SITE="
    goto site
)

if "%CODE%"=="" set /p "CODE=Code d appairage, page Mes acces (Entree seule si mise a jour) : "

rem ---- Fichier de cle Coinbase : recherche automatique ----
set "CLE="
for %%F in ("%~dp0cdp_api_key.json" "%USERPROFILE%\Downloads\cdp_api_key.json" "%USERPROFILE%\Desktop\cdp_api_key.json" "%USERPROFILE%\OneDrive\Desktop\cdp_api_key.json") do (
    if not defined CLE if exist "%%~F" set "CLE=%%~F"
)
if defined CLE goto clefound
echo.
echo Fichier cdp_api_key.json introuvable dans Telechargements ou sur le Bureau.
set /p "CLE=Glissez-le dans cette fenetre puis Entree (Entree seule : l ajouter plus tard) : "
if not defined CLE goto telecharger
set "CLE=%CLE:"=%"
:clefound
if not exist "%CLE%" (
    echo Fichier introuvable : "%CLE%"
    goto fin
)
echo Cle Coinbase : "%CLE%"

:telecharger
echo.
echo Telechargement de l installateur...
set "PS1=%TEMP%\cc-install.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol='Tls12'; $ProgressPreference='SilentlyContinue'; Invoke-WebRequest 'https://github.com/%DEPOT%/releases/latest/download/install.ps1' -OutFile '%PS1%' -UseBasicParsing"
if errorlevel 1 (
    echo Telechargement impossible : verifiez la connexion Internet.
    goto fin
)

set "ARGS=-Serveur "%SITE%""
if not "%CODE%"=="" set "ARGS=%ARGS% -Code "%CODE%""
if defined CLE set "ARGS=%ARGS% -CleCoinbase "%CLE%" -SupprimerFichierCle"
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %ARGS%
set "RES=%ERRORLEVEL%"
del "%PS1%" >nul 2>&1

echo.
if "%RES%"=="0" (
    echo Installation terminee. L agent demarre avec Windows.
) else (
    echo L installation n a pas abouti : lisez le message ci-dessus.
)

:fin
echo.
pause
endlocal
