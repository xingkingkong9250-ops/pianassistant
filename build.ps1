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
    [ValidateSet('Prepare')]
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

switch ($Mode) {
    'Prepare' {
        $version = Get-ProjectVersion -CsprojPath $csprojPath
        $staging = Invoke-PrepareGate -Version $version
        Write-Host "[PianAssistant] Prepare complete: $staging"
    }
}
