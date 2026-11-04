@echo off
chcp 65001 >nul
cd /d "%~dp0"
where git >nul 2>nul
if errorlevel 1 (
  echo Git is not installed. Run:  winget install Git.Git
  echo Then close this window and double-click this file again.
  pause
  exit /b 1
)
if not exist README.md (
  echo Put this file inside the OpiLink folder, next to README.md.
  pause
  exit /b 1
)
git init
git add .
git commit -m "feat: initial release"
git branch -M main
git remote remove origin >nul 2>nul
git remote add origin https://github.com/OrangePiShell/Opilink.git
git push -u origin main
if errorlevel 1 (
  echo.
  echo PUSH FAILED. Copy the text above and send it to Claude.
  pause
  exit /b 1
)
git tag v1.0.0
git push origin v1.0.0
echo.
echo DONE. Open https://github.com/OrangePiShell/Opilink
pause
