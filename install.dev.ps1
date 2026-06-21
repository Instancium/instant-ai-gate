# =====================================================================
# InstantAIGate - Local Build & Test Windows Service Deployment Script
# =====================================================================

# 1. ENFORCE ADMINISTRATOR PRIVILEGES
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Administrator rights are required. Please open PowerShell as Administrator."
    exit
}

Write-Host "Starting InstantAIGate Local Build & TEST Deployment..." -ForegroundColor Cyan

# 2. DEFINE PROJECT SOURCE PATHS & BUILD VARIABLES
$apiSourcePath    = "./src/InstantAIGate.API/InstantAIGate.API.csproj"
$adminSourcePath  = "./src/InstantAIGate.Admin/InstantAIGate.Admin.csproj"

$testBuildsDir    = "C:\Temp\TestBuilds"
$tempPublishDir   = Join-Path -Path $env:TEMP -ChildPath "InstantAIGate_Publish"

# Local ZIP paths
$apiLocalZip      = Join-Path -Path $testBuildsDir -ChildPath "api-win-x64.zip"
$adminLocalZip    = Join-Path -Path $testBuildsDir -ChildPath "admin-win-x64.zip"
$runtimeLocalZip  = Join-Path -Path $testBuildsDir -ChildPath "instant-ai-gate-runtime-v1.0.2-win-x64.zip"

# Port Bindings
$apiPort   = 49154
$adminPort = 49155

# Strict Windows target folder paths
$baseAppDir       = Join-Path -Path $env:ProgramFiles -ChildPath "InstantAIGate"
$apiDirectory     = Join-Path -Path $baseAppDir -ChildPath "API"
$adminDirectory   = Join-Path -Path $baseAppDir -ChildPath "Admin"
$runtimeDirectory = Join-Path -Path $apiDirectory -ChildPath ".runtimes"
$dataDirectory    = Join-Path -Path $env:ProgramData -ChildPath "InstantAIGate\Models"

# 3. INTERACTIVE PROMPT: BUILD OR SKIP?
Write-Host ""
$buildChoice = Read-Host "Do you want to compile and build the projects from source? (Y/N)"

if ($buildChoice -eq 'Y' -or $buildChoice -eq 'y') {
    Write-Host "Preparing build directories..."
    if (-not (Test-Path -Path $testBuildsDir)) { New-Item -ItemType Directory -Path $testBuildsDir -Force | Out-Null }
    if (Test-Path -Path $tempPublishDir) { Remove-Item -Path $tempPublishDir -Recurse -Force }
    New-Item -ItemType Directory -Path $tempPublishDir | Out-Null

    # Changed to --self-contained true so it doesn't rely on the system's .NET runtime
    Write-Host "Building and Compiling API Project..." -ForegroundColor Yellow
    dotnet publish $apiSourcePath -c Release -r win-x64 --self-contained true -o "$tempPublishDir\API"
    Write-Host "Zipping API Project..."
    Compress-Archive -Path "$tempPublishDir\API\*" -DestinationPath $apiLocalZip -Force

    Write-Host "Building and Compiling Admin Project..." -ForegroundColor Yellow
    dotnet publish $adminSourcePath -c Release -r win-x64 --self-contained true -o "$tempPublishDir\Admin"
    Write-Host "Zipping Admin Project..."
    Compress-Archive -Path "$tempPublishDir\Admin\*" -DestinationPath $adminLocalZip -Force

    Write-Host "Cleaning up temporary build files..."
    Remove-Item -Path $tempPublishDir -Recurse -Force
} else {
    Write-Host "Skipping build step. Using existing ZIP files in $testBuildsDir..." -ForegroundColor Yellow
}

# 4. VERIFY LOCAL ZIP FILES EXIST BEFORE RUNNING
$zipFiles = @($apiLocalZip, $adminLocalZip, $runtimeLocalZip)
foreach ($zip in $zipFiles) {
    if (-not (Test-Path -Path $zip)) {
        Write-Error "Test ZIP file not found: $zip. Please ensure it exists before deploying."
        exit
    }
}

