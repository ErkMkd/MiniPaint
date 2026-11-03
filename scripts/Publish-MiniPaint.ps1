#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes MiniPaint (Release, self-contained win-x64) to publish\,
  wiping any previous build first, bumps build-number.txt, then refreshes the pin shortcut.

  Version scheme: 1.0.<build>  (build counter in repo-root build-number.txt)
#>
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src\MiniPaint.App\MiniPaint.App.csproj"
$OutDir = Join-Path $Root "publish"
$Exe = Join-Path $OutDir "MiniPaint.exe"
$BuildNumberFile = Join-Path $Root "build-number.txt"

# --- bump build number ---
$previous = 0
if (Test-Path $BuildNumberFile) {
  $raw = (Get-Content -LiteralPath $BuildNumberFile -Raw).Trim()
  [void][int]::TryParse($raw, [ref]$previous)
}
$build = $previous + 1
Set-Content -LiteralPath $BuildNumberFile -Value "$build" -Encoding ascii -NoNewline
$version = "1.0.$build"
$fileVersion = "1.0.$build.0"

Write-Host ""
Write-Host "MiniPaint - Release publish (self-contained win-x64)" -ForegroundColor Cyan
Write-Host "  Project : $Project"
Write-Host "  Output  : $OutDir  (previous build will be deleted)"
Write-Host "  Version : $version  (build $previous -> $build, tracked in build-number.txt)"
Write-Host ""

# Wipe previous publish so leftover DLLs from older builds cannot linger.
if (Test-Path $OutDir) {
  Write-Host "Removing previous build: $OutDir" -ForegroundColor Yellow
  try {
    Remove-Item -LiteralPath $OutDir -Recurse -Force -ErrorAction Stop
  }
  catch {
    # Roll back build number if we cannot clear the folder.
    Set-Content -LiteralPath $BuildNumberFile -Value "$previous" -Encoding ascii -NoNewline
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
  /p:PublishTrimmed=false `
  /p:Version=$version `
  /p:AssemblyVersion=$fileVersion `
  /p:FileVersion=$fileVersion `
  /p:InformationalVersion=$version

if ($LASTEXITCODE -ne 0) {
  Set-Content -LiteralPath $BuildNumberFile -Value "$previous" -Encoding ascii -NoNewline
  throw "dotnet publish failed with exit code $LASTEXITCODE (build number restored to $previous)"
}

if (-not (Test-Path $Exe)) {
  Set-Content -LiteralPath $BuildNumberFile -Value "$previous" -Encoding ascii -NoNewline
  throw "Expected published exe not found: $Exe"
}

# Human-readable stamp next to the exe (not required at runtime).
@"
MiniPaint $version
Build $build
Published $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
"@ | Set-Content -LiteralPath (Join-Path $OutDir "VERSION.txt") -Encoding utf8

& (Join-Path $PSScriptRoot "Create-PinShortcut.ps1")

Write-Host ""
Write-Host "OK - previous build overwritten." -ForegroundColor Green
Write-Host "  Version  : $version"
Write-Host "  Exe      : $Exe"
Write-Host "  Shortcut : $(Join-Path $Root 'MiniPaint.lnk')"
Write-Host "  Counter  : $BuildNumberFile"
Write-Host ""
Write-Host "Pin: right-click MiniPaint.lnk -> Pin to taskbar"
Write-Host "  (or drag publish\MiniPaint.exe onto the taskbar)"
