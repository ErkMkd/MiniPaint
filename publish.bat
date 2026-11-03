@echo off
setlocal
REM Publish Release self-contained win-x64 to d:\MiniPaint\publish\ and refresh the pin shortcut.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-MiniPaint.ps1"
exit /b %ERRORLEVEL%
