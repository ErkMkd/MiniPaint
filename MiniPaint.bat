@echo off
setlocal
set EXE=%~dp0src\MiniPaint.App\bin\Debug\net10.0\win-x64\MiniPaint.App.exe
if not exist "%EXE%" (
  echo MiniPaint.App.exe not found. Build first:
  echo   dotnet build "%~dp0MiniPaint.slnx"
  exit /b 1
)
echo Logs: %LOCALAPPDATA%\MiniPaint\logs\
echo Last crash: %LOCALAPPDATA%\MiniPaint\logs\crash-last.txt
start "" "%EXE%"
