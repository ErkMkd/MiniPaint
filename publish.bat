@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo.
echo ========================================
echo  MiniPaint - Publish Release win-x64
echo  Output: %~dp0publish\
echo  (efface le build precedent + incremente
echo   le numero dans build-number.txt)
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
if exist "%~dp0publish\VERSION.txt" (
  echo.
  type "%~dp0publish\VERSION.txt"
)
echo.
echo Compteur: %~dp0build-number.txt
pause
exit /b 0
