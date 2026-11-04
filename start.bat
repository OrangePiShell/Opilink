@echo off
setlocal
cd /d "%~dp0"
title Orange Pi Link

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

rem Files from a downloaded zip are marked "from the internet"; clear that mark.
powershell -NoProfile -Command "Get-ChildItem -Recurse -File | Unblock-File" >nul 2>&1

set NEEDBUILD=0
if not exist OpiLink.exe set NEEDBUILD=1
if not exist OpiLink.ver set NEEDBUILD=1
if exist OpiLink.ver (
  fc /b VERSION.txt OpiLink.ver >nul 2>&1
  if errorlevel 1 set NEEDBUILD=1
)
if exist OpiLink.exe (
  powershell -NoProfile -Command "$e=(Get-Item OpiLink.exe).LastWriteTime; if (Get-ChildItem app -File | Where-Object { $_.LastWriteTime -gt $e }) { exit 1 }" >nul 2>&1
  if errorlevel 1 set NEEDBUILD=1
)

if "%NEEDBUILD%"=="1" (
  if not exist "%CSC%" (
    echo The .NET Framework 4 compiler was not found at: %CSC%
    pause
    exit /b 1
  )
  taskkill /f /im OpiLink.exe >nul 2>&1
  if exist OpiLink.ver del /q OpiLink.ver
  echo Building Orange Pi Link - this only happens on the first run...
  "%CSC%" /nologo /codepage:65001 /target:winexe /out:OpiLink.exe /win32icon:assets\app.ico /win32manifest:app\app.manifest /r:System.Windows.Forms.dll /r:System.Drawing.dll app\*.cs
  if errorlevel 1 (
    echo.
    echo Build failed. Copy the errors above and send them.
    pause
    exit /b 1
  )
  copy /y VERSION.txt OpiLink.ver >nul
)

start "" "%~dp0OpiLink.exe"
