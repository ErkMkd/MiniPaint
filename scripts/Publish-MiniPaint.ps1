#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes MiniPaint (Release, self-contained win-x64) to a stable folder and refreshes the pin shortcut.
#>
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src\MiniPaint.App\MiniPaint.App.csproj"
$OutDir = Join-Path $Root "publish"
$Exe = Join-Path $OutDir "MiniPaint.exe"

Write-Host "Publishing MiniPaint → $OutDir" -ForegroundColor Cyan
Write-Host "  Configuration : Release"
Write-Host "  Runtime       : win-x64 (self-contained)"

dotnet publish $Project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o $OutDir `
  /p:PublishSingleFile=false `
  /p:PublishTrimmed=false

if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $Exe)) {
  throw "Expected published exe not found: $Exe"
}

& (Join-Path $PSScriptRoot "Create-PinShortcut.ps1")

Write-Host ""
Write-Host "Ready to pin:" -ForegroundColor Green
Write-Host "  Exe      : $Exe"
Write-Host "  Shortcut : $(Join-Path $Root 'MiniPaint.lnk')"
Write-Host ""
Write-Host "Pin: right-click MiniPaint.lnk → Pin to taskbar"
Write-Host "  (or drag publish\MiniPaint.exe onto the taskbar)"
