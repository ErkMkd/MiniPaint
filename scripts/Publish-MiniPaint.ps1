#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes MiniPaint (Release, self-contained win-x64) to publish\,
  wiping any previous build first, then refreshes the pin shortcut.
#>
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src\MiniPaint.App\MiniPaint.App.csproj"
$OutDir = Join-Path $Root "publish"
$Exe = Join-Path $OutDir "MiniPaint.exe"

Write-Host ""
Write-Host "MiniPaint - Release publish (self-contained win-x64)" -ForegroundColor Cyan
Write-Host "  Project : $Project"
Write-Host "  Output  : $OutDir  (previous build will be deleted)"
Write-Host ""

# Wipe previous publish so leftover DLLs from older builds cannot linger.
if (Test-Path $OutDir) {
  Write-Host "Removing previous build: $OutDir" -ForegroundColor Yellow
  try {
    Remove-Item -LiteralPath $OutDir -Recurse -Force -ErrorAction Stop
  }
  catch {
    throw "Cannot clear '$OutDir'. Close MiniPaint.exe (and any Explorer window locking the folder), then retry. $($_.Exception.Message)"
  }
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

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
Write-Host "OK - previous build overwritten." -ForegroundColor Green
Write-Host "  Exe      : $Exe"
Write-Host "  Shortcut : $(Join-Path $Root 'MiniPaint.lnk')"
Write-Host ""
Write-Host "Pin: right-click MiniPaint.lnk -> Pin to taskbar"
Write-Host "  (or drag publish\MiniPaint.exe onto the taskbar)"
