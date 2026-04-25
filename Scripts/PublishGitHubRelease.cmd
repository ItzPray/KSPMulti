@echo off
setlocal
if exist "%~dp0SetDirectories.bat" call "%~dp0SetDirectories.bat"
REM Do not use: cmd /c PublishGitHubRelease.ps1  (cmd does not run .ps1; args are wrong). Use this or: powershell -File ...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0PublishGitHubRelease.ps1" %*
