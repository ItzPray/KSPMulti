@echo off
setlocal EnableExtensions

call "%~dp0SetDirectories.bat"

set "ERRORS=0"

echo === Verifying LunaMultiplayer local environment ===

if defined MSBUILD_EXE (
  if exist "%MSBUILD_EXE%" (
    echo [OK] MSBUILD_EXE=%MSBUILD_EXE%
  ) else (
    echo [ERROR] MSBUILD_EXE is set but does not exist: %MSBUILD_EXE%
    set /a ERRORS+=1
  )
) else (
  where msbuild >nul 2>nul
  if errorlevel 1 (
    echo [ERROR] msbuild was not found on PATH. Set MSBUILD_EXE in Scripts\SetDirectories.bat.
    set /a ERRORS+=1
  ) else (
    for /f "delims=" %%I in ('where msbuild') do (
      echo [OK] msbuild found: %%I
      goto :msbuild_done
    )
  )
)
:msbuild_done

if defined DOTNET_EXE (
  if exist "%DOTNET_EXE%" (
    echo [OK] DOTNET_EXE=%DOTNET_EXE%
  ) else (
    echo [ERROR] DOTNET_EXE is set but does not exist: %DOTNET_EXE%
    set /a ERRORS+=1
  )
) else (
  where dotnet >nul 2>nul
  if errorlevel 1 (
    echo [ERROR] dotnet was not found on PATH. Set DOTNET_EXE in Scripts\SetDirectories.bat.
    set /a ERRORS+=1
  ) else (
    for /f "delims=" %%I in ('where dotnet') do (
      echo [OK] dotnet found: %%I
      goto :dotnet_done
    )
  )
)
:dotnet_done

if exist "%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2" (
  echo [OK] .NET Framework 4.7.2 targeting pack/reference assemblies found.
) else (
  echo [ERROR] .NET Framework 4.7.2 targeting pack/reference assemblies not found.
  set /a ERRORS+=1
)

call :check_file "External\KSPLibraries\Assembly-CSharp.dll"
call :check_file "External\KSPLibraries\UnityEngine.dll"
call :check_file "External\Dependencies\Harmony\000_Harmony\0Harmony.dll"
call :check_file "LmpClient\LmpClient.csproj"
call :check_file "Server\Server.csproj"

if defined KSPPATH (
  call :check_dir "%KSPPATH%" "KSPPATH"
  if exist "%KSPPATH%\KSP_x64.exe" (
    echo [OK] KSP executable found in KSPPATH.
  ) else (
    echo [WARN] KSPPATH exists but KSP_x64.exe was not found: %KSPPATH%
  )
) else (
  echo [WARN] KSPPATH is not set. Client deploy scripts will default to C:\Kerbal Space Program.
)

if defined KSPPATH2 (
  call :check_dir "%KSPPATH2%" "KSPPATH2"
)

if defined LMPSERVERPATH (
  call :check_dir "%LMPSERVERPATH%" "LMPSERVERPATH"
) else (
  echo [WARN] LMPSERVERPATH is not set. Server deployment scripts cannot target a test server folder yet.
)

echo.
if "%ERRORS%"=="0" (
  echo Environment verification passed.
  exit /b 0
) else (
  echo Environment verification failed with %ERRORS% error^(s^).
  exit /b 1
)

:check_file
if exist "%~1" (
  echo [OK] %~1
) else (
  echo [ERROR] Missing required file: %~1
  set /a ERRORS+=1
)
exit /b 0

:check_dir
if exist "%~1" (
  echo [OK] %~2=%~1
) else (
  echo [ERROR] %~2 does not exist: %~1
  set /a ERRORS+=1
)
exit /b 0
