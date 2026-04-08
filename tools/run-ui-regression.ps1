[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [switch]$IncludeManualGoogle
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repoRoot "artifacts"
$uiArtifactsRoot = Join-Path $artifactsRoot "ui"
$logArtifactsRoot = Join-Path $artifactsRoot "logs"
$appProject = Join-Path $repoRoot "src\CQEPC.TimetableSync.Presentation.Wpf\CQEPC.TimetableSync.Presentation.Wpf.csproj"
$uiTestProject = Join-Path $repoRoot "tests\CQEPC.TimetableSync.Presentation.Wpf.UiTests\CQEPC.TimetableSync.Presentation.Wpf.UiTests.csproj"
$appExe = Join-Path $repoRoot "src\CQEPC.TimetableSync.Presentation.Wpf\bin\$Configuration\net8.0-windows\CQEPC.TimetableSync.Presentation.Wpf.exe"

New-Item -ItemType Directory -Force -Path $uiArtifactsRoot | Out-Null
New-Item -ItemType Directory -Force -Path $logArtifactsRoot | Out-Null

$buildLog = Join-Path $logArtifactsRoot "ui-build.log"
$smokeLog = Join-Path $logArtifactsRoot "ui-smoke.log"

Write-Host "Building WPF app and UI tests..."
dotnet build $appProject -c $Configuration 2>&1 | Tee-Object -FilePath $buildLog
dotnet build $uiTestProject -c $Configuration 2>&1 | Tee-Object -FilePath $buildLog -Append

if (-not (Test-Path $appExe)) {
    throw "Built app executable not found at $appExe"
}

$pages = @("Home", "Import", "Settings")
foreach ($page in $pages) {
    $screenshotPath = Join-Path $uiArtifactsRoot ("{0}.png" -f $page.ToLowerInvariant())
    Write-Host "Generating $page screenshot -> $screenshotPath"
    & $appExe --ui-test --page $page --fixture sample --width 1380 --height 900 --screenshot $screenshotPath 2>&1 |
        Tee-Object -FilePath $smokeLog -Append
    if (-not (Test-Path $screenshotPath)) {
        throw "Expected screenshot was not created at $screenshotPath"
    }
}

Write-Host "Running FlaUI smoke tests..."
if ($IncludeManualGoogle) {
    $env:CQEPC_RUN_MANUAL_UI_TESTS = "1"
    Write-Host "Manual Google UI tests enabled."
}
else {
    Remove-Item Env:CQEPC_RUN_MANUAL_UI_TESTS -ErrorAction SilentlyContinue
    Write-Host "Manual Google UI tests skipped. Pass -IncludeManualGoogle to enable them."
}

try {
    dotnet test $uiTestProject -c $Configuration --no-build --logger "trx;LogFileName=ui-smoke.trx" --results-directory $logArtifactsRoot 2>&1 |
        Tee-Object -FilePath $smokeLog -Append
}
finally {
    if (-not $IncludeManualGoogle) {
        Remove-Item Env:CQEPC_RUN_MANUAL_UI_TESTS -ErrorAction SilentlyContinue
    }
}
