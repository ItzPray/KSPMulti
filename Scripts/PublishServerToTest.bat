@echo off
setlocal

call "%~dp0SetDirectories.bat"

if not defined LMPSERVERPATH (
  echo LMPSERVERPATH is not set. Add it to Scripts\SetDirectories.bat.
  exit /b 1
)

if "%~1"=="" (
  set SOLUTIONCONFIGURATION=Debug
) else (
  set SOLUTIONCONFIGURATION=%~1
)

if /I not "%SOLUTIONCONFIGURATION%"=="Debug" if /I not "%SOLUTIONCONFIGURATION%"=="Release" (
  echo Invalid configuration "%SOLUTIONCONFIGURATION%". Use Debug or Release.
  exit /b 1
)

if defined DOTNET_EXE (
  set "DOTNET_CMD=%DOTNET_EXE%"
) else (
  set "DOTNET_CMD=dotnet"
)

set "PUBLISH_DIR=%~dp0..\_build\Server\%SOLUTIONCONFIGURATION%"

if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
mkdir "%PUBLISH_DIR%"

echo Publishing server with %DOTNET_CMD%...
call "%DOTNET_CMD%" publish "%~dp0..\Server\Server.csproj" -c %SOLUTIONCONFIGURATION% -o "%PUBLISH_DIR%"
if errorlevel 1 (
  echo Server publish failed.
  exit /b 1
)

echo Copying published server runtime into %LMPSERVERPATH%...
robocopy "%PUBLISH_DIR%" "%LMPSERVERPATH%" /E /XD Universe Config logs /NFL /NDL /NJH /NJS /NP
if errorlevel 8 (
  echo Server deploy failed.
  exit /b 1
)

echo Server publish and deploy completed.
exit /b 0
