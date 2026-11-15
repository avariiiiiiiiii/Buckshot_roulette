@echo off
setlocal
whoami | find /I "\codexsandbox" >nul
if not errorlevel 1 (
  echo Steam cannot run from the isolated agent account. Open this file from your Windows desktop.
  exit /b 1
)
set "SteamAppId=3241660"
set "SteamGameId=3241660"
if not exist "%~dp0scratch\playtest\BepInEx\plugins\BuckshotRoulette\BuckshotRoulette.dll" (
  echo Test profile is missing. Run python scripts\start_test_game.py first.
  pause
  exit /b 1
)
tasklist /FI "IMAGENAME eq REPO.exe" /NH | find /I "REPO.exe" >nul
if not errorlevel 1 (
  echo R.E.P.O. is already running.
  pause
  exit /b 1
)
tasklist /FI "IMAGENAME eq steam.exe" /NH | find /I "steam.exe" >nul
if errorlevel 1 (
  start "" "C:\Program Files (x86)\Steam\steam.exe" -silent
  timeout /t 10 /nobreak >nul
)
start "" /D "C:\Program Files (x86)\Steam\steamapps\common\REPO" "C:\Program Files (x86)\Steam\steamapps\common\REPO\REPO.exe" --doorstop-enable true --doorstop-target-assembly "%~dp0scratch\playtest\BepInEx\core\BepInEx.Preloader.dll" -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "%~dp0scratch\playtest\Player.log"
