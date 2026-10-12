# tools/download-ffmpeg.ps1 — downloads ffmpeg.exe + ffprobe.exe into
# src\PianAssistant\Tools\ffmpeg\ (gitignored by .gitignore; first-clone prep).
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$target = Join-Path $projectRoot 'src\PianAssistant\Tools\ffmpeg'
if (-not (Test-Path -LiteralPath $target)) {
    New-Item -ItemType Directory -Path $target -Force | Out-Null
}

# ffmpeg release essentials for Windows x64 — gpl variant, static, from
# https://www.gyan.dev/ffmpeg/builds/ (canonical Windows distribution).
$sourceBase = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
$temp = Join-Path $env:TEMP "pianassistant-ffmpeg-$(Get-Random).zip"

Write-Host "[PianAssistant] Downloading ffmpeg essentials to $temp ..."
Invoke-WebRequest -Uri $sourceBase -OutFile $temp -UseBasicParsing

Write-Host "[PianAssistant] Extracting to $target ..."
$extractRoot = Join-Path $env:TEMP "pianassistant-ffmpeg-extract-$(Get-Random)"
Expand-Archive -Path $temp -DestinationPath $extractRoot -Force

$binDir = Get-ChildItem -Path $extractRoot -Recurse -Filter 'ffmpeg.exe' |
    Select-Object -First 1 -ExpandProperty DirectoryName
if (-not $binDir) { throw "ffmpeg.exe not found in extracted archive." }

Copy-Item -Path (Join-Path $binDir 'ffmpeg.exe') -Destination (Join-Path $target 'ffmpeg.exe') -Force
Copy-Item -Path (Join-Path $binDir 'ffprobe.exe') -Destination (Join-Path $target 'ffprobe.exe') -Force

Write-Host "[PianAssistant] Done. ffmpeg + ffprobe installed at $target"
Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
