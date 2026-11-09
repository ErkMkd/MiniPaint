#Requires -Version 5.1
<#
.SYNOPSIS
  Downloads the local AI models into models\:
    models\MobileSAM\  Select > Object Selection   (~45 MB)
    models\LaMa\       Edit > Content-Aware Fill   (~208 MB)

.DESCRIPTION
  Files come from pinned Hugging Face commits and are checked against their SHA-256 before use.
  Already present, valid files are not downloaded again, so the script is cheap to call before
  every build. The models stay out of git (see .gitignore).

  MobileSAM weights: Apache-2.0 (https://github.com/ChaoningZhang/MobileSAM); ONNX export: MIT.
  LaMa weights: Apache-2.0 (https://github.com/advimman/lama); ONNX export: Apache-2.0.
  See THIRD-PARTY-NOTICES.md.
#>
param(
  [string] $Destination = (Join-Path (Split-Path -Parent $PSScriptRoot) "models")
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue" # Invoke-WebRequest is very slow with the progress bar

$Models = @(
  @{
    Folder = "MobileSAM"
    Base   = "https://huggingface.co/Acly/MobileSAM/resolve/0d3b403339b4674a82493d5e97964dd78089ddc8"
    Files  = @(
      @{ Name = "mobile_sam_image_encoder.onnx"; Sha256 = "580f5fb648ea1062c0aabc26217aed56921985f03f0cbbd852bba81d760cc749" },
      @{ Name = "sam_mask_decoder_multi.onnx";   Sha256 = "8976b90a87ba50a6a72217a5ff994f7d25ce16f2229fcc1ed259e1294c622ffe" }
    )
  },
  @{
    Folder = "LaMa"
    Base   = "https://huggingface.co/Carve/LaMa-ONNX/resolve/c3c0c9e468934d62e79c329e35d82dd09ff8c444"
    Files  = @(
      @{ Name = "lama_fp32.onnx"; Sha256 = "1faef5301d78db7dda502fe59966957ec4b79dd64e16f03ed96913c7a4eb68d6" }
    )
  }
)

function Test-Hash([string] $Path, [string] $Expected) {
  if (-not (Test-Path -LiteralPath $Path)) { return $false }
  return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $Expected.ToUpperInvariant()
}

foreach ($model in $Models) {
  $folder = Join-Path $Destination $model.Folder
  New-Item -ItemType Directory -Force -Path $folder | Out-Null
  foreach ($f in $model.Files) {
    $target = Join-Path $folder $f.Name
    if (Test-Hash $target $f.Sha256) {
      Write-Host "  OK  $($model.Folder)\$($f.Name)"
      continue
    }

    Write-Host "  Downloading $($model.Folder)\$($f.Name) ..."
    $temp = "$target.download"
    try {
      Invoke-WebRequest -Uri "$($model.Base)/$($f.Name)" -OutFile $temp -UseBasicParsing
      if (-not (Test-Hash $temp $f.Sha256)) {
        throw "SHA-256 mismatch for $($f.Name) - download corrupted or file changed upstream."
      }
      Move-Item -LiteralPath $temp -Destination $target -Force
      Write-Host "  OK  $($model.Folder)\$($f.Name)"
    }
    finally {
      if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
    }
  }
}
Write-Host "AI models ready: $Destination"
