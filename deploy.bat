
@echo off

rem H is the destination game folder
rem GAMEDIR is the name of the mod folder (usually the mod name)
rem GAMEDATA is the name of the local GameData

set H=%KSPDIR%
set GAMEDIR=_0000_KramaxPluginReload
set GAMEDATA="GameData"
set VERSIONFILE=%GAMEDIR%.version

copy /Y "%1%2" "%GAMEDATA%\%GAMEDIR%\Plugins"

xcopy /y /s /I %GAMEDATA%\%GAMEDIR% "%H%\GameData\%GAMEDIR%"
