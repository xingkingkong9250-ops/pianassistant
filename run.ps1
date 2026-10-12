# run.ps1 — silent Debug build + single-instance graceful restart + launch.
# Mirrors StellarHub run.ps1 convention.
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSCommandPath
$solutionPath = Join-Path $projectRoot 'PianAssistant.sln'
$csprojPath = Join-Path $projectRoot 'src\PianAssistant\PianAssistant.csproj'
$logPath = Join-Path $env:TEMP 'PianAssistant.run.log'
$pidFile = Join-Path $env:TEMP 'PianAssistant.dev.pid'

function Write-Log([string]$msg) {
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $msg
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
}

function Get-ProjectVersion {
    [xml]$csproj = Get-Content -LiteralPath $csprojPath -Raw -Encoding UTF8
    return $csproj.Project.PropertyGroup.Version.Trim()
}

function Test-DotNetSdk {
    $out = & dotnet --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ".NET SDK not found. Install .NET 10 SDK (win-x64). Output: $out"
    }
}

function Invoke-GracefulShutdown {
    $event = New-Object System.Threading.EventHandle(
        $false, $false, 'Local\PianAssistant_GracefulShutdown'
    ) -ErrorAction SilentlyContinue
    if ($null -ne $event) {
        try {
            $event.Set()
            Write-Log "Sent graceful shutdown signal. Waiting up to 8s..."
            $event = [System.Threading.EventWaitHandle]::OpenExisting(
                'Local\PianAssistant_GracefulShutdown'
            ) -ErrorAction SilentlyContinue
        } catch { }
    }
}

try {
    Write-Log "=== PianAssistant run started ==="

    Test-DotNetSdk
    Write-Log ".NET SDK OK"

    # Single-instance graceful restart
    $mutexName = 'Global\PianAssistant_SingleInstance'
    $created = $false
    $mutex = New-Object System.Threading.Mutex($false, $mutexName, [ref]$created)
    if (-not $created) {
        Write-Log "Existing instance detected; signaling graceful shutdown."
        Invoke-GracefulShutdown
        Start-Sleep -Seconds 8
    }

    $version = Get-ProjectVersion
    Write-Log "Project version: $version"

    Write-Log "Building Debug..."
    $out = & dotnet build $solutionPath -c Debug --nologo 2>&1
    $out | Out-File -LiteralPath $logPath -Append -Encoding UTF8
    if ($LASTEXITCODE -ne 0) {
        Write-Log "Debug build failed (exit $LASTEXITCODE). See log for details."
        exit 1
    }
    Write-Log "Build OK."

    $exe = Join-Path $projectRoot "src\PianAssistant\bin\Debug\net10.0-windows\PianAssistant.exe"
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "Built exe not found at expected path: $exe"
    }

    $process = Start-Process -FilePath $exe -PassThru
    Set-Content -LiteralPath $pidFile -Value $process.Id -Encoding UTF8
    Write-Log "Launched PianAssistant (PID $($process.Id))."
}
catch {
    Write-Log "FATAL: $($_.Exception.Message)"
    throw
}
