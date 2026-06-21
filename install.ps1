# =====================================================================
# InstantAIGate - Production Deployment (NATIVE POWERSHELL SERVICES)
# =====================================================================

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Administrator rights are required. Please open PowerShell as Administrator."
    exit
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$ProgressPreference = 'SilentlyContinue'

# --- SHA256 HASHES ---
$KnownHashes = @{
    "api-win-x64.zip"     = "4d4a137aac2b79f433e10f46c91cf7b7f53ca80f2d768c212fb8802837b62661"
    "admin-win-x64.zip"   = "ee07217c1e8ca2d2ede0bf760b22131404ce1be4455dcfc4924a9dce3c1c058c"
    "runtime-win-x64.zip" = "d2b26c02b21e1aeeeb60253740f50cefda98685987735f26fab8ee6c0b7f7aef"
}

$apiZipUrl      = "https://github.com/Instancium/instant-ai-gate/releases/download/v1.0.3/api-win-x64.zip" 
$adminZipUrl    = "https://github.com/Instancium/instant-ai-gate/releases/download/v1.0.3/admin-win-x64.zip"
$runtimeZipUrl  = "https://github.com/Instancium/instant-ai-gate/releases/download/v1.0.3/runtime-win-x64.zip"

$apiPort   = 49154
$adminPort = 49155

$baseAppDir       = Join-Path -Path $env:ProgramFiles -ChildPath "InstantAIGate"
$apiDirectory     = Join-Path -Path $baseAppDir -ChildPath "API"
$adminDirectory   = Join-Path -Path $baseAppDir -ChildPath "Admin"
$runtimeDirectory = Join-Path -Path $apiDirectory -ChildPath ".runtimes"
$dataDirectory    = Join-Path -Path $env:ProgramData -ChildPath "InstantAIGate\Models"
$tempDir          = Join-Path -Path $env:TEMP -ChildPath "InstantAIGateDeploy"

New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
foreach ($dir in @($apiDirectory, $adminDirectory, $runtimeDirectory, $dataDirectory)) {
    if (-not (Test-Path -Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
}

function Invoke-SafeDownload {
    param([string]$url, [string]$zipPath, [string]$fileName)
    
    $expectedHash = $KnownHashes[$fileName]
    $valid = $false
    
    if (Test-Path $zipPath) {
        $actualHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLower()
        if ($actualHash -eq $expectedHash.ToLower()) {
            Write-Host "Hash for $fileName is correct." -ForegroundColor Green
            $valid = $true
        } else {
            Write-Host "Hash mismatch! Removing corrupted $fileName..." -ForegroundColor Red
            Remove-Item $zipPath -Force
        }
    }

    if (-not $valid) {
        Write-Host "Downloading $fileName..." -ForegroundColor Cyan
        try {
            $wc = New-Object System.Net.WebClient
            $wc.Headers.Add("User-Agent", "Mozilla/5.0")
            $wc.DownloadFile($url, $zipPath)
            
            $newHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLower()
            if ($newHash -ne $expectedHash.ToLower()) { throw "Downloaded file $fileName is corrupted! Hash: $newHash" }
        }
        catch {
            Write-Error "Failed to download $fileName. Error: $_"
            exit
        }
    }
}

Stop-Service -Name "InstantAIGate_API" -ErrorAction SilentlyContinue
Stop-Service -Name "InstantAIGate_Admin" -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Invoke-SafeDownload $apiZipUrl "$tempDir\api-win-x64.zip" "api-win-x64.zip"
Invoke-SafeDownload $adminZipUrl "$tempDir\admin-win-x64.zip" "admin-win-x64.zip"
Invoke-SafeDownload $runtimeZipUrl "$tempDir\runtime-win-x64.zip" "runtime-win-x64.zip"

Expand-Archive -Path "$tempDir\api-win-x64.zip" -DestinationPath $apiDirectory -Force
Expand-Archive -Path "$tempDir\admin-win-x64.zip" -DestinationPath $adminDirectory -Force
Expand-Archive -Path "$tempDir\runtime-win-x64.zip" -DestinationPath $runtimeDirectory -Force

foreach ($d in @($apiDirectory, $adminDirectory)) {
    if (Test-Path "$d\publish") {
        Copy-Item "$d\publish\*" $d -Recurse -Force
        Remove-Item "$d\publish" -Recurse -Force
    }
}

$apiConfigPath   = Join-Path $apiDirectory "appsettings.json"
$adminConfigPath = Join-Path $adminDirectory "appsettings.json"
$secureApiKey    = [guid]::NewGuid().ToString("N") + [guid]::NewGuid().ToString("N")

if (Test-Path $apiConfigPath) {
    $apiJson = Get-Content $apiConfigPath -Raw | ConvertFrom-Json
    $apiJson.Storage.RootPath = $dataDirectory
    $apiJson.ApiKeyOptions.AdminKey = $secureApiKey
    if ($apiJson.CorsSettings) { $apiJson.CorsSettings.AllowedOrigins = @("http://localhost:$adminPort", "http://127.0.0.1:$adminPort") }
    $apiJson | Add-Member -MemberType NoteProperty -Name "Urls" -Value "http://*:$apiPort" -Force
    $apiJson | ConvertTo-Json -Depth 10 | Set-Content $apiConfigPath
}
if (Test-Path $adminConfigPath) {
    $adminJson = Get-Content $adminConfigPath -Raw | ConvertFrom-Json
    $adminJson.APIClientOptions.AdminApiKey = $secureApiKey
    $adminJson.APIClientOptions.BaseUrl = "http://127.0.0.1:$apiPort"
    $adminJson.APIClientOptions.PublicUrl = "http://127.0.0.1:$apiPort"
    $adminJson | Add-Member -MemberType NoteProperty -Name "Urls" -Value "http://*:$adminPort" -Force
    $adminJson | ConvertTo-Json -Depth 10 | Set-Content $adminConfigPath
}

$apiExePath   = Join-Path $apiDirectory "InstantAIGate.API.exe"
$adminExePath = Join-Path $adminDirectory "InstantAIGate.Admin.exe"

# REMOVE AND RECREATE SERVICES USING NATIVE CMDLETS
if (Get-Service -Name "InstantAIGate_API" -ErrorAction SilentlyContinue) { 
    Stop-Service -Name "InstantAIGate_API" -Force
    sc.exe delete "InstantAIGate_API" 
}
if (Get-Service -Name "InstantAIGate_Admin" -ErrorAction SilentlyContinue) { 
    Stop-Service -Name "InstantAIGate_Admin" -Force
    sc.exe delete "InstantAIGate_Admin" 
}
Start-Sleep -Seconds 2

New-Service -Name "InstantAIGate_API" -BinaryPathName "$apiExePath --run-as-service" -DisplayName "InstantAIGate API" -StartupType Automatic
New-Service -Name "InstantAIGate_Admin" -BinaryPathName "$adminExePath --run-as-service" -DisplayName "InstantAIGate Admin" -StartupType Automatic

Start-Service -Name "InstantAIGate_API"
Start-Service -Name "InstantAIGate_Admin"

New-NetFirewallRule -DisplayName "InstantAIGate API" -Direction Inbound -LocalPort $apiPort -Protocol TCP -Action Allow -ErrorAction SilentlyContinue | Out-Null
New-NetFirewallRule -DisplayName "InstantAIGate Admin" -Direction Inbound -LocalPort $adminPort -Protocol TCP -Action Allow -ErrorAction SilentlyContinue | Out-Null

Write-Host "=====================================================================" -ForegroundColor Green
Write-Host "Deployment Complete! Services are active." -ForegroundColor Green
Write-Host "API: http://localhost:$apiPort | Admin: http://localhost:$adminPort" -ForegroundColor Cyan
Write-Host "=====================================================================" -ForegroundColor Green