# Auto-elevate to Administrator
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -ArgumentList "-NoExit -NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$ErrorActionPreference = "Continue"

$ServiceName = "InstantAIGate.Server"
$InstallDir  = "C:\ProgramData\InstantAIGate\Server"

Write-Host ">>> Stopping and removing Windows Service: $ServiceName..." -ForegroundColor Cyan

# 1. Stop and remove the service
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Stopping service..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2

    Write-Host "Deleting service..."
    sc.exe delete $ServiceName
    Start-Sleep -Seconds 2
} else {
    Write-Host "Service $ServiceName is not installed." -ForegroundColor Yellow
}

# 2. Terminate running process if orphaned
$process = Get-Process -Name "InstantAIGate.Server" -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "Killing running process..."
    Stop-Process -Name "InstantAIGate.Server" -Force -ErrorAction SilentlyContinue
}

# 3. Clean up installation directory
if (Test-Path $InstallDir) {
    Write-Host "Removing installation directory: $InstallDir..."
    Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "=== SERVICE UNINSTALLED AND CLEANED SUCCESSFULLY ===" -ForegroundColor Green
Write-Host "===================================================" -ForegroundColor Green
Write-Host ""

Read-Host "Press Enter to exit"