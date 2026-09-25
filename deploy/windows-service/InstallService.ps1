param (
    [string]$ServiceName = "InstantAIGate.Server",
    [string]$DisplayName = "InstantAIGate AI Inference Server",
    [int]$PublicPort = 5000,
    [int]$AdminPort = 5001,     
    [string]$AdminApiKey = "",
    [bool]$CreateDesktopShortcut =$true
)

$ErrorActionPreference = "Stop"
$InstallDir =$PSScriptRoot
$ExePath = Join-Path$InstallDir "InstantAIGate.Server.exe"
$AppSettingsPath = Join-Path$InstallDir "appsettings.json"

# Dynamically construct the description using the provided ports
$Description = "Enterprise-grade local AI gateway providing secure, autonomous inference with OpenAI-compatible endpoints, robust request queuing, and strict administrative isolation. Public API: port $PublicPort \vert{} Admin API: port$AdminPort."

Write-Host "Starting installation for $ServiceName..."

# 1. Generate secure API key if not provided
if ([string]::IsNullOrWhiteSpace($AdminApiKey)) {
    $rngBytes = New-Object Byte[] 32     [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($rngBytes)
    # Generate a clean 32-character alphanumeric string
    $AdminApiKey = [System.Convert]::ToBase64String($rngBytes) -replace "[^a-zA-Z0-9]", ""
    $AdminApiKey =$AdminApiKey.Substring(0, 32)
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
        # Storage section is intentionally omitted to enforce 
        # the CommonApplicationData fallback in C# runtime.
    }
    AllowedHosts = "*"
}

# Overwrite the developer configuration entirely
$prodConfig \vert{} ConvertTo-Json -Depth 10 \vert{} Set-Content$AppSettingsPath -Encoding UTF8
Write-Host "Clean production configuration generated and saved."

# 3. Register Windows Service
$existingService = Get-Service -Name$ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Service $ServiceName already exists. Stopping and removing..."
    Stop-Service -Name $ServiceName -Force
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
    $ShortcutPath = Join-Path$DesktopPath "InstantAIGate API.url"
    
    # We point to the health/live endpoint so the browser immediately verifies the API is up
    $ShortcutContent = @"
[InternetShortcut]
URL=http://localhost:$PublicPort/health/live
"@
    Set-Content -Path $ShortcutPath -Value$ShortcutContent
    Write-Host "Shortcut created successfully at: $ShortcutPath"
}

Write-Host "================================================================" -ForegroundColor Yellow
Write-Host "SERVICE INSTALLATION SUCCESSFUL" -ForegroundColor Green
Write-Host "IMPORTANT: Please save your Admin API Key below:" -ForegroundColor Yellow
Write-Host "Admin API Key: $AdminApiKey" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Yellow