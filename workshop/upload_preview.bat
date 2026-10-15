@echo off
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul

rem ============================================================
rem  Swap My Audio! - replace the Steam Workshop preview with the
rem  animated GIF. Only the preview changes: no mod files, title,
rem  description or visibility are touched.
rem
rem  Run it AFTER every publish from tModLoader, because the
rem  in-game publisher uploads icon_workshop.png and overwrites it.
rem ============================================================

rem --- settings ------------------------------------------------
rem Path to steamcmd.exe. Leave empty: the script downloads SteamCMD
rem from Valve into workshop\steamcmd\ on first run.
set "STEAMCMD="
rem Steam account that owns the Workshop item
set "STEAM_USER="
rem Number from the Workshop URL: ...filedetails/?id=XXXXXXXXXX
set "PUBLISHED_ID="
rem ------------------------------------------------------------

set "APPID=1281930"
set "HERE=%~dp0"
set "GIF=%HERE%..\icon_workshop.gif"
set "VDF=%HERE%preview_update.vdf"
set "MAX_BYTES=1048576"

if "%STEAMCMD%"=="" set "STEAMCMD=%HERE%steamcmd\steamcmd.exe"

if not exist "%STEAMCMD%" (
    echo SteamCMD not found at "%STEAMCMD%".
    choice /c YN /m "Download it from Valve now (about 3 MB)"
    if errorlevel 2 goto :fail
    for %%D in ("%STEAMCMD%") do set "STEAMCMD_DIR=%%~dpD"
    if not exist "!STEAMCMD_DIR!" mkdir "!STEAMCMD_DIR!"
    echo Downloading...
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "$ErrorActionPreference='Stop'; [Net.ServicePointManager]::SecurityProtocol='Tls12';" ^
        "$zip=Join-Path $env:TEMP 'steamcmd.zip';" ^
        "Invoke-WebRequest 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip' -OutFile $zip -UseBasicParsing;" ^
        "Expand-Archive $zip -DestinationPath $env:STEAMCMD_DIR -Force; Remove-Item $zip"
    if not exist "%STEAMCMD%" (
        echo Download failed. Get SteamCMD manually:
        echo   https://developer.valvesoftware.com/wiki/SteamCMD
        echo and put steamcmd.exe in "!STEAMCMD_DIR!"
        goto :fail
    )
    echo SteamCMD installed. It will update itself on the first launch, that's normal.
    echo.
)

if not exist "%GIF%" (
    echo GIF not found: "%GIF%"
    goto :fail
)

for %%F in ("%GIF%") do (
    set "GIF=%%~fF"
    set "GIF_SIZE=%%~zF"
)
if !GIF_SIZE! GEQ %MAX_BYTES% (
    echo icon_workshop.gif is !GIF_SIZE! bytes. Steam rejects previews of 1 MB or more.
    goto :fail
)

if "%STEAM_USER%"=="" set /p "STEAM_USER=Steam login: "
if "%PUBLISHED_ID%"=="" set /p "PUBLISHED_ID=Workshop item id: "
if "%STEAM_USER%"=="" goto :fail
if "%PUBLISHED_ID%"=="" goto :fail

rem Forward slashes keep the path safe inside the VDF.
set "GIF_VDF=!GIF:\=/!"

> "%VDF%" (
    echo "workshopitem"
    echo {
    echo     "appid"           "%APPID%"
    echo     "publishedfileid" "%PUBLISHED_ID%"
    echo     "previewfile"     "!GIF_VDF!"
    echo     "changenote"      "Updated the preview image."
    echo }
)

echo.
echo Item:    %PUBLISHED_ID%
echo Preview: !GIF! ^(!GIF_SIZE! bytes^)
echo.
echo SteamCMD will ask for your password and a Steam Guard code.
echo.

"%STEAMCMD%" +login "%STEAM_USER%" +workshop_build_item "%VDF%" +quit
set "RC=%ERRORLEVEL%"

del "%VDF%" >nul 2>&1

if not "%RC%"=="0" (
    echo.
    echo SteamCMD exited with code %RC%.
    goto :fail
)

echo.
echo Done: https://steamcommunity.com/sharedfiles/filedetails/?id=%PUBLISHED_ID%
echo Steam can take a few minutes to show the new preview.
pause
exit /b 0

:fail
echo.
echo Nothing was uploaded.
pause
exit /b 1
