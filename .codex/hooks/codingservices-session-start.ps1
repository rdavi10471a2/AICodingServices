$ErrorActionPreference = 'SilentlyContinue'

$repoRoot = 'C:\VSCodeProjects\CodingServices'
$currentPath = (Get-Location).Path

if (-not $currentPath.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    exit 0
}

$runtimeRoot = Join-Path $repoRoot 'runtime'
$logRoot = Join-Path $runtimeRoot 'codex-startup'
$logPath = Join-Path $logRoot 'session-start.log'
$siteUrl = 'http://localhost:5000/'
$codexUiDll = Join-Path $repoRoot 'src\CodexUI\bin\Debug\net10.0\CodexUI.dll'

function Write-StartupLog {
    param(
        [string]$Message
    )

    $timestamp = Get-Date -Format o
    Add-Content -Path $logPath -Value "[$timestamp] $Message"
}

New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
Write-StartupLog 'SessionStart hook running.'

$siteHealthy = $false
try {
    $response = Invoke-WebRequest -Uri $siteUrl -UseBasicParsing -TimeoutSec 5
    if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
        $siteHealthy = $true
        Write-StartupLog "CodexUI reachable at $siteUrl with HTTP $($response.StatusCode)."
    }
} catch {
    Write-StartupLog "CodexUI probe failed: $($_.Exception.Message)"
}

if (-not $siteHealthy) {
    if (Test-Path $codexUiDll) {
        Write-StartupLog "Starting CodexUI from $codexUiDll."
        Start-Process -FilePath 'dotnet' -ArgumentList @($codexUiDll, '--urls', $siteUrl) -WorkingDirectory $repoRoot -WindowStyle Hidden | Out-Null
        Start-Sleep -Seconds 3
    } else {
        Write-StartupLog "CodexUI binary was not found at $codexUiDll. Build the project before relying on automatic startup."
    }
}

try {
    $status = & codex mcp get aicodingservices 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-StartupLog 'aicodingservices MCP registration is visible to Codex.'
    } else {
        Write-StartupLog "aicodingservices MCP registration check returned exit code ${LASTEXITCODE}: $status"
    }
} catch {
    Write-StartupLog "aicodingservices MCP registration check failed: $($_.Exception.Message)"
}

Write-StartupLog 'SessionStart hook complete. First model turn should call initialize_coding_services through mounted MCP.'
exit 0
