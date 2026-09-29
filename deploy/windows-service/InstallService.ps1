# Auto-elevate to Administrator
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -ArgumentList "-NoExit -NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$ErrorActionPreference = "Stop"

# Hardcoded settings
$ServiceName = "InstantAIGate.Server"
$DisplayName = "InstantAIGate AI Inference Server"
$PublicPort  = 5000
$AdminPort   = 5001
$AdminApiKey = "ChangeMeSuperSecureAdminApiKey123"

# Installation target directory
$InstallDir  = "C:\ProgramData\InstantAIGate\Server"
$ExePath     = Join-Path $InstallDir "InstantAIGate.Server.exe"
$ConfigPath  = Join-Path $InstallDir "appsettings.json"

# Search for .csproj or .sln
$SearchDir  = $PSScriptRoot
$TargetFile = $null

while ($SearchDir) {
    $found = Get-ChildItem -Path $SearchDir -Filter "InstantAIGate.Server.csproj" -File -Recurse -Depth 2 -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $found) {
        $found = Get-ChildItem -Path $SearchDir -Filter "*.sln" -File -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    if ($found) {
        $TargetFile = $found.FullName
        break
    }
    $parent = Split-Path -Path $SearchDir -Parent
    if ($parent -eq $SearchDir) { break }
    $SearchDir = $parent
}

if (-not $TargetFile) {
    Write-Error "Project or solution file could not be found automatically. Please check directory structure."
    Read-Host "Press Enter to exit"
    exit 1
}

# Ensure destination directory exists
if (-not (Test-Path $InstallDir)) {
    Write-Host ">>> Creating directory $InstallDir..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

# Stop existing service before publishing
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host ">>> Stopping existing service before build/publish..." -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
}

Write-Host ">>> Found target: $TargetFile" -ForegroundColor Cyan
Write-Host ">>> Building and publishing project to $InstallDir..." -ForegroundColor Cyan
dotnet publish "$TargetFile" -c Release -o "$InstallDir" --nologo

# Patch existing full appsettings.json without dropping other properties
if (Test-Path $ConfigPath) {
    Write-Host ">>> Updating ports and API key in appsettings.json..." -ForegroundColor Cyan
    $settings = Get-Content $ConfigPath -Raw | ConvertFrom-Json

    if (-not $settings.Kestrel) { $settings | Add-Member -MemberType NoteProperty -Name "Kestrel" -Value ([PSCustomObject]@{}) }
    if (-not $settings.Kestrel.Endpoints) { $settings.Kestrel | Add-Member -MemberType NoteProperty -Name "Endpoints" -Value ([PSCustomObject]@{}) }
    if (-not $settings.Kestrel.Endpoints.PublicEndpoint) { $settings.Kestrel.Endpoints | Add-Member -MemberType NoteProperty -Name "PublicEndpoint" -Value ([PSCustomObject]@{}) }
    if (-not $settings.Kestrel.Endpoints.AdminEndpoint) { $settings.Kestrel.Endpoints | Add-Member -MemberType NoteProperty -Name "AdminEndpoint" -Value ([PSCustomObject]@{}) }
    if (-not $settings.InstantAIGate) { $settings | Add-Member -MemberType NoteProperty -Name "InstantAIGate" -Value ([PSCustomObject]@{}) }

    $settings.Kestrel.Endpoints.PublicEndpoint.Url = "http://0.0.0.0:$PublicPort"
    $settings.Kestrel.Endpoints.AdminEndpoint.Url = "http://0.0.0.0:$AdminPort"
    $settings.InstantAIGate.AdminApiKey = $AdminApiKey

    $settings | ConvertTo-Json -Depth 20 | Set-Content -Path $ConfigPath -Encoding UTF8
}

Write-Host ">>> Registering Windows Service..." -ForegroundColor Cyan
New-Service -Name $ServiceName `
    -BinaryPathName "`"$ExePath`"" `
    -DisplayName $DisplayName `
    -StartupType Automatic

Write-Host ">>> Starting service..." -ForegroundColor Cyan
Start-Service -Name $ServiceName

Write-Host ""
Write-Host "=== SERVICE INSTALLED AND STARTED SUCCESSFULLY ===" -ForegroundColor Green
Write-Host "Install Path : $InstallDir"
Write-Host "Public Port  : $PublicPort"
Write-Host "Admin Port   : $AdminPort"
Write-Host "Admin Key    : $AdminApiKey"
Write-Host "=================================================" -ForegroundColor Green
Write-Host ""

Read-Host "Press Enter to exit"