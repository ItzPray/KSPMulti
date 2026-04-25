::You must keep this file in the solution folder for it to work.
::Make sure to pass the solution configuration when calling it (either Debug or Release)

::Set the directories in the SetDirectories.bat file if you want a different folder than Kerbal Space Program
::EXAMPLE:
:: SET KSPPATH=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
:: SET KSPPATH2=C:\Users\Malte\Desktop\Kerbal Space Program
call "%~dp0\SetDirectories.bat"

IF DEFINED KSPPATH (ECHO KSPPATH is defined) ELSE (SET KSPPATH=C:\Kerbal Space Program)
IF DEFINED KSPPATH2 (ECHO KSPPATH2 is defined)

IF "%~1"=="" (
  SET SOLUTIONCONFIGURATION=Debug
) ELSE (
  SET SOLUTIONCONFIGURATION=%~1
)

IF /I NOT "%SOLUTIONCONFIGURATION%"=="Debug" IF /I NOT "%SOLUTIONCONFIGURATION%"=="Release" (
  ECHO Invalid configuration "%SOLUTIONCONFIGURATION%". Use Debug or Release.
  EXIT /B 1
)

IF NOT DEFINED COPYHARMONY SET COPYHARMONY=false

ECHO Using configuration: %SOLUTIONCONFIGURATION%
ECHO COPYHARMONY=%COPYHARMONY%

mkdir "%KSPPATH%\GameData\KSPMultiplayer\"
IF DEFINED KSPPATH2 (mkdir "%KSPPATH2%\GameData\KSPMultiplayer\")

mkdir "%KSPPATH%\GameData\KSPMultiplayer\Plugins"
IF DEFINED KSPPATH2 (mkdir "%KSPPATH2%\GameData\KSPMultiplayer\Plugins")

del "%KSPPATH%\GameData\KSPMultiplayer\Plugins\*.*" /Q /F
IF DEFINED KSPPATH2 (del "%KSPPATH2%\GameData\KSPMultiplayer\Plugins\*.*" /Q /F)

mkdir "%KSPPATH%\GameData\KSPMultiplayer\Button"
IF DEFINED KSPPATH2 (mkdir "%KSPPATH2%\GameData\KSPMultiplayer\Button")

del "%KSPPATH%\GameData\KSPMultiplayer\Button\*.*" /Q /F
IF DEFINED KSPPATH2 (del "%KSPPATH2%\GameData\KSPMultiplayer\Button\*.*" /Q /F)

mkdir "%KSPPATH%\GameData\KSPMultiplayer\Localization"
IF DEFINED KSPPATH2 (mkdir "%KSPPATH2%\GameData\KSPMultiplayer\Localization")

del "%KSPPATH%\GameData\KSPMultiplayer\Localization\*.*" /Q /F
IF DEFINED KSPPATH2 (del "%KSPPATH2%\GameData\KSPMultiplayer\Localization\*.*" /Q /F)

mkdir "%KSPPATH%\GameData\KSPMultiplayer\PartSync"
IF DEFINED KSPPATH2 (mkdir "%KSPPATH2%\GameData\KSPMultiplayer\PartSync")

del "%KSPPATH%\GameData\KSPMultiplayer\PartSync\*.*" /Q /F
IF DEFINED KSPPATH2 (del "%KSPPATH2%\GameData\KSPMultiplayer\PartSync\*.*" /Q /F)

mkdir "%KSPPATH%\GameData\KSPMultiplayer\Icons"
IF DEFINED KSPPATH2 (mkdir "%KSPPATH2%\GameData\KSPMultiplayer\Icons")

del "%KSPPATH%\GameData\KSPMultiplayer\Icons\*.*" /Q /F
IF DEFINED KSPPATH2 (del "%KSPPATH2%\GameData\KSPMultiplayer\Icons\*.*" /Q /F)

mkdir "%KSPPATH%\GameData\KSPMultiplayer\Flags"
IF DEFINED KSPPATH2 (mkdir "%KSPPATH2%\GameData\KSPMultiplayer\Flags")

del "%KSPPATH%\GameData\KSPMultiplayer\Flags\*.*" /Q /F
IF DEFINED KSPPATH2 (del "%KSPPATH2%\GameData\KSPMultiplayer\Flags\*.*" /Q /F)

mkdir "%KSPPATH%\GameData\KSPMultiplayer\LoadingScreens"
IF DEFINED KSPPATH2 (mkdir "%KSPPATH2%\GameData\KSPMultiplayer\LoadingScreens")

del "%KSPPATH%\GameData\KSPMultiplayer\LoadingScreens\*.*" /Q /F
IF DEFINED KSPPATH2 (del "%KSPPATH2%\GameData\KSPMultiplayer\LoadingScreens\*.*" /Q /F)

mkdir "%KSPPATH%\UserLoadingScreens"
IF DEFINED KSPPATH2 (mkdir "%KSPPATH2%\UserLoadingScreens")

IF /I "%COPYHARMONY%"=="true" (
  xcopy /Y /s /e "%~dp0..\External\Dependencies\Harmony\" "%KSPPATH%\GameData\"
  IF DEFINED KSPPATH2 (xcopy /Y /s /e "%~dp0..\External\Dependencies\Harmony\" "%KSPPATH2%\GameData\")
) ELSE (
  ECHO Skipping Harmony copy. Existing 000_Harmony will be left untouched.
)

IF NOT EXIST "%~dp0..\KSPMultiplayer.version" (
  ECHO ERROR: KSPMultiplayer.version missing at repo root.
  EXIT /B 1
)
copy /Y "%~dp0..\KSPMultiplayer.version" "%KSPPATH%\GameData\KSPMultiplayer\KSPMultiplayer.version" >nul
IF DEFINED KSPPATH2 (copy /Y "%~dp0..\KSPMultiplayer.version" "%KSPPATH2%\GameData\KSPMultiplayer\KSPMultiplayer.version" >nul)

xcopy /Y "%~dp0..\LmpClient\bin\%SOLUTIONCONFIGURATION%\*.*" "%KSPPATH%\GameData\KSPMultiplayer\Plugins"
IF DEFINED KSPPATH2 (xcopy /Y "%~dp0..\LmpClient\bin\%SOLUTIONCONFIGURATION%\*.*" "%KSPPATH2%\GameData\KSPMultiplayer\Plugins")

xcopy /Y "%~dp0..\External\Dependencies\*.*" "%KSPPATH%\GameData\KSPMultiplayer\Plugins"
IF DEFINED KSPPATH2 (xcopy /Y "%~dp0..\External\Dependencies\*.*" "%KSPPATH2%\GameData\KSPMultiplayer\Plugins")

xcopy /Y "%~dp0..\LmpClient\Resources\*.png" "%KSPPATH%\GameData\KSPMultiplayer\Button"
IF DEFINED KSPPATH2 (xcopy /Y "%~dp0..\LmpClient\Resources\*.png" "%KSPPATH2%\GameData\KSPMultiplayer\Button")

xcopy /Y /S "%~dp0..\LmpClient\Localization\XML\*.*" "%KSPPATH%\GameData\KSPMultiplayer\Localization"
IF DEFINED KSPPATH2 (xcopy /Y /S "%~dp0..\LmpClient\Localization\XML\*.*" "%KSPPATH2%\GameData\KSPMultiplayer\Localization")

xcopy /Y /S "%~dp0..\LmpClient\ModuleStore\XML\*.xml" "%KSPPATH%\GameData\KSPMultiplayer\PartSync"
IF DEFINED KSPPATH2 (xcopy /Y /S "%~dp0..\LmpClient\ModuleStore\XML\*.xml" "%KSPPATH2%\GameData\KSPMultiplayer\PartSync")

xcopy /Y "%~dp0..\LmpClient\Resources\Icons\*.*" "%KSPPATH%\GameData\KSPMultiplayer\Icons"
IF DEFINED KSPPATH2 (xcopy /Y "%~dp0..\LmpClient\Resources\Icons\*.*" "%KSPPATH2%\GameData\KSPMultiplayer\Icons")

xcopy /Y "%~dp0..\LmpClient\Resources\Flags\*.*" "%KSPPATH%\GameData\KSPMultiplayer\Flags"
IF DEFINED KSPPATH2 (xcopy /Y "%~dp0..\LmpClient\Resources\Flags\*.*" "%KSPPATH2%\GameData\KSPMultiplayer\Flags")

xcopy /Y "%~dp0..\LmpClient\Resources\LoadingScreens\*.*" "%KSPPATH%\GameData\KSPMultiplayer\LoadingScreens"
IF DEFINED KSPPATH2 (xcopy /Y "%~dp0..\LmpClient\Resources\LoadingScreens\*.*" "%KSPPATH2%\GameData\KSPMultiplayer\LoadingScreens")

xcopy /Y "%~dp0..\LmpClient\Resources\LoadingScreens\KSPMultiLoadingScreen.png" "%KSPPATH%\UserLoadingScreens"
IF DEFINED KSPPATH2 (xcopy /Y "%~dp0..\LmpClient\Resources\LoadingScreens\KSPMultiLoadingScreen.png" "%KSPPATH2%\UserLoadingScreens")
