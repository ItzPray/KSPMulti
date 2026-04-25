@echo off
setlocal
if exist "%~dp0SetDirectories.bat" call "%~dp0SetDirectories.bat"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0PackageKspmpReleaseZips.ps1" %*
