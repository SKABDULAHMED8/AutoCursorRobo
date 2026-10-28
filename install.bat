@echo off
setlocal
cd /d "%~dp0"
call build.bat nopause
if errorlevel 1 exit /b 1
set "DEST=%LOCALAPPDATA%\AutoCursorRobo"
taskkill /f /im AutoCursorRobo.exe >nul 2>&1
if not exist "%DEST%" mkdir "%DEST%"
copy /y bin\AutoCursorRobo.exe "%DEST%\" >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "$s=New-Object -ComObject WScript.Shell;" ^
 "foreach($p in @([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('Programs'))){" ^
 " $l=$s.CreateShortcut((Join-Path $p 'Auto Cursor Robo.lnk'));" ^
 " $l.TargetPath='%DEST%\AutoCursorRobo.exe'; $l.Arguments='/on'; $l.IconLocation='%DEST%\AutoCursorRobo.exe,0';" ^
 " $l.Description='Auto Cursor Robo - keeps your PC awake while you are away'; $l.Save() }"
echo.
echo Installed to %DEST%
echo Desktop + Start Menu shortcut "Auto Cursor Robo" created.
start "" "%DEST%\AutoCursorRobo.exe"
pause
