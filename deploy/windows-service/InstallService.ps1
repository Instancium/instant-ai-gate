# PowerShell script to register InstantAIGate.Server as a Windows Service
# Requires administrative privileges

param(
    [string]$ServiceName = "InstantAIGate",
    [string]$DisplayName = "InstantAIGate Server",
    [string]$Description = "High-performance local AI inference and model management service",
    [string]$InstallPath = "C:\Program Files\InstantAIGate"
)

# Check if running as administrator
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Error "This script must be run as Administrator."
    exit 1
}

# Stop existing service if it exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Stopping existing service..."
    Stop-Service -Name $ServiceName -Force
    sc.exe delete $ServiceName
}

# Create new service
Write-Host "Installing service..."
sc.exe create $ServiceName binPath= "\"$InstallPath\InstantAIGate.Server.exe\"" start= auto DisplayName= "$DisplayName"

# Set service description
sc.exe description $ServiceName "$Description"

# Configure recovery options
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/300000/restart/600000

# Start the service
Write-Host "Starting service..."
Start-Service -Name $ServiceName

Write-Host "Service installed and started successfully."
Write-Host "Service Name: $ServiceName"
Write-Host "Display Name: $DisplayName"
