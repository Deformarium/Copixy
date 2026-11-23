@echo off
rem Builds Copixy.exe with the C# compiler that ships with .NET Framework 4.x (present on every Windows 10/11).
setlocal enabledelayedexpansion
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe

rem Compiler options go to a response file: there are 100+ resources, too many for one command line.
set RSP=%TEMP%\copixy_build.rsp
> "%RSP%" echo /nologo /target:winexe /optimize+ /out:Copixy.exe /win32icon:resources\app.ico
>>"%RSP%" echo /resource:resources\app.ico,app.ico
rem toolbar icons, logos, hand cursors (app_*.png and Copixy-Logo-220.png are design sources, not embedded)
for %%f in (resources\icons\*.png) do (
  set "N=%%~nf"
  set "SKIP="
  if /i "!N:~0,4!"=="app_" set SKIP=1
  if /i "!N!"=="Copixy-Logo-220" set SKIP=1
  if not defined SKIP >>"%RSP%" echo /resource:%%f,icon.%%~nxf
)
rem cursor images for the Cursor tool
for %%f in (resources\cursors\*.png) do >>"%RSP%" echo /resource:%%f,icon.%%~nxf
for %%f in (resources\fonts\*.ttf) do >>"%RSP%" echo /resource:%%f,font.%%~nxf
>>"%RSP%" echo /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.dll /r:System.Core.dll
>>"%RSP%" echo src\*.cs

"%CSC%" @"%RSP%"
rem no pause on CI (GitHub Actions sets CI=true)
if errorlevel 1 (
  echo.
  echo Build failed.
  if not defined CI pause
  exit /b 1
)
del "%RSP%" >nul 2>&1
echo Done: Copixy.exe
