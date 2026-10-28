@echo off
setlocal
cd /d "%~dp0"
set "CSC="
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not defined CSC if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not defined CSC (
  echo Could not find csc.exe. .NET Framework 4.x ships with Windows 10/11 - please run Windows Update.
  pause & exit /b 1
)
if not exist bin mkdir bin
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu ^
  /out:bin\AutoCursorRobo.exe ^
  /win32icon:assets\AutoCursorRobo.ico ^
  /resource:assets\AutoCursorRobo.ico,AutoCursorRobo.ico ^
  /resource:assets\AutoCursorRobo_off.ico,AutoCursorRobo_off.ico ^
  /resource:assets\robo.png,robo.png ^
  /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
  src\AutoCursorRobo.cs
if errorlevel 1 (
  echo.
  echo BUILD FAILED - please send me the error lines above.
  pause & exit /b 1
)
echo.
echo Built: %~dp0bin\AutoCursorRobo.exe
if /i not "%~1"=="nopause" pause
