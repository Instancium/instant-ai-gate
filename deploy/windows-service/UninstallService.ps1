<#
.SYNOPSIS
    Uninstalls the InstantAIGate Windows Service and optionally removes application files.
#>
param (
    [string]$ServiceName = "InstantAIGate.Server",
    [switch]$RemoveFiles,
    [switch]$RemoveShortcut = $true
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
    Start-Sleep -Seconds 2
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
    # Safety check: prevent accidental deletion of root or critical system directories
    if ($InstallDir -and $InstallDir.Length -gt 3) {
        Write-Host "Cleaning up application files from $InstallDir..." -ForegroundColor Yellow
        
        $CurrentScript = $MyInvocation.MyCommand.Name
        
        # Exclude the uninstaller script itself and the 'logs' directory if it exists
        $itemsToDelete = Get-ChildItem -Path $InstallDir -Force | Where-Object { 
            $_.Name -ne $CurrentScript -and $_.Name -ne 'logs' 
        }
        
        foreach ($item in $itemsToDelete) {
            Write-Host "Deleting: $($item.Name)"
            Remove-Item -Path $item.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
        
        Write-Host "Application files cleaned up successfully." -ForegroundColor Green
    } else {
        Write-Host "Skipping file removal to prevent accidental deletion of system directories." -ForegroundColor Red
    }
}

Write-Host "================================================================" -ForegroundColor Yellow
Write-Host "SERVICE UNINSTALLATION COMPLETED" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Yellow