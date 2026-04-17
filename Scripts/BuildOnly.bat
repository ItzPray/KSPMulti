@echo off
setlocal

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

if defined MSBUILD_EXE (
  set "MSBUILD_CMD=%MSBUILD_EXE%"
) else (
  set "MSBUILD_CMD=msbuild"
)

echo.
echo ===== Building Client =====
call "%MSBUILD_CMD%" "%~dp0..\LmpClient\LmpClient.csproj" /t:Build "/p:Configuration=%SOLUTIONCONFIGURATION%;Platform=AnyCPU;SolutionDir=%REPOROOT%\\;TargetFrameworkVersion=v4.7.2"
if errorlevel 1 (
  echo Client build failed.
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

echo.
echo ===== Publishing Server =====
call "%DOTNET_CMD%" publish "%~dp0..\Server\Server.csproj" -c %SOLUTIONCONFIGURATION% -o "%PUBLISH_DIR%"
if errorlevel 1 (
  echo Server publish failed.
  exit /b 1
)

echo.
echo ===== Build Only Complete =====
echo Client built to: LmpClient\bin\%SOLUTIONCONFIGURATION%
echo Server built to: _build\Server\%SOLUTIONCONFIGURATION%

exit /b 0
