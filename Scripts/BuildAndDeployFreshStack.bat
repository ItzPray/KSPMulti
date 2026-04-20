@echo off
setlocal EnableExtensions

REM -----------------------------------------------------------------------------
REM BuildAndDeployFreshStack.bat
REM -----------------------------------------------------------------------------
REM Same inputs as BuildOnly.bat (Debug/Release) but additionally:
REM   1. Deploys Build\<Configuration>\Client into
REM      <KSPPATH>\GameData\LunaMultiplayer\ while PRESERVING the "Data" subfolder
REM      (and any other unmanaged content under LunaMultiplayer\).
REM      Managed subfolders that are fully refreshed on every run:
REM        Plugins, Button, Localization, PartSync, Icons, Flags
REM      (This mirrors the set of folders BuildOnly stages under Build\...\Client.)
REM   2. Deploys Build\<Configuration>\Server into the test server root (default
REM      C:\LMPServer-test, override with LMPTESTDEPLOYPATH in SetDirectories.bat)
REM      while PRESERVING "Universe_Backup\". The active "Universe\" directory is
REM      recreated fresh from "Universe_Backup\" so every run starts with a clean
REM      universe save.
REM
REM Usage:
REM   Scripts\BuildAndDeployFreshStack.bat Debug
REM   Scripts\BuildAndDeployFreshStack.bat Release
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

if not defined KSPPATH (
  echo KSPPATH is not set. Set it in Scripts\SetDirectories.bat.
  exit /b 1
)

if not exist "%KSPPATH%\GameData" (
  echo KSPPATH "%KSPPATH%" does not contain a GameData folder. Aborting.
  exit /b 1
)

REM Separate test-deploy path so we do not clobber whatever LMPSERVERPATH points at.
REM Default mirrors the user-requested C:\LMPServer-test location.
if not defined LMPTESTDEPLOYPATH (
  set "LMPTESTDEPLOYPATH=C:\LMPServer-test"
)

set "BUILD_ROOT=%REPOROOT%\Build\%SOLUTIONCONFIGURATION%"
set "CLIENT_OUT=%BUILD_ROOT%\Client"
set "SERVER_OUT=%BUILD_ROOT%\Server"

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
set "GAMEDATA=%KSPPATH%\GameData"
set "LMPFOLDER=%GAMEDATA%\LunaMultiplayer"

if not exist "%LMPFOLDER%" mkdir "%LMPFOLDER%"

REM Purge only managed subfolders. Data\ and anything else under LunaMultiplayer\
REM stays untouched. rmdir+mkdir is simpler and more reliable than del+for/D.
for %%S in (Plugins Button Localization PartSync Icons Flags) do (
  if exist "%LMPFOLDER%\%%S\" rmdir /S /Q "%LMPFOLDER%\%%S"
)

REM Copy staged layout. /E preserves subfolders (Localization can be nested);
REM /XD Data defensively skips the user-preserved folder even if somehow staged.
robocopy "%CLIENT_OUT%" "%LMPFOLDER%" /E /XD "Data" /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
  echo Client deploy failed ^(robocopy exit code %errorlevel%^).
  exit /b 1
)

if exist "%LMPFOLDER%\Data\" (
  echo Preserved: "%LMPFOLDER%\Data\"
) else (
  echo Note: "%LMPFOLDER%\Data\" does not exist yet; nothing to preserve.
)

if /I "%COPYHARMONY%"=="true" (
  if exist "%BUILD_ROOT%\000_Harmony\" (
    echo COPYHARMONY=true: copying staged 000_Harmony into GameData\.
    robocopy "%BUILD_ROOT%\000_Harmony" "%GAMEDATA%\000_Harmony" /E /NFL /NDL /NJH /NJS /NP >nul
    if errorlevel 8 (
      echo Harmony deploy failed.
      exit /b 1
    )
  ) else (
    echo COPYHARMONY=true but "%BUILD_ROOT%\000_Harmony\" is missing. Re-run with COPYHARMONY=true.
  )
) else (
  echo Skipping Harmony deploy ^(set COPYHARMONY=true to include 000_Harmony^).
)

echo Client deployed to "%LMPFOLDER%".

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
echo Client        : "%LMPFOLDER%"   ^(Data\ preserved^)
echo Server        : "%LMPTESTDEPLOYPATH%"   ^(Universe_Backup\ preserved, Universe\ reset^)

exit /b 0
