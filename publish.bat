@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo.
echo ========================================
echo  MiniPaint - Publish Release win-x64
echo  Output: %~dp0publish\
echo  (efface et remplace le build precedent)
echo ========================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-MiniPaint.ps1"
set ERR=%ERRORLEVEL%

echo.
if not %ERR%==0 (
  echo ECHEC du publish. Code=%ERR%
  echo Ferme MiniPaint.exe s'il tourne, puis relance publish.bat
  pause
  exit /b %ERR%
)

echo Termine. Exe: %~dp0publish\MiniPaint.exe
pause
exit /b 0
