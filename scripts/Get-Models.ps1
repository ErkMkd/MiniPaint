#Requires -Version 5.1
<#
.SYNOPSIS
  Downloads the MobileSAM ONNX models used by Select > Object Selection into models\MobileSAM\.

.DESCRIPTION
  Files come from a pinned commit of https://huggingface.co/Acly/MobileSAM and are checked
  against their SHA-256 before use. Already present, valid files are not downloaded again,
  so the script is cheap to call before every build. The models stay out of git (see .gitignore).

  MobileSAM weights: Apache-2.0 (https://github.com/ChaoningZhang/MobileSAM).
  ONNX export scripts: MIT. See THIRD-PARTY-NOTICES.md.
#>
param(
  [string] $Destination = (Join-Path (Split-Path -Parent $PSScriptRoot) "models\MobileSAM")
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue" # Invoke-WebRequest is very slow with the progress bar

$Base = "https://huggingface.co/Acly/MobileSAM/resolve/0d3b403339b4674a82493d5e97964dd78089ddc8"
$Files = @(
  @{ Name = "mobile_sam_image_encoder.onnx"; Sha256 = "580f5fb648ea1062c0aabc26217aed56921985f03f0cbbd852bba81d760cc749" },
  @{ Name = "sam_mask_decoder_multi.onnx";   Sha256 = "8976b90a87ba50a6a72217a5ff994f7d25ce16f2229fcc1ed259e1294c622ffe" }
)

function Test-Hash([string] $Path, [string] $Expected) {
  if (-not (Test-Path -LiteralPath $Path)) { return $false }
  return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Expected.ToUpperInvariant()
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
foreach ($f in $Files) {
  $target = Join-Path $Destination $f.Name
  if (Test-Hash $target $f.Sha256) {
    Write-Host "  OK  $($f.Name)"
    continue
  }

  Write-Host "  Downloading $($f.Name) ..."
  $temp = "$target.download"
  try {
    Invoke-WebRequest -Uri "$Base/$($f.Name)" -OutFile $temp -UseBasicParsing
    if (-not (Test-Hash $temp $f.Sha256)) {
      throw "SHA-256 mismatch for $($f.Name) - download corrupted or file changed upstream."
    }
    Move-Item -LiteralPath $temp -Destination $target -Force
    Write-Host "  OK  $($f.Name)"
  }
  finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
  }
}
Write-Host "MobileSAM models ready: $Destination"
