@echo off
setlocal

if "%~1"=="" (
  set SOLUTIONCONFIGURATION=Debug
) else (
  set SOLUTIONCONFIGURATION=%~1
)

call "%~dp0BuildClientAndCopy.bat" %SOLUTIONCONFIGURATION%
if errorlevel 1 exit /b 1

call "%~dp0PublishServerToTest.bat" %SOLUTIONCONFIGURATION%
if errorlevel 1 exit /b 1

echo Full client and server test deployment completed.
exit /b 0
