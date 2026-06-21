# =====================================================================
# InstantAIGate - Full Windows Service Deployment Script
# =====================================================================

# 1. ENFORCE ADMINISTRATOR PRIVILEGES
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Administrator rights are required. Please open PowerShell as Administrator."
    exit
}

Write-Host "Starting InstantAIGate Deployment..." -ForegroundColor Cyan

# 2. DEFINE VARIABLES & DOWNLOAD LINKS
$apiZipUrl      = "https://github.com/Instancium/instant-ai-gate/releases/download/v1.0.2/api-win-x64.zip" 
$adminZipUrl    = "https://github.com/Instancium/instant-ai-gate/releases/download/v1.0.2/admin-win-x64.zip"
$runtimeZipUrl  = "https://github.com/Instancium/instant-ai-gate/releases/download/v1.0.2/instant-ai-gate-runtime-v1.0.2-win-x64.zip"

# Port Bindings
$apiPort   = 49154
$adminPort = 49155

# Strict Windows folder paths
$baseAppDir       = Join-Path -Path $env:ProgramFiles -ChildPath "InstantAIGate"
$apiDirectory     = Join-Path -Path $baseAppDir -ChildPath "API"
$adminDirectory   = Join-Path -Path $baseAppDir -ChildPath "Admin"
$runtimeDirectory = Join-Path -Path $apiDirectory -ChildPath ".runtimes"
$dataDirectory    = Join-Path -Path $env:ProgramData -ChildPath "InstantAIGate\Models"

# Temporary download directory
$tempDir          = Join-Path -Path $env:TEMP -ChildPath "InstantAIGateDeploy"

# 3. PREPARE TEMPORARY DIRECTORY
if (Test-Path -Path $tempDir) { Remove-Item -Path $tempDir -Recurse -Force }
New-Item -ItemType Directory -Path $tempDir | Out-Null

# 4. STOP EXISTING SERVICES (If updating)
Write-Host "Stopping existing services (if any)..."
Stop-Service -Name "InstantAIGate_API" -ErrorAction SilentlyContinue
Stop-Service -Name "InstantAIGate_Admin" -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# 5. CREATE TARGET DIRECTORIES
Write-Host "Creating secure application directories..."
$directories = @($apiDirectory, $adminDirectory, $runtimeDirectory, $dataDirectory)
foreach ($dir in $directories) {
    if (-not (Test-Path -Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

# 6. DOWNLOAD AND EXTRACT FILES
Write-Host "Downloading and extracting API files..."
Invoke-WebRequest -Uri $apiZipUrl -OutFile "$tempDir\api.zip"
Expand-Archive -Path "$tempDir\api.zip" -DestinationPath $apiDirectory -Force

Write-Host "Downloading and extracting Admin files..."
Invoke-WebRequest -Uri $adminZipUrl -OutFile "$tempDir\admin.zip"
Expand-Archive -Path "$tempDir\admin.zip" -DestinationPath $adminDirectory -Force

Write-Host "Downloading and extracting multi-backend Runtime Drivers..."
Invoke-WebRequest -Uri $runtimeZipUrl -OutFile "$tempDir\runtime.zip"
Expand-Archive -Path "$tempDir\runtime.zip" -DestinationPath $runtimeDirectory -Force

if (Test-Path (Join-Path $runtimeDirectory "win-x64\cuda")) {
    Write-Host "[OK] Runtime structure successfully deployed." -ForegroundColor Green
} else {
    Write-Host "[WARNING] Could not verify win-x64\cuda structure. Please check the ZIP contents." -ForegroundColor Yellow
}

# 7. CONFIGURE APPSETTINGS (Auto-generate Secure Keys, Paths & Ports)
Write-Host "Injecting secure configurations and port bindings..."
$apiConfigPath   = Join-Path -Path $apiDirectory -ChildPath "appsettings.json"
$adminConfigPath = Join-Path -Path $adminDirectory -ChildPath "appsettings.json"

$secureApiKey = [guid]::NewGuid().ToString("N") + [guid]::NewGuid().ToString("N")

if (Test-Path $apiConfigPath) {
    $apiJson = Get-Content -Path $apiConfigPath -Raw | ConvertFrom-Json
    $apiJson.Storage.RootPath = $dataDirectory
    $apiJson.ApiKeyOptions.AdminKey = $secureApiKey
    $apiJson | Add-Member -MemberType NoteProperty -Name "Urls" -Value "http://*:$apiPort" -Force
    $apiJson | ConvertTo-Json -Depth 10 | Set-Content -Path $apiConfigPath
}

if (Test-Path $adminConfigPath) {
    $adminJson = Get-Content -Path $adminConfigPath -Raw | ConvertFrom-Json
    $adminJson.APIClientOptions.AdminApiKey = $secureApiKey
    $adminJson.APIClientOptions.BaseUrl = "http://127.0.0.1:$apiPort"
    $adminJson.APIClientOptions.PublicUrl = "http://127.0.0.1:$apiPort"
    $adminJson | Add-Member -MemberType NoteProperty -Name "Urls" -Value "http://*:$adminPort" -Force
    $adminJson | ConvertTo-Json -Depth 10 | Set-Content -Path $adminConfigPath
}

# 8. CREATE AND START WINDOWS SERVICES
Write-Host "Configuring Windows Services..."
$apiExePath   = Join-Path -Path $apiDirectory -ChildPath "InstantAIGate.API.exe"
$adminExePath = Join-Path -Path $adminDirectory -ChildPath "InstantAIGate.Admin.exe"

$apiServiceQuery = Get-Service -Name "InstantAIGate_API" -ErrorAction SilentlyContinue
if (-not $apiServiceQuery) {
    sc.exe create "InstantAIGate_API" binPath= "\`"$apiExePath\`" --run-as-service" start= auto
}

$adminServiceQuery = Get-Service -Name "InstantAIGate_Admin" -ErrorAction SilentlyContinue
if (-not $adminServiceQuery) {
    sc.exe create "InstantAIGate_Admin" binPath= "\`"$adminExePath\`" --run-as-service" start= auto
}

Write-Host "Starting services..."
Start-Service -Name "InstantAIGate_API"
Start-Service -Name "InstantAIGate_Admin"

# 9. OPEN WINDOWS FIREWALL PORTS
Write-Host "Configuring Windows Firewall..."
New-NetFirewallRule -DisplayName "InstantAIGate API (Port $apiPort)" -Direction Inbound -LocalPort $apiPort -Protocol TCP -Action Allow -ErrorAction SilentlyContinue | Out-Null
New-NetFirewallRule -DisplayName "InstantAIGate Admin (Port $adminPort)" -Direction Inbound -LocalPort $adminPort -Protocol TCP -Action Allow -ErrorAction SilentlyContinue | Out-Null

# 10. CLEANUP
Write-Host "Cleaning up temporary files..."
Remove-Item -Path $tempDir -Recurse -Force

Write-Host "=====================================================================" -ForegroundColor Green
Write-Host "Deployment Complete! InstantAIGate is now running securely on Windows." -ForegroundColor Green
Write-Host "API Port: $apiPort" -ForegroundColor Cyan
Write-Host "Admin Port: $adminPort" -ForegroundColor Cyan
Write-Host "Models should be placed in: $dataDirectory" -ForegroundColor Yellow
Write-Host "=====================================================================" -ForegroundColor Green