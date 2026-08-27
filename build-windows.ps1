param([string]$Version = "1.1.2")

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $projectRoot "src\AfterSchoolManager\AfterSchoolManager.csproj"
$outputPath = Join-Path $projectRoot "artifacts\win-x64"

Write-Host "Restoring packages..."
dotnet restore $projectPath
if ($LASTEXITCODE -ne 0) { throw "Package restore failed." }

Write-Host "Building AfterSchoolManager v$Version..."
dotnet build $projectPath -c Release --no-restore -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Write-Host "Publishing self-contained Windows app..."
dotnet publish $projectPath `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:Version=$Version `
  -o $outputPath
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed." }

Write-Host "Completed: $outputPath"
Write-Host "To create an installer, run .\build-installer.ps1 after installing Inno Setup 6."
