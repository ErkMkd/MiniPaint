#Requires -Version 5.1
<#
.SYNOPSIS
  Creates MiniPaint.lnk at the repo root, pointing at publish\MiniPaint.exe.
  Process AUMID (ErkMkd.MiniPaint) is set by the app at startup for taskbar grouping.
#>
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$OutDir = Join-Path $Root "publish"
$Exe = Join-Path $OutDir "MiniPaint.exe"
$Lnk = Join-Path $Root "MiniPaint.lnk"
$AppUserModelId = "ErkMkd.MiniPaint"

if (-not (Test-Path $Exe)) {
  throw "Published exe missing: $Exe`nRun publish.bat (or scripts\Publish-MiniPaint.ps1) first."
}

$Wsh = New-Object -ComObject WScript.Shell
$Shortcut = $Wsh.CreateShortcut($Lnk)
$Shortcut.TargetPath = $Exe
$Shortcut.WorkingDirectory = $OutDir
$Shortcut.IconLocation = "$Exe,0"
$Shortcut.Description = "MiniPaint - local image editor"
$Shortcut.WindowStyle = 1
$Shortcut.Save()
[System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($Shortcut) | Out-Null
[System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($Wsh) | Out-Null

# Best-effort: stamp AppUserModelID on the .lnk via a compiled helper (matches process AUMID).
$HelperCs = Join-Path $PSScriptRoot "ShellLinkAumid.cs"
if (Test-Path $HelperCs) {
  try {
    Add-Type -Path $HelperCs -ErrorAction Stop
    [MiniPaint.Shell.ShellLinkAumid]::SetAppUserModelId($Lnk, $AppUserModelId)
    Write-Host "Shortcut ready: $Lnk (AUMID=$AppUserModelId)" -ForegroundColor Green
  }
  catch {
    Write-Host "Shortcut ready: $Lnk" -ForegroundColor Green
    Write-Host "  (AUMID stamp skipped: $($_.Exception.Message))" -ForegroundColor Yellow
  }
}
else {
  Write-Host "Shortcut ready: $Lnk" -ForegroundColor Green
}

Write-Host "  Target : $Exe"
Write-Host "  Pin    : right-click the .lnk -> Pin to taskbar"
