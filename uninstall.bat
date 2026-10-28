@echo off
taskkill /f /im AutoCursorRobo.exe >nul 2>&1
rmdir /s /q "%LOCALAPPDATA%\AutoCursorRobo" 2>nul
del "%USERPROFILE%\Desktop\Auto Cursor Robo.lnk" 2>nul
powershell -NoProfile -Command "Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Auto Cursor Robo.lnk') -ErrorAction SilentlyContinue"
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v AutoCursorRobo /f >nul 2>&1
reg delete "HKCU\Software\AutoCursorRobo" /f >nul 2>&1
echo Removed.
pause
