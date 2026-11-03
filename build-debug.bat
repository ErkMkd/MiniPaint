@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set PROJECT=%~dp0src\MiniPaint.App\MiniPaint.App.csproj
set EXE=%~dp0src\MiniPaint.App\bin\Debug\net10.0\win-x64\MiniPaint.exe

echo.
echo ========================================
echo  MiniPaint - Debug build (win-x64)
echo  Output: src\MiniPaint.App\bin\Debug\net10.0\win-x64\
echo  (pas de publish, pas de bump, pas de Release GitHub)
echo ========================================
echo.

dotnet build "%PROJECT%" -c Debug --nologo
set ERR=%ERRORLEVEL%

echo.
if not %ERR%==0 (
  echo ECHEC du build Debug. Code=%ERR%
  pause
  exit /b %ERR%
)

if not exist "%EXE%" (
  echo Build OK mais exe introuvable:
  echo   %EXE%
  pause
  exit /b 1
)

echo OK - Debug build pret.
echo   Exe: %EXE%
echo.

REM Optional: build-debug.bat run  → lance l'exe apres le build
if /i "%~1"=="run" (
  echo Lancement...
  start "" "%EXE%"
) else (
  echo Pour lancer:  build-debug.bat run
  echo Ou:           MiniPaint.bat  ^(prefers publish\ s'il existe^)
)

echo.
echo Quand tu es content: publish.bat
echo.
pause
exit /b 0
