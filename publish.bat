@echo off
setlocal EnableExtensions
cd /d "%~dp0"

REM Usage:
REM   publish.bat              -> publish + push build-number + GitHub Release
REM   publish.bat -SkipGit     -> publish only (no git)
REM   publish.bat -SkipRelease -> publish + push counter, no Release zip
REM   publish.bat -SkipGit -SkipRelease

echo.
echo ========================================
echo  MiniPaint - Publish Release win-x64
echo  Output: %~dp0publish\
echo  + incremente build-number.txt
echo  + commit/push GitHub (sauf -SkipGit)
echo  + Release zip (sauf -SkipRelease)
echo ========================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-MiniPaint.ps1" %*
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
