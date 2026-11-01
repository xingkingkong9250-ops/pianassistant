# PianAssistant build/release script.
# Prepare runs the full local gate (Release build with zero warnings,
# all tests, self-contained staging publish).
# See GEMINI.md section 7 for the contract.
#
# NOTE: this file MUST be saved as UTF-8 WITH BOM. Windows PowerShell 5.1
# reads BOM-less UTF-8 as ANSI/GBK on zh-CN systems; CJK punctuation byte
# pairs can swallow LF newlines and silently corrupt parsing.
[CmdletBinding()]
param(
    [ValidateSet('Prepare', 'Publish')]
    [string]$Mode = 'Prepare'
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSCommandPath
$solutionPath = Join-Path $projectRoot 'PianAssistant.sln'
$csprojPath = Join-Path $projectRoot 'src\PianAssistant\PianAssistant.csproj'

<#
    Prepare gate: restore + Release zero-warning build + all tests +
    self-contained win-x64 ReadyToRun publish into
    artifacts\staging\PianAssistant_v{version}\.
#>
function Invoke-PrepareGate {
    param([Parameter(Mandatory = $true)][string]$Version)

    $staging = Join-Path $projectRoot "artifacts\staging\PianAssistant_v$Version"

    # Native command stdout MUST be captured and re-emitted via Write-Host:
    # un-captured stdout enters the function output pipeline and pollutes
    # the return value. Do NOT use 2>&1: on PS 5.1 with
    # $ErrorActionPreference='Stop' a stderr redirect raises NativeCommandError.
    Write-Host "[PianAssistant] Prepare v$Version : restore"
    $out = & dotnet restore $solutionPath --nologo
    $out | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

    Write-Host "[PianAssistant] Prepare v$Version : Release build (zero-warning gate)"
    $out = & dotnet build $solutionPath -c Release --nologo
    $out | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    Write-Host "[PianAssistant] Prepare v$Version : tests"
    $out = & dotnet test $solutionPath -c Release --nologo --no-build --verbosity minimal
    $out | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

    Write-Host "[PianAssistant] Prepare v$Version : self-contained publish (win-x64, ReadyToRun)"
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
    $out = & dotnet publish $csprojPath `
        -c Release -r win-x64 --self-contained true `
        -p:PublishReadyToRun=true `
        --nologo -o $staging
    $out | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

    return $staging
}

function Get-ProjectVersion {
    param([Parameter(Mandatory = $true)][string]$CsprojPath)
    [xml]$csproj = Get-Content -LiteralPath $CsprojPath -Raw -Encoding UTF8
    $versionNode = $csproj.Project.PropertyGroup.Version
    if ($null -eq $versionNode) {
        throw "Version not found in $CsprojPath"
    }
    return $versionNode.Trim()
}

<#
    Search for Inno Setup ISCC.exe in common install locations.
    Returns the first found absolute path, or $null if none found.
    Search order (checked top-to-bottom):
      1. %ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
      2. %ProgramFiles%\Inno Setup 6\ISCC.exe
      3. %ProgramFiles(x86)%\Inno Setup 5\ISCC.exe
      4. %ProgramFiles%\Inno Setup 5\ISCC.exe
      5. %LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe   (user-level install)
      6. %LOCALAPPDATA%\Programs\Inno Setup 5\ISCC.exe
      7. %APPDATA%\Inno Setup 6\ISCC.exe
      8. %APPDATA%\Inno Setup 5\ISCC.exe
      9. HKLM\SOFTWARE\...\Uninstall\ & HKCU\...\Uninstall\
         Inno Setup 6_is1 / Inno Setup 5_is1 → InstallLocation
     10. PATH (Get-Command ISCC)
    NOTE: must be defined BEFORE the switch below — PowerShell executes a
    script top-to-bottom and does not hoist function definitions.
#>
function Get-IsccPath {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 5\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 5\ISCC.exe",
        "${env:APPDATA}\Inno Setup 6\ISCC.exe",
        "${env:APPDATA}\Inno Setup 5\ISCC.exe"
    )

    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    # Fallback: check registry for uninstall entries (HKLM + HKCU)
    $regRoots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
    )
    foreach ($regRoot in $regRoots) {
        if (Test-Path -LiteralPath $regRoot) {
            foreach ($key in (Get-ChildItem -Path $regRoot -ErrorAction SilentlyContinue)) {
                $displayName = $key.GetValue('DisplayName', '')
                if ($displayName -like 'Inno Setup *') {
                    $installDir = $key.GetValue('InstallLocation', '')
                    if (-not $installDir) { $installDir = $key.GetValue('DisplayIcon', '') }
                    if ($installDir) {
                        # DisplayIcon may point directly to ISCC.exe
                        if ($installDir -like '*ISCC.exe' -and (Test-Path -LiteralPath $installDir)) {
                            return $installDir
                        }
                        $tryPath = Join-Path $installDir 'ISCC.exe'
                        if (Test-Path -LiteralPath $tryPath) {
                            return $tryPath
                        }
                    }
                }
            }
        }
    }

    # Last resort: PATH
    $cmd = Get-Command ISCC -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    return $null
}

switch ($Mode) {
    'Prepare' {
        $version = Get-ProjectVersion -CsprojPath $csprojPath
        $staging = Invoke-PrepareGate -Version $version
        Write-Host "[PianAssistant] Prepare complete: $staging"
    }
    'Publish' {
        $version = Get-ProjectVersion -CsprojPath $csprojPath
        Write-Host "[PianAssistant] Publish v$version — start"

        # Step 1: Prepare gate
        $staging = Invoke-PrepareGate -Version $version

        # Step 2: Compile ISCC installer (auto-search common paths)
        $isccPath = Get-IsccPath
        if (-not $isccPath) {
            throw "ISCC.exe not found. Searched: Program Files, Program Files (x86), HKLM registry. Please install Inno Setup 6+ or add ISCC to PATH."
        }
        Write-Host "[PianAssistant] Publish v$version — ISCC: $isccPath"

        $issPath = Join-Path $projectRoot 'installer\PianAssistant.iss'
        $installerOutDir = Join-Path $projectRoot 'artifacts\installer'
        if (-not (Test-Path -LiteralPath $installerOutDir)) {
            New-Item -ItemType Directory -Path $installerOutDir -Force | Out-Null
        }

        Push-Location (Join-Path $projectRoot 'installer')
        try {
            & $isccPath "/DAppVersion=$version" 'PianAssistant.iss'
            if ($LASTEXITCODE -ne 0) { throw "ISCC compilation failed (exit $LASTEXITCODE)." }
        } finally {
            Pop-Location
        }

        $installerExe = Join-Path $installerOutDir "PianAssistant_Setup_v$version.exe"
        if (-not (Test-Path -LiteralPath $installerExe)) {
            throw "Installer exe not found at $installerExe after ISCC compilation."
        }
        Write-Host "[PianAssistant] Publish v$version — installer ready: $installerExe"

        # Step 3: Generate Word user guide
        $guideGenerator = Join-Path $projectRoot 'tools\generate-user-guide.js'
        if (Test-Path -LiteralPath $guideGenerator) {
            Write-Host "[PianAssistant] Publish v$version — generating Word guide"
            Push-Location $projectRoot
            try {
                & node $guideGenerator
                if ($LASTEXITCODE -ne 0) { Write-Warning "Word guide generation exited with $LASTEXITCODE (non-fatal)." }
            } finally {
                Pop-Location
            }
        } else {
            Write-Warning "Word guide generator not found at $guideGenerator — skipping."
        }

        $guideDocx = Join-Path $installerOutDir "拉片助手_安装使用说明.docx"
        $guideDest = Join-Path $installerOutDir "拉片助手_安装使用说明_v$version.docx"
        if (Test-Path -LiteralPath $guideDocx) {
            Copy-Item -LiteralPath $guideDocx -Destination $guideDest -Force
        }

        # Step 4: Compress portable zip
        Write-Host "[PianAssistant] Publish v$version — compressing portable zip"
        $zipPath = Join-Path $installerOutDir "PianAssistant_${version}_win-x64_portable.zip"
        if (Test-Path -LiteralPath $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }
        Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zipPath -CompressionLevel Optimal

        # Step 5: Copy three-piece to Releases
        $releasesRoot = Join-Path $projectRoot '..\Releases\拉片助手'
        $destDir = Join-Path $releasesRoot "v$version"
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null

        Copy-Item -LiteralPath $installerExe -Destination (Join-Path $destDir "PianAssistant_Setup_v$version.exe") -Force
        Copy-Item -LiteralPath $zipPath -Destination (Join-Path $destDir "PianAssistant_${version}_win-x64_portable.zip") -Force
        if (Test-Path -LiteralPath $guideDest) {
            Copy-Item -LiteralPath $guideDest -Destination (Join-Path $destDir "拉片助手_安装使用说明_v$version.docx") -Force
        }

        Write-Host ""
        Write-Host "[PianAssistant] Publish v$version — COMPLETE"
        Write-Host "  Installer : $(Join-Path $destDir "PianAssistant_Setup_v$version.exe")"
        Write-Host "  Portable  : $(Join-Path $destDir "PianAssistant_${version}_win-x64_portable.zip")"
        Write-Host "  UserGuide : $(Join-Path $destDir "拉片助手_安装使用说明_v$version.docx")"
        Write-Host "  Staging   : $staging"
    }
}
