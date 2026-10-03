# Auto-elevate to Administrator
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -ArgumentList "-NoExit -NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$ErrorActionPreference = "Stop"

# Configuration settings
$ServiceName = "InstantAIGate.Server"
$DisplayName = "InstantAIGate AI Inference Server"
$Port        = 5000
$AdminApiKey = "ChangeMeSuperSecureAdminApiKey123"

# Installation paths
$InstallDir  = "C:\ProgramData\InstantAIGate\Server"
$ExePath     = Join-Path $InstallDir "InstantAIGate.Server.exe"
$ConfigPath  = Join-Path $InstallDir "appsettings.json"

# Search for InstantAIGate.Server.csproj or solution directory
$SearchDir   = $PSScriptRoot
$ProjectFile = $null
$RootDir     = $null

while ($SearchDir) {
    $candidateProj = Join-Path $SearchDir "src\InstantAIGate.Server\InstantAIGate.Server.csproj"
    if (Test-Path $candidateProj) {
        $ProjectFile = (Get-Item $candidateProj).FullName
        $RootDir = $SearchDir
        break
    }

    $foundFile = Get-ChildItem -Path $SearchDir -Filter "InstantAIGate.Server.csproj" -File -Recurse -Depth 3 -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($foundFile) {
        $ProjectFile = $foundFile.FullName
        $slnCheck = Get-Item (Join-Path $foundFile.Directory.Parent.Parent.FullName "InstantAIGate.sln") -ErrorAction SilentlyContinue
        if ($slnCheck) {
            $RootDir = $slnCheck.Directory.FullName
        }
        break
    }

    $parent = Split-Path -Path $SearchDir -Parent
    if ($parent -eq $SearchDir) { break }
    $SearchDir = $parent
}

if (-not $ProjectFile) {
    Write-Error "Could not locate InstantAIGate.Server.csproj."
    Read-Host "Press Enter to exit"
    exit 1
}

# Stop and remove existing service if already registered
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host ">>> Stopping existing service before build and publish..." -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
}

# Terminate orphaned process if still running
$orphanedProcess = Get-Process -Name "InstantAIGate.Server" -ErrorAction SilentlyContinue
if ($orphanedProcess) {
    Stop-Process -Name "InstantAIGate.Server" -Force -ErrorAction SilentlyContinue
}

# Ensure destination directory exists
if (-not (Test-Path $InstallDir)) {
    Write-Host ">>> Creating directory $InstallDir..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

Write-Host ">>> Publishing project: $ProjectFile" -ForegroundColor Cyan
dotnet publish "$ProjectFile" -c Release -r win-x64 --self-contained false -o "$InstallDir" --nologo

# Copy native runtime dependencies (llama.dll, mtmd.dll, etc.)
if ($RootDir) {
    $NativeSourceDir = Join-Path $RootDir "src\InstantAIGate.Native\runtimes\win-x64"
    if (Test-Path $NativeSourceDir) {
        Write-Host ">>> Deploying native runtime binaries..." -ForegroundColor Cyan
        Get-ChildItem -Path $NativeSourceDir -Filter "*.dll" | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination $InstallDir -Force
        }
    }

    # Ensure model catalog config exists
    $CatalogSource = Join-Path $RootDir "config\model_catalog.json"
    $ConfigDir = Join-Path $InstallDir "config"
    if (Test-Path $CatalogSource) {
        Write-Host ">>> Deploying model catalog..." -ForegroundColor Cyan
        if (-not (Test-Path $ConfigDir)) {
            New-Item -ItemType Directory -Path $ConfigDir -Force | Out-Null
        }
        Copy-Item -Path $CatalogSource -Destination (Join-Path $ConfigDir "model_catalog.json") -Force
    }
}

# Update appsettings.json
if (Test-Path $ConfigPath) {
    Write-Host ">>> Updating port and API keys in appsettings.json..." -ForegroundColor Cyan
    $settings = Get-Content -Path $ConfigPath -Raw | ConvertFrom-Json

    if (-not $settings.InstantAIGate) {
        $settings | Add-Member -MemberType NoteProperty -Name "InstantAIGate" -Value ([PSCustomObject]@{})
    }
    $settings.InstantAIGate.AdminApiKey = $AdminApiKey

    if (-not $settings.Kestrel) {
        $settings | Add-Member -MemberType NoteProperty -Name "Kestrel" -Value ([PSCustomObject]@{})
    }
    if (-not $settings.Kestrel.Endpoints) {
        $settings.Kestrel | Add-Member -MemberType NoteProperty -Name "Endpoints" -Value ([PSCustomObject]@{})
    }
    if (-not $settings.Kestrel.Endpoints.Http) {
        $settings.Kestrel.Endpoints | Add-Member -MemberType NoteProperty -Name "Http" -Value ([PSCustomObject]@{})
    }
    $settings.Kestrel.Endpoints.Http.Url = "http://0.0.0.0:$Port"

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
Write-Host "Service Port : $Port"
Write-Host "Admin Key    : $AdminApiKey"
Write-Host "=================================================" -ForegroundColor Green
Write-Host ""

Read-Host "Press Enter to exit"