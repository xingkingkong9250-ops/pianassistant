# run.ps1 — Debug 构建 + 关闭旧实例 + 启动。
# 通过 run.bat 静默调用，也可直接 powershell -File run.ps1 调试。
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSCommandPath
$solutionPath = Join-Path $projectRoot 'PianAssistant.sln'
$csprojPath = Join-Path $projectRoot 'src\PianAssistant\PianAssistant.csproj'
$logPath = Join-Path $env:TEMP 'PianAssistant.run.log'

function Write-Log([string]$msg) {
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $msg
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    Write-Host $line
}

function Get-ProjectVersion {
    [xml]$csproj = Get-Content -LiteralPath $csprojPath -Raw -Encoding UTF8
    return $csproj.Project.PropertyGroup.Version.Trim()
}

try {
    Write-Log "=== PianAssistant run started ==="

    # .NET SDK 检查
    $out = & dotnet --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ".NET SDK not found. Install .NET 10 SDK (win-x64). Output: $out"
    }
    Write-Log ".NET SDK OK"

    # 关闭已有实例（单实例模式：互斥锁已被旧进程持有，直接结束进程）
    $existing = Get-Process PianAssistant -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Log "Existing instance found (PID $($existing.Id)). Stopping..."
        $existing | Stop-Process -Force
        Start-Sleep -Seconds 2
    }

    $version = Get-ProjectVersion
    Write-Log "Project version: $version"

    Write-Log "Building Debug..."
    $out = & dotnet build $solutionPath -c Debug --nologo 2>&1
    $out | Out-File -LiteralPath $logPath -Append -Encoding UTF8
    if ($LASTEXITCODE -ne 0) {
        Write-Log "Debug build failed (exit $LASTEXITCODE). See log: $logPath"
        exit 1
    }
    Write-Log "Build OK."

    # PlatformTarget=x64 导致输出在 bin\x64\Debug\ 下
    $exe = Join-Path $projectRoot "src\PianAssistant\bin\x64\Debug\net10.0-windows\PianAssistant.exe"
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "Built exe not found: $exe"
    }

    $process = Start-Process -FilePath $exe -PassThru
    Write-Log "Launched PianAssistant (PID $($process.Id))."
}
catch {
    Write-Log "FATAL: $($_.Exception.Message)"
    throw
}
