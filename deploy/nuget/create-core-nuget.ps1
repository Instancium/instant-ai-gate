$ErrorActionPreference = "Stop"


$rootDir = (Resolve-Path "$PSScriptRoot\..\..").Path

$serverProjectFile = Join-Path $rootDir "src\InstantAIGate.Server\InstantAIGate.Server.csproj"
$coreProjectFile = Join-Path $rootDir "src\InstantAIGate.Core\InstantAIGate.Core.csproj"


$outputDir = Join-Path $rootDir "src\InstantAIGate.Core\bin\Release"

Write-Host "Resolved Solution Root: $rootDir" -ForegroundColor DarkGray
Write-Host "Extracting version from Server project..." -ForegroundColor Cyan


if (-not (Test-Path $serverProjectFile)) {
    Write-Host "Error: Cannot find $serverProjectFile" -ForegroundColor Red
    exit 1
}

$serverContent = Get-Content $serverProjectFile -Raw


$versionMatch = [regex]::Match($serverContent, '<Version>(.*?)</Version>')

if ($versionMatch.Success) {
    $Version = $versionMatch.Groups[1].Value.Trim()
    Write-Host "Target version extracted from Server: $Version" -ForegroundColor Green
} else {
    Write-Host "Error: Could not find <Version> element in $serverProjectFile" -ForegroundColor Red
    exit 1
}

Write-Host "Building and packing InstantAIGate.Core v$Version..." -ForegroundColor Cyan


dotnet pack $coreProjectFile -c Release -p:PackageVersion=$Version -o $outputDir

Write-Host "=======================================================" -ForegroundColor Green
Write-Host "SUCCESS: Package generated successfully." -ForegroundColor Green
Write-Host "You can find the .nupkg file for manual upload here:" -ForegroundColor Yellow
Write-Host (Resolve-Path $outputDir).Path -ForegroundColor Yellow