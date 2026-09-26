<#
.SYNOPSIS
    Installs and configures the InstantAIGate Windows Service.
#>
param (
    [string]$ServiceName = "InstantAIGate.Server",
    [string]$DisplayName = "InstantAIGate AI Inference Server",
    [int]$PublicPort = 0,
    [int]$AdminPort = 0,
    [string]$AdminApiKey = "",
    [bool]$CreateDesktopShortcut = $true
)

# Helper function to find a free port
function Get-FreePort {
    param([int]$StartPort = 5000, [int]$EndPort = 5100)
    for ($i = $StartPort; $i -le $EndPort; $i++) {
        $inUse = Get-NetTCPConnection -LocalPort $i -ErrorAction SilentlyContinue
        if (-not $inUse) {
            return $i
        }
    }
    throw "No free ports found in range $StartPort-$EndPort"
}

# Helper function to extract port from URL
function Get-PortFromUrl {
    param([string]$Url)
    if ($Url -match ':(\d+)$') {
        return [int]$matches[1]
    }
    return 0
}

$ErrorActionPreference = "Stop"
$InstallDir = $PSScriptRoot
$ExePath = Join-Path $InstallDir "InstantAIGate.Server.exe"
$AppSettingsPath = Join-Path $InstallDir "appsettings.json"

# Determine if ports were explicitly provided
$explicitPublicPort = $PSBoundParameters.ContainsKey('PublicPort')
$explicitAdminPort = $PSBoundParameters.ContainsKey('AdminPort')
$explicitApiKey = $PSBoundParameters.ContainsKey('AdminApiKey')

# Check if service already exists and read existing config
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
$existingConfig = $null
if (Test-Path $AppSettingsPath) {
    $existingConfig = Get-Content $AppSettingsPath -Raw | ConvertFrom-Json
}

# Resolve Public Port
if ($existingService -and -not $explicitPublicPort -and $existingConfig) {
    $PublicPort = Get-PortFromUrl $existingConfig.Kestrel.Endpoints.PublicEndpoint.Url
} elseif (-not $explicitPublicPort -or $PublicPort -eq 0) {
    $PublicPort = Get-FreePort -StartPort 5000
}

# Resolve Admin Port
if ($existingService -and -not $explicitAdminPort -and $existingConfig) {
    $AdminPort = Get-PortFromUrl $existingConfig.Kestrel.Endpoints.AdminEndpoint.Url
} elseif (-not $explicitAdminPort -or $AdminPort -eq 0) {
    $startAdminPort = if ($PublicPort -gt 0) { $PublicPort + 1 } else { 5000 }
    $AdminPort = Get-FreePort -StartPort $startAdminPort
}

# Dynamically construct the description using the provided ports
$Description = "Enterprise-grade local AI gateway providing secure, autonomous inference with OpenAI-compatible endpoints, robust request queuing, and strict administrative isolation. Public API: port $PublicPort | Admin API: port $AdminPort."

Write-Host "Starting installation for $ServiceName..."
Write-Host "Using Public Port: $PublicPort, Admin Port: $AdminPort"

# 1. Generate secure API key if not provided
if ([string]::IsNullOrWhiteSpace($AdminApiKey)) {
    if ($existingConfig -and $existingConfig.InstantAIGate -and $existingConfig.InstantAIGate.AdminApiKey) {
        $AdminApiKey = $existingConfig.InstantAIGate.AdminApiKey
    } else {
        $rngBytes = New-Object Byte[] 32
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($rngBytes)
        
        # Generate a clean 32-character alphanumeric string
        $AdminApiKey = [System.Convert]::ToBase64String($rngBytes) -replace "[^a-zA-Z0-9]", ""
        $AdminApiKey = $AdminApiKey.Substring(0, 32)
    }
}

# 2. Generate a pristine production configuration
Write-Host "Generating pristine production appsettings.json..."
$prodConfig = @{
    Logging = @{
        LogLevel = @{
            Default = "Information"
            "Microsoft.AspNetCore" = "Warning"
        }
    }
    Kestrel = @{
        Endpoints = @{
            PublicEndpoint = @{
                Url = "http://0.0.0.0:$PublicPort"
            }
            AdminEndpoint = @{
                Url = "http://0.0.0.0:$AdminPort"
            }
        }
    }
    InstantAIGate = @{
        AdminApiKey = $AdminApiKey
    }
    AllowedHosts = "*"
}

# Overwrite the configuration
$prodConfig | ConvertTo-Json -Depth 10 | Set-Content $AppSettingsPath -Encoding UTF8
Write-Host "Clean production configuration generated and saved."

# 3. Register Windows Service
if ($existingService) {
    Write-Host "Service $ServiceName already exists. Stopping and removing..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
}

Write-Host "Registering service in Windows Service Control Manager..."
New-Service -Name $ServiceName `
    -BinaryPathName "`"$ExePath`"" `
    -DisplayName $DisplayName `
    -Description $Description `
    -StartupType Automatic

# 4. Start the Service
Write-Host "Starting $ServiceName..."
Start-Service -Name $ServiceName

# 5. Create Desktop Shortcut
if ($CreateDesktopShortcut) {
    Write-Host "Creating desktop shortcut for the API..."
    $DesktopPath = [Environment]::GetFolderPath("Desktop")
    $ShortcutPath = Join-Path $DesktopPath "InstantAIGate API.url"
    
    # We point to the health/live endpoint so the browser immediately verifies the API is up
    $ShortcutContent = @"
[InternetShortcut]
URL=http://localhost:$PublicPort/health/live
"@
    Set-Content -Path $ShortcutPath -Value $ShortcutContent
    Write-Host "Shortcut created successfully at: $ShortcutPath"
}

Write-Host "================================================================" -ForegroundColor Yellow
Write-Host "SERVICE INSTALLATION SUCCESSFUL" -ForegroundColor Green
Write-Host "IMPORTANT: Please save your Admin API Key below:" -ForegroundColor Yellow
Write-Host "Admin API Key: $AdminApiKey" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Yellow