param([string]$Version = "1.1.2")

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishPath = Join-Path $projectRoot "artifacts\win-x64"
$installerScript = Join-Path $projectRoot "installer\AfterSchoolManager.iss"
$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

& (Join-Path $projectRoot "build-windows.ps1") -Version $Version
if (-not $iscc) {
    throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php and run this script again."
}

Write-Host "Creating Windows installer..."
& $iscc "/DMyAppVersion=$Version" $installerScript
if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }
Write-Host "Completed: $(Join-Path $projectRoot 'artifacts\installer')"
