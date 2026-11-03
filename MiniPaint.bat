@echo off
setlocal
REM Prefer the stable published exe (taskbar-pin friendly). Fall back to Debug build.
set PUB=%~dp0publish\MiniPaint.exe
set DBG=%~dp0src\MiniPaint.App\bin\Debug\net10.0\win-x64\MiniPaint.exe
set DBG_OLD=%~dp0src\MiniPaint.App\bin\Debug\net10.0\win-x64\MiniPaint.App.exe

if exist "%PUB%" (
  set EXE=%PUB%
) else if exist "%DBG%" (
  set EXE=%DBG%
  echo [MiniPaint] Using Debug build. For taskbar pin, run publish.bat first.
) else if exist "%DBG_OLD%" (
  set EXE=%DBG_OLD%
  echo [MiniPaint] Using Debug build. For taskbar pin, run publish.bat first.
) else (
  echo MiniPaint.exe not found.
  echo   1^) Debug build:   build-debug.bat
  echo   2^) Publish:       publish.bat
  exit /b 1
)

echo Launching: %EXE%
echo Logs: %LOCALAPPDATA%\MiniPaint\logs\
echo Last crash: %LOCALAPPDATA%\MiniPaint\logs\crash-last.txt
echo Pin to taskbar: use "%~dp0MiniPaint.lnk" ^(created by publish.bat^)
start "" "%EXE%"
