param (
    [string]$ServiceName = "InstantAIGate.Server",
    [switch]$RemoveFiles = $false, # Pass -RemoveFiles to delete application files from the directory
    [switch]$RemoveShortcut = $true # Automatically remove the desktop shortcut
)

$ErrorActionPreference = "Stop"
$InstallDir = $PSScriptRoot

Write-Host "Starting uninstallation for $ServiceName..." -ForegroundColor Yellow

# 1. Stop and remove the Windows Service
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    if ($existingService.Status -eq 'Running') {
        Write-Host "Stopping service $ServiceName..."
        Stop-Service -Name $ServiceName -Force
        Start-Sleep -Seconds 2
    }

    Write-Host "Removing service from Windows Service Control Manager..."
    sc.exe delete $ServiceName
    Write-Host "Service successfully removed." -ForegroundColor Green
} else {
    Write-Host "Service $ServiceName is not installed on this system." -ForegroundColor Yellow
}

# 2. Remove Desktop Shortcut
if ($RemoveShortcut) {
    $DesktopPath = [Environment]::GetFolderPath("Desktop")
    $ShortcutPath = Join-Path $DesktopPath "InstantAIGate API.url"
    
    if (Test-Path $ShortcutPath) {
        Remove-Item -Path $ShortcutPath -Force
        Write-Host "Desktop shortcut removed: $ShortcutPath" -ForegroundColor Green
    }
}

# 3. Optional: Remove application files from the installation directory
if ($RemoveFiles) {
    Write-Host "Cleaning up application files from $InstallDir..." -ForegroundColor Yellow
    
    # Get all items in the installation directory except this uninstaller script itself
    $CurrentScript = $MyInvocation.MyCommand.Name
    Get-ChildItem -Path $InstallDir -Exclude $CurrentScript | ForEach-Object {
        Write-Host "Deleting: $_.Name"
        Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
    
    Write-Host "Application files cleaned up successfully." -ForegroundColor Green
}

Write-Host "================================================================" -ForegroundColor Yellow
Write-Host "SERVICE UNINSTALLATION COMPLETED" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Yellow