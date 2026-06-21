# =====================================================================
# InstantAIGate - Full Windows Service Uninstallation Script
# =====================================================================

# 1. ENFORCE ADMINISTRATOR PRIVILEGES
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Administrator rights are required to uninstall services. Please open PowerShell as Administrator."
    exit
}

Write-Host "Starting InstantAIGate Uninstallation..." -ForegroundColor Cyan

# 2. DEFINE VARIABLES
$baseAppDir       = Join-Path -Path $env:ProgramFiles -ChildPath "InstantAIGate"
$dataDirectory    = Join-Path -Path $env:ProgramData -ChildPath "InstantAIGate"

# 3. STOP AND DELETE WINDOWS SERVICES
Write-Host "Stopping and removing Windows Services..."

$apiServiceQuery = Get-Service -Name "InstantAIGate_API" -ErrorAction SilentlyContinue
if ($apiServiceQuery) {
    Stop-Service -Name "InstantAIGate_API" -Force
    Start-Sleep -Seconds 2
    sc.exe delete "InstantAIGate_API" | Out-Null
    Write-Host "Removed service: InstantAIGate_API"
}

$adminServiceQuery = Get-Service -Name "InstantAIGate_Admin" -ErrorAction SilentlyContinue
if ($adminServiceQuery) {
    Stop-Service -Name "InstantAIGate_Admin" -Force
    Start-Sleep -Seconds 2
    sc.exe delete "InstantAIGate_Admin" | Out-Null
    Write-Host "Removed service: InstantAIGate_Admin"
}

# 4. REMOVE FIREWALL RULES
Write-Host "Removing Firewall rules..."
# Using wildcards to catch the rules we created earlier
Remove-NetFirewallRule -DisplayName "InstantAIGate API*" -ErrorAction SilentlyContinue | Out-Null
Remove-NetFirewallRule -DisplayName "InstantAIGate Admin*" -ErrorAction SilentlyContinue | Out-Null
Write-Host "Firewall rules removed."

# 5. REMOVE APPLICATION BINARIES (Program Files)
Write-Host "Removing application files from Program Files..."
if (Test-Path -Path $baseAppDir) {
    Remove-Item -Path $baseAppDir -Recurse -Force
    Write-Host "Removed application directory: $baseAppDir"
} else {
    Write-Host "Application directory not found, skipping." -ForegroundColor DarkGray
}

# 6. OPTIONAL: REMOVE MODELS (ProgramData)
if (Test-Path -Path $dataDirectory) {
    Write-Host ""
    Write-Host "WARNING: The models directory still exists: $dataDirectory" -ForegroundColor Yellow
    $deleteModels = Read-Host "Do you want to delete all downloaded AI models and data? (Y/N)"
    
    if ($deleteModels -eq 'Y' -or $deleteModels -eq 'y') {
        Remove-Item -Path $dataDirectory -Recurse -Force
        Write-Host "Removed models directory: $dataDirectory"
    } else {
        Write-Host "Models directory preserved." -ForegroundColor Green
    }
}

Write-Host "=====================================================================" -ForegroundColor Green
Write-Host "Uninstallation Complete! InstantAIGate has been successfully removed." -ForegroundColor Green
Write-Host "=====================================================================" -ForegroundColor Green