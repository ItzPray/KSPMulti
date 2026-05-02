@echo off
setlocal EnableExtensions

REM -----------------------------------------------------------------------------
REM BuildAndDeployFreshStack.bat
REM -----------------------------------------------------------------------------
REM Same inputs as BuildOnly.bat (Debug/Release) but additionally:
REM   1. Deploys Build\<Configuration>\Client into
REM      <KSPPATH>\GameData\KSPMultiplayer\ while PRESERVING the "Data" subfolder
REM      (and any other unmanaged content under KSPMultiplayer\).
REM      When KSPPATH2 is set in SetDirectories.bat, repeats the same deploy for the
REM      second install (two multiplayer test clients).
REM      Managed subfolders that are fully refreshed on every run:
REM        Plugins, Button, Localization, PartSync, Icons, Flags
REM      (This mirrors the set of folders BuildOnly stages under Build\...\Client.)
REM   2. Deploys Build\<Configuration>\Server into the test server root
REM      (default C:\KSPMPServer-test, or LMPSERVERPATH / LMPTESTDEPLOYPATH in SetDirectories.bat)
REM      while PRESERVING "Universe_Backup\". The active "Universe\" directory is
REM      recreated fresh from "Universe_Backup\" so every run starts with a clean
REM      universe save.
REM
REM Usage:
REM   Scripts\BuildAndDeployFreshStack.bat Debug
REM   Scripts\BuildAndDeployFreshStack.bat Release
REM Optional second argument launches two KSP installs ^(KSPPATH + KSPPATH2^) after deploy:
REM   Scripts\BuildAndDeployFreshStack.bat Release LAUNCHDUAL
REM   Scripts\BuildAndDeployFreshStack.bat Release -LaunchDual
REM   ^(Calls LaunchDualKspTestInstances.bat: starts Server.exe under deploy root, then windowed 1280x800 KSP side by side.^)
REM Before build/deploy: stops processes launched from KSPPATH, KSPPATH2 ^(if set^), and
REM Server.exe under the server deploy root ^(same resolution as LMPTESTDEPLOYPATH^).
REM Set LMP_SKIP_STOP=true to skip that stop step.
REM -----------------------------------------------------------------------------

call "%~dp0SetDirectories.bat"

for %%I in ("%~dp0..") do set "REPOROOT=%%~fI"

if "%~1"=="" (
  set SOLUTIONCONFIGURATION=Debug
) else (
  set SOLUTIONCONFIGURATION=%~1
)

if /I not "%SOLUTIONCONFIGURATION%"=="Debug" if /I not "%SOLUTIONCONFIGURATION%"=="Release" (
  echo Invalid configuration "%SOLUTIONCONFIGURATION%". Use Debug or Release.
  exit /b 1
)

if not "%~2"=="" if /I not "%~2"=="LAUNCHDUAL" if /I not "%~2"=="-LaunchDual" (
  echo Unknown second argument "%~2". Use LAUNCHDUAL or -LaunchDual to launch two KSP instances after deploy, or omit it.
  exit /b 1
)

if not defined KSPPATH (
  echo KSPPATH is not set. Set it in Scripts\SetDirectories.bat.
  exit /b 1
)

if not exist "%KSPPATH%\GameData" (
  echo KSPPATH "%KSPPATH%" does not contain a GameData folder. Aborting.
  exit /b 1
)

if defined KSPPATH2 (
  if not exist "%KSPPATH2%\GameData" (
    echo KSPPATH2 "%KSPPATH2%" does not contain a GameData folder. Aborting.
    exit /b 1
  )
)

REM Server deploy target: optional explicit LMPTESTDEPLOYPATH, else LMPSERVERPATH from SetDirectories.bat, else default.
if not defined LMPTESTDEPLOYPATH (
  if defined LMPSERVERPATH (
    set "LMPTESTDEPLOYPATH=%LMPSERVERPATH%"
  ) else (
    set "LMPTESTDEPLOYPATH=C:\KSPMPServer-test"
  )
)

REM CLEANOFFEREDONRESET: when "true", strip every Offered CONTRACT block out of
REM Universe_Backup\Scenarios\ContractSystem.txt BEFORE the live Universe is
REM rebuilt from it. This prevents a stale offered pool (captured into the
REM backup during a past session) from flooding clients on reconnect. Active
REM contracts and all non-CONTRACT data are preserved.
REM
REM Default: true. Set CLEANOFFEREDONRESET=false in SetDirectories.bat (or
REM the shell) to keep the backup as-is.
if not defined CLEANOFFEREDONRESET (
  set "CLEANOFFEREDONRESET=true"
)

set "BUILD_ROOT=%REPOROOT%\Build\%SOLUTIONCONFIGURATION%"
set "CLIENT_OUT=%BUILD_ROOT%\Client"
set "SERVER_OUT=%BUILD_ROOT%\Server"

echo.
if /I "%LMP_SKIP_STOP%"=="true" (
  echo ===== Skipping stop of running KSP/server ^(LMP_SKIP_STOP=true^) =====
) else (
  echo ===== Stopping running instances under deploy paths ^(KSP + test server^) =====
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Stop-TestStackProcesses.ps1" -KspRoot1 "%KSPPATH%" -KspRoot2 "%KSPPATH2%" -ServerDeployRoot "%LMPTESTDEPLOYPATH%"
)


echo.
echo ===== Step 1 / 3: Build client + server via BuildOnly =====
call "%~dp0BuildOnly.bat" %SOLUTIONCONFIGURATION%
if errorlevel 1 (
  echo BuildOnly failed.
  exit /b 1
)

if not exist "%CLIENT_OUT%" (
  echo Expected client staging "%CLIENT_OUT%" is missing after BuildOnly. Aborting.
  exit /b 1
)
if not exist "%SERVER_OUT%" (
  echo Expected server staging "%SERVER_OUT%" is missing after BuildOnly. Aborting.
  exit /b 1
)

echo.
echo ===== Step 2 / 3: Deploy client into KSP ^(preserving Data\^) =====

call :DeployFreshClientToKsp "%KSPPATH%"
if errorlevel 1 exit /b 1

if defined KSPPATH2 (
  echo.
  echo ----- Second client ^(KSPPATH2^) -----
  call :DeployFreshClientToKsp "%KSPPATH2%"
  if errorlevel 1 exit /b 1
)

echo.
echo ===== Step 3 / 3: Deploy server into "%LMPTESTDEPLOYPATH%" with fresh Universe =====

if not exist "%LMPTESTDEPLOYPATH%\" (
  mkdir "%LMPTESTDEPLOYPATH%"
)

REM Universe_Backup is the canonical reset source. First-run bootstrap: if it
REM does not exist yet but a live Universe does, seed Universe_Backup from it so
REM the first reset captures the user's current known-good state. If NEITHER
REM exists the script refuses to continue (no canonical reset source).
if not exist "%LMPTESTDEPLOYPATH%\Universe_Backup\" (
  if exist "%LMPTESTDEPLOYPATH%\Universe\" (
    echo "%LMPTESTDEPLOYPATH%\Universe_Backup\" is missing; bootstrapping it from
    echo the current "%LMPTESTDEPLOYPATH%\Universe\" so future runs have a reset source.
    mkdir "%LMPTESTDEPLOYPATH%\Universe_Backup"
    robocopy "%LMPTESTDEPLOYPATH%\Universe" "%LMPTESTDEPLOYPATH%\Universe_Backup" /E /NFL /NDL /NJH /NJS /NP >nul
    if errorlevel 8 (
      echo ERROR: failed to seed Universe_Backup from Universe.
      exit /b 1
    )
  ) else (
    echo ERROR: neither "%LMPTESTDEPLOYPATH%\Universe_Backup\" nor
    echo "%LMPTESTDEPLOYPATH%\Universe\" exists. Place a known-good Universe
    echo snapshot under "%LMPTESTDEPLOYPATH%\Universe_Backup\" and re-run.
    exit /b 1
  )
)

REM Optionally strip stale offered contracts from the backup before we use it
REM as the reset source. See CLEANOFFEREDONRESET comment above for rationale.
if /I "%CLEANOFFEREDONRESET%"=="true" (
  if exist "%LMPTESTDEPLOYPATH%\Universe_Backup\Scenarios\ContractSystem.txt" (
    echo Stripping stale Offered contracts from Universe_Backup ^(CLEANOFFEREDONRESET=true^)
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0CleanContractsInUniverseBackup.ps1" -Path "%LMPTESTDEPLOYPATH%\Universe_Backup\Scenarios\ContractSystem.txt" -NoBackup
    if errorlevel 1 (
      echo ERROR: CleanContractsInUniverseBackup.ps1 failed.
      exit /b 1
    )
  ) else (
    echo CLEANOFFEREDONRESET=true but ContractSystem.txt not found under Universe_Backup; skipping.
  )
) else (
  echo Skipping offered-contract strip ^(CLEANOFFEREDONRESET=%CLEANOFFEREDONRESET%^).
)

REM Reset the live Universe folder from Universe_Backup so every run starts fresh.
if exist "%LMPTESTDEPLOYPATH%\Universe\" (
  rmdir /S /Q "%LMPTESTDEPLOYPATH%\Universe"
  if exist "%LMPTESTDEPLOYPATH%\Universe\" (
    echo ERROR: failed to remove existing "%LMPTESTDEPLOYPATH%\Universe\".
    echo Is the test server still running? Stop it and re-run.
    exit /b 1
  )
)
mkdir "%LMPTESTDEPLOYPATH%\Universe"
robocopy "%LMPTESTDEPLOYPATH%\Universe_Backup" "%LMPTESTDEPLOYPATH%\Universe" /E /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
  echo ERROR: failed to restore Universe from Universe_Backup.
  exit /b 1
)
echo Universe reset from Universe_Backup.

REM Copy server binaries / runtime files. /XD keeps the Universe folder we just
REM rebuilt, the Universe_Backup the user asked us to preserve, plus per-run
REM state directories that should never be overwritten by a build artifact.
robocopy "%SERVER_OUT%" "%LMPTESTDEPLOYPATH%" /E /XD Universe Universe_Backup Config logs Backup /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
  echo ERROR: server deploy failed ^(robocopy exit code %errorlevel%^).
  exit /b 1
)
echo Server deployed to "%LMPTESTDEPLOYPATH%".

echo.
echo ===== Build + Deploy Complete =====
echo Configuration: %SOLUTIONCONFIGURATION%
echo Client 1      : "%KSPPATH%\GameData\KSPMultiplayer\"   ^(Data\ preserved^)
if defined KSPPATH2 (
  echo Client 2      : "%KSPPATH2%\GameData\KSPMultiplayer\"   ^(Data\ preserved^)
)
echo Server        : "%LMPTESTDEPLOYPATH%"   ^(Universe_Backup\ preserved, Universe\ reset^)

if /I "%~2"=="LAUNCHDUAL" goto RunDualKspLaunch
if /I "%~2"=="-LaunchDual" goto RunDualKspLaunch
goto SkipDualKspLaunch

:RunDualKspLaunch
if not defined KSPPATH2 (
  echo LAUNCHDUAL was requested but KSPPATH2 is not set in Scripts\SetDirectories.bat.
  exit /b 1
)
echo.
echo ===== Optional: dual KSP launch ^(1280x800, side by side^) =====
call "%~dp0LaunchDualKspTestInstances.bat"
if errorlevel 1 exit /b 1

:SkipDualKspLaunch

exit /b 0

REM -----------------------------------------------------------------------------
REM Subroutine: deploy staged Build\...\Client into one KSP root ^(preserves Data\^).
REM -----------------------------------------------------------------------------
:DeployFreshClientToKsp
set "DF_KSP_ROOT=%~1"
set "DF_GAMEDATA=%DF_KSP_ROOT%\GameData"
set "DF_LMP=%DF_GAMEDATA%\KSPMultiplayer"

if not exist "%DF_KSP_ROOT%\GameData" (
  echo ERROR: "%DF_KSP_ROOT%" does not contain a GameData folder.
  exit /b 1
)

if not exist "%DF_LMP%" mkdir "%DF_LMP%"

for %%S in (Plugins Button Localization PartSync Icons Flags) do (
  if exist "%DF_LMP%\%%S\" rmdir /S /Q "%DF_LMP%\%%S"
)

robocopy "%CLIENT_OUT%" "%DF_LMP%" /E /XD "Data" /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
  echo Client deploy failed for "%DF_KSP_ROOT%" ^(robocopy exit code %errorlevel%^).
  exit /b 1
)

if exist "%DF_LMP%\Data\" (
  echo Preserved: "%DF_LMP%\Data\"
) else (
  echo Note: "%DF_LMP%\Data\" does not exist yet; nothing to preserve.
)

if /I "%COPYHARMONY%"=="true" (
  if exist "%BUILD_ROOT%\000_Harmony\" (
    echo COPYHARMONY=true: copying staged 000_Harmony into GameData\ ^(%DF_KSP_ROOT%^).
    robocopy "%BUILD_ROOT%\000_Harmony" "%DF_GAMEDATA%\000_Harmony" /E /NFL /NDL /NJH /NJS /NP >nul
    if errorlevel 8 (
      echo Harmony deploy failed.
      exit /b 1
    )
  ) else (
    echo COPYHARMONY=true but "%BUILD_ROOT%\000_Harmony\" is missing.
  )
) else (
  echo Skipping Harmony deploy ^(set COPYHARMONY=true to include 000_Harmony^).
)

echo Client deployed to "%DF_LMP%".
exit /b 0