# 5. STOP EXISTING SERVICES (If updating)
Write-Host "Stopping existing services (if any)..."
Stop-Service -Name "InstantAIGate_API" -ErrorAction SilentlyContinue
Stop-Service -Name "InstantAIGate_Admin" -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# 6. CREATE TARGET DIRECTORIES
Write-Host "Creating secure application directories..."
$directories = @($apiDirectory, $adminDirectory, $runtimeDirectory, $dataDirectory)
foreach ($dir in $directories) {
    if (-not (Test-Path -Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

# 7. EXTRACT LOCAL ARCHIVES AND FLATTEN STRUCTURE
Write-Host "Extracting API files from local ZIP..."
Expand-Archive -Path $apiLocalZip -DestinationPath $apiDirectory -Force

if (Test-Path -Path (Join-Path $apiDirectory "publish")) {
    Write-Host "Flattening API directory structure (merging folders)..."
    Copy-Item -Path "$apiDirectory\publish\*" -Destination $apiDirectory -Recurse -Force
    Remove-Item -Path "$apiDirectory\publish" -Recurse -Force
}

Write-Host "Extracting Admin files from local ZIP..."
Expand-Archive -Path $adminLocalZip -DestinationPath $adminDirectory -Force

if (Test-Path -Path (Join-Path $adminDirectory "publish")) {
    Write-Host "Flattening Admin directory structure (merging folders)..."
    Copy-Item -Path "$adminDirectory\publish\*" -Destination $adminDirectory -Recurse -Force
    Remove-Item -Path "$adminDirectory\publish" -Recurse -Force
}

Write-Host "Extracting multi-backend Runtime Drivers from local ZIP..."
Expand-Archive -Path $runtimeLocalZip -DestinationPath $runtimeDirectory -Force

if (Test-Path (Join-Path $runtimeDirectory "win-x64\cuda")) {
    Write-Host "[OK] Runtime structure successfully deployed." -ForegroundColor Green
} else {
    Write-Host "[WARNING] Could not verify win-x64\cuda structure. Please check the ZIP contents." -ForegroundColor Yellow
}

# 8. CONFIGURE APPSETTINGS (Auto-generate Secure Keys, Paths, Ports & CORS)
Write-Host "Injecting secure configurations and port bindings..."
$apiConfigPath   = Join-Path -Path $apiDirectory -ChildPath "appsettings.json"
$adminConfigPath = Join-Path -Path $adminDirectory -ChildPath "appsettings.json"

$secureApiKey = [guid]::NewGuid().ToString("N") + [guid]::NewGuid().ToString("N")

if (Test-Path $apiConfigPath) {
    $apiJson = Get-Content -Path $apiConfigPath -Raw | ConvertFrom-Json
    
    $apiJson.Storage.RootPath = $dataDirectory
    $apiJson.ApiKeyOptions.AdminKey = $secureApiKey
    
    # Update CORS settings dynamically based on the Admin port BEFORE saving
    if ($null -ne $apiJson.CorsSettings) {
        $apiJson.CorsSettings.AllowedOrigins = @("http://localhost:$adminPort", "http://127.0.0.1:$adminPort")
    }
    
    $apiJson | Add-Member -MemberType NoteProperty -Name "Urls" -Value "http://*:$apiPort" -Force
    
    # Save the modified JSON back to the file
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

# 9. CREATE AND START WINDOWS SERVICES
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

# 10. OPEN WINDOWS FIREWALL PORTS
Write-Host "Configuring Windows Firewall..."
New-NetFirewallRule -DisplayName "InstantAIGate API (Port $apiPort)" -Direction Inbound -LocalPort $apiPort -Protocol TCP -Action Allow -ErrorAction SilentlyContinue | Out-Null
New-NetFirewallRule -DisplayName "InstantAIGate Admin (Port $adminPort)" -Direction Inbound -LocalPort $adminPort -Protocol TCP -Action Allow -ErrorAction SilentlyContinue | Out-Null

Write-Host "=====================================================================" -ForegroundColor Green
Write-Host "TEST Deployment Complete! App extracted and services are active." -ForegroundColor Green
Write-Host "API URL:   http://localhost:$apiPort" -ForegroundColor Cyan
Write-Host "Admin URL: http://localhost:$adminPort" -ForegroundColor Cyan
Write-Host "Models should be placed in: $dataDirectory" -ForegroundColor Yellow
Write-Host "=====================================================================" -ForegroundColor Green