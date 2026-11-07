#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes MiniPaint (Release, self-contained win-x64) to publish\,
  bumps build-number.txt, refreshes the pin shortcut, then (by default)
  commits/pushes the counter to GitHub and creates a Release with a zip asset.

  The release version comes from version.txt (major.minor.patch, e.g. 1.1.0). After a
  successful publish its patch is bumped for the next one (1.1.0 -> 1.1.1); edit the file
  to start a new minor or major version. The build counter stays as the 4th part of the
  file version (1.1.0.<build>) so every publish remains traceable.

.PARAMETER SkipGit
  Do not commit/push build-number.txt.

.PARAMETER SkipRelease
  Do not zip or create a GitHub Release (git push of the counter still runs unless -SkipGit).
#>
param(
  [switch] $SkipGit,
  [switch] $SkipRelease
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src\MiniPaint.App\MiniPaint.App.csproj"
$OutDir = Join-Path $Root "publish"
$DistDir = Join-Path $Root "dist"
$Exe = Join-Path $OutDir "MiniPaint.exe"
$BuildNumberFile = Join-Path $Root "build-number.txt"
$VersionFile = Join-Path $Root "version.txt"
$Repo = "ErkMkd/MiniPaint"

function Get-GitHubToken {
  $fill = @"
protocol=https
host=github.com

"@ | & git -c credential.helper= -c credential.helper=store credential fill 2>$null
  if (-not $fill) { return $null }
  $line = $fill | Where-Object { $_ -match '^password=' } | Select-Object -First 1
  if (-not $line) { return $null }
  return ($line -replace '^password=', '')
}

# --- bump build number ---
$previous = 0
if (Test-Path $BuildNumberFile) {
  $raw = (Get-Content -LiteralPath $BuildNumberFile -Raw).Trim()
  [void][int]::TryParse($raw, [ref]$previous)
}
$build = $previous + 1

# --- release version from version.txt ---
$version = if (Test-Path $VersionFile) { (Get-Content -LiteralPath $VersionFile -Raw).Trim() } else { "" }
if ($version -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
  throw "version.txt must contain the next release version as major.minor.patch (e.g. 1.1.0), found '$version'."
}
$nextVersion = "$($Matches[1]).$($Matches[2]).$([int]$Matches[3] + 1)"
$fileVersion = "$version.$build"
$tag = "v$version"

Set-Content -LiteralPath $BuildNumberFile -Value "$build" -Encoding ascii -NoNewline

Write-Host ""
Write-Host "MiniPaint - Release publish (self-contained win-x64)" -ForegroundColor Cyan
Write-Host "  Project : $Project"
Write-Host "  Output  : $OutDir  (previous build will be deleted)"
Write-Host "  Version : $version  (from version.txt; build $previous -> $build, tracked in build-number.txt)"
Write-Host ""

# Wipe previous publish so leftover DLLs from older builds cannot linger.
if (Test-Path $OutDir) {
  Write-Host "Removing previous build: $OutDir" -ForegroundColor Yellow
  try {
    Remove-Item -LiteralPath $OutDir -Recurse -Force -ErrorAction Stop
  }
  catch {
    Set-Content -LiteralPath $BuildNumberFile -Value "$previous" -Encoding ascii -NoNewline
    throw "Cannot clear '$OutDir'. Close MiniPaint.exe (and any Explorer window locking the folder), then retry. $($_.Exception.Message)"
  }
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

# Object Selection AI models (MobileSAM) must ship with the release.
try {
  & (Join-Path $PSScriptRoot "Get-Models.ps1")
}
catch {
  Set-Content -LiteralPath $BuildNumberFile -Value "$previous" -Encoding ascii -NoNewline
  throw "Cannot get the MobileSAM models (scripts\Get-Models.ps1): $($_.Exception.Message) (build number restored to $previous)"
}

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

$versionText = "MiniPaint $version`r`nBuild $build`r`nPublished $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`r`n"
[System.IO.File]::WriteAllText((Join-Path $OutDir "VERSION.txt"), $versionText)

# Published: the next publish gets the following patch version.
Set-Content -LiteralPath $VersionFile -Value "$nextVersion" -Encoding ascii -NoNewline

& (Join-Path $PSScriptRoot "Create-PinShortcut.ps1")

Write-Host ""
Write-Host "OK - previous build overwritten." -ForegroundColor Green
Write-Host "  Version  : $version"
Write-Host "  Exe      : $Exe"
Write-Host "  Shortcut : $(Join-Path $Root 'MiniPaint.lnk')"

# --- git commit + push build-number.txt ---
$gitOk = $false
if (-not $SkipGit) {
  Write-Host ""
  Write-Host "GitHub: commit + push build-number.txt ..." -ForegroundColor Cyan
  Push-Location $Root
  try {
    & git add -- "build-number.txt" "version.txt"
    $pending = & git status --porcelain -- "build-number.txt" "version.txt"
    if ($pending) {
      & git commit -m "Bump build to $build ($version)."
      if ($LASTEXITCODE -ne 0) { throw "git commit failed" }
    }
    else {
      Write-Host "  (build-number.txt / version.txt already committed)"
    }

    # Push the commit we just made (current HEAD), even if we are on a feature branch.
    # "git push origin main" only updates from local main and can be a no-op while HEAD is elsewhere.
    $headSha = (& git rev-parse HEAD).Trim()
    Write-Host "  Pushing $headSha -> origin/main ..."
    & git -c credential.helper= -c credential.helper=store push origin "HEAD:main"
    if ($LASTEXITCODE -ne 0) { throw "git push HEAD:main failed" }

    & git fetch origin main --quiet
    $remoteMain = (& git rev-parse origin/main).Trim()
    if ($remoteMain -ne $headSha) {
      throw "origin/main is $remoteMain after push; expected $headSha"
    }

    $gitOk = $true
    Write-Host "  Pushed to origin/main ($headSha)" -ForegroundColor Green
  }
  catch {
    Write-Host "  Git push skipped/failed: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "  Publish folder is still OK locally."
  }
  finally {
    Pop-Location
  }
}
else {
  Write-Host "  Skipping git (-SkipGit)."
}

# --- zip + GitHub Release ---
if (-not $SkipRelease) {
  Write-Host ""
  Write-Host "GitHub Release $tag ..." -ForegroundColor Cyan
  try {
    if (-not $gitOk -and -not $SkipGit) {
      throw "Skipping release: build commit was not pushed to origin/main (target would be invalid)."
    }

    New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
    $zipName = "MiniPaint-$tag-win-x64.zip"
    $zipPath = Join-Path $DistDir $zipName
    if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

    Compress-Archive -Path (Join-Path $OutDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "  Zip: $zipPath"

    $token = Get-GitHubToken
    if ($token) { $env:GH_TOKEN = $token }

    $notes = @"
## MiniPaint $tag

Self-contained Windows x64 build $build - unzip and run MiniPaint.exe.

Developpe par Cursor, orchestre par Eric Kernin.
MIT License - Copyright (c) 2026 Eric Kernin / ErkMkd
"@
    # Always target the default branch name. A local-only SHA makes GitHub return
    # HTTP 422 Release.target_commitish is invalid.
    $target = "main"

    # Do NOT use "gh release view" under $ErrorActionPreference=Stop:
    # a missing release writes "release not found" to stderr and aborts before create.
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
      & gh release create $tag $zipPath `
        --repo $Repo `
        --title "MiniPaint $tag" `
        --notes $notes `
        --target $target 2>&1 | ForEach-Object { Write-Host "  $_" }
      $createCode = $LASTEXITCODE
    }
    finally {
      $ErrorActionPreference = $prevEap
    }

    if ($createCode -ne 0) {
      Write-Host "  create returned $createCode - trying upload to existing tag..."
      $ErrorActionPreference = "Continue"
      try {
        & gh release upload $tag $zipPath --repo $Repo --clobber 2>&1 | ForEach-Object { Write-Host "  $_" }
        $uploadCode = $LASTEXITCODE
      }
      finally {
        $ErrorActionPreference = $prevEap
      }
      if ($uploadCode -ne 0) {
        throw "gh release create/upload failed (create=$createCode upload=$uploadCode)"
      }
    }

    Write-Host "  Release: https://github.com/$Repo/releases/tag/$tag" -ForegroundColor Green
  }
  catch {
    Write-Host "  Release skipped/failed: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "  Local publish is still OK. You can retry with gh later."
  }
}
else {
  Write-Host "  Skipping GitHub Release (-SkipRelease)."
}

Write-Host ""
Write-Host "Pin: right-click MiniPaint.lnk -> Pin to taskbar"
Write-Host "  (or drag publish\MiniPaint.exe onto the taskbar)"
