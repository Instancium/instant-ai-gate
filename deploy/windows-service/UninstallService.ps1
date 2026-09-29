# Auto-elevate to Administrator
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -ArgumentList "-NoExit -NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$ErrorActionPreference = "Continue"

# Hardcoded settings
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

# 2. Terminate any orphaned processes if still running
$process = Get-Process -Name "InstantAIGate.Server" -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "Killing running process..."
    Stop-Process -Name "InstantAIGate.Server" -Force -ErrorAction SilentlyContinue
}

# 3. Clean up the installation directory
if (Test-Path $InstallDir) {
    Write-Host ">>> Removing installation directory: $InstallDir..." -ForegroundColor Cyan
    Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
}

# 4. Remove desktop shortcut if present
$DesktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "InstantAIGate API.url"
if (Test-Path $DesktopShortcut) {
    Write-Host "Removing desktop shortcut..."
    Remove-Item -Path $DesktopShortcut -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "=== SERVICE UNINSTALLED AND CLEANED SUCCESSFULLY ===" -ForegroundColor Green
Write-Host "===================================================" -ForegroundColor Green
Write-Host ""

# Keep the window open
Read-Host "Press Enter to exit"