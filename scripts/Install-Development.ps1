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
# Dynamically resolve paths relative to the repository root
$repoRoot         = Split-Path -Parent -Path $PSScriptRoot
$apiSourcePath    = Join-Path -Path $repoRoot -ChildPath "src\InstantAIGate.API\InstantAIGate.API.csproj"
$adminSourcePath  = Join-Path -Path $repoRoot -ChildPath "src\InstantAIGate.Admin\InstantAIGate.Admin.csproj"
$localRuntimeSrc  = Join-Path -Path $repoRoot -ChildPath ".runtimes\win-x64"

# Use a local 'build' folder in the repository root (Ensure 'build/' is in your .gitignore)
$localBuildDir    = Join-Path -Path $repoRoot -ChildPath "build"
$tempPublishDir   = Join-Path -Path $env:TEMP -ChildPath "InstantAIGate_Publish"

# Local ZIP paths inside the 'build' directory
$apiLocalZip      = Join-Path -Path $localBuildDir -ChildPath "api-win-x64.zip"
$adminLocalZip    = Join-Path -Path $localBuildDir -ChildPath "admin-win-x64.zip"
$runtimeLocalZip  = Join-Path -Path $localBuildDir -ChildPath "runtime-win-x64.zip"

# Port Bindings
$apiPort   = 49154
$adminPort = 49155

# Strict Windows target folder paths
$baseAppDir       = Join-Path -Path $env:ProgramFiles -ChildPath "InstantAIGate"
$apiDirectory     = Join-Path -Path $baseAppDir -ChildPath "API"
$adminDirectory   = Join-Path -Path $baseAppDir -ChildPath "Admin"
$runtimeDirectory = Join-Path -Path $apiDirectory -ChildPath ".runtimes"
$dataDirectory    = Join-Path -Path $env:ProgramData -ChildPath "InstantAIGate\Models"

# 3. PREPARE BUILD DIRECTORY & PACKAGE RUNTIMES
if (-not (Test-Path -Path $localBuildDir)) { 
	Write-Host "Creating local build directory at $localBuildDir..."
	New-Item -ItemType Directory -Path $localBuildDir -Force | Out-Null 
}

Write-Host "Packaging local runtimes from .runtimes\win-x64..." -ForegroundColor Yellow
if (-not (Test-Path -Path $localRuntimeSrc)) {
	Write-Error "Local runtime folder not found at: $localRuntimeSrc. Cannot proceed without drivers."
	exit
}
# Compress the win-x64 folder directly into the build directory
Compress-Archive -Path $localRuntimeSrc -DestinationPath $runtimeLocalZip -Force

# 4. INTERACTIVE PROMPT: BUILD OR SKIP .NET PROJECTS?
Write-Host ""
$buildChoice = Read-Host "Do you want to compile and build the C# projects from source? (Y/N)"

if ($buildChoice -eq 'Y' -or $buildChoice -eq 'y') {

	# SAFETY CHECK: Ensure the project files actually exist
	if (-not (Test-Path $apiSourcePath)) { Write-Error "API project file not found at: $apiSourcePath"; exit }
	if (-not (Test-Path $adminSourcePath)) { Write-Error "Admin project file not found at: $adminSourcePath"; exit }

	if (Test-Path -Path $tempPublishDir) { Remove-Item -Path $tempPublishDir -Recurse -Force }
	New-Item -ItemType Directory -Path $tempPublishDir | Out-Null

	Write-Host "Building and Compiling API Project..." -ForegroundColor Yellow
	dotnet publish $apiSourcePath -c Release -r win-x64 --self-contained true -o "$tempPublishDir\API"

	if ($LASTEXITCODE -ne 0) { Write-Error "API Build failed! Aborting deployment."; exit }

	Write-Host "Zipping API Project..."
	Compress-Archive -Path "$tempPublishDir\API\*" -DestinationPath $apiLocalZip -Force

	Write-Host "Building and Compiling Admin Project..." -ForegroundColor Yellow
	dotnet publish $adminSourcePath -c Release -r win-x64 --self-contained true -o "$tempPublishDir\Admin"

	if ($LASTEXITCODE -ne 0) { Write-Error "Admin Build failed! Aborting deployment."; exit }

	Write-Host "Zipping Admin Project..."
	Compress-Archive -Path "$tempPublishDir\Admin\*" -DestinationPath $adminLocalZip -Force

	Write-Host "Cleaning up temporary build files..."
	Remove-Item -Path $tempPublishDir -Recurse -Force
} else {
	Write-Host "Skipping .NET build step. Using existing ZIP files in $localBuildDir..." -ForegroundColor Yellow
}

# 5. VERIFY ALL LOCAL ZIP FILES EXIST BEFORE RUNNING (NO NETWORK REQUIRED)
$zipFiles = @($apiLocalZip, $adminLocalZip, $runtimeLocalZip)
foreach ($zip in $zipFiles) {
	if (-not (Test-Path -Path $zip)) {
		Write-Error "Archive not found: $zip. Please ensure it is built before deploying."
		exit
	}
}

# 6. STOP EXISTING SERVICES (If updating)
Write-Host "Stopping existing services (if any)..."
Stop-Service -Name "InstantAIGate_API" -ErrorAction SilentlyContinue
Stop-Service -Name "InstantAIGate_Admin" -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# 7. CREATE TARGET DIRECTORIES
Write-Host "Creating secure application directories..."
$directories = @($apiDirectory, $adminDirectory, $runtimeDirectory, $dataDirectory)
foreach ($dir in $directories) {
	if (-not (Test-Path -Path $dir)) {
		New-Item -ItemType Directory -Path $dir -Force | Out-Null
	}
}

# 8. EXTRACT LOCAL ARCHIVES AND FLATTEN STRUCTURE
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

Write-Host "Extracting local Runtime Drivers from ZIP..."
Expand-Archive -Path $runtimeLocalZip -DestinationPath $runtimeDirectory -Force

if (Test-Path (Join-Path $runtimeDirectory "win-x64\cuda")) {
	Write-Host "[OK] Runtime structure successfully deployed." -ForegroundColor Green
} else {
	Write-Host "[WARNING] Could not verify win-x64\cuda structure. Please check the local .runtimes folder." -ForegroundColor Yellow
}

# 9. CONFIGURE APPSETTINGS (Auto-generate Secure Keys, Paths, Ports & CORS)
Write-Host "Injecting secure configurations and port bindings..."
$apiConfigPath   = Join-Path -Path $apiDirectory -ChildPath "appsettings.json"
$adminConfigPath = Join-Path -Path $adminDirectory -ChildPath "appsettings.json"

$secureApiKey = [guid]::NewGuid().ToString("N") + [guid]::NewGuid().ToString("N")

if (Test-Path $apiConfigPath) {
	$apiJson = Get-Content -Path $apiConfigPath -Raw | ConvertFrom-Json

	$apiJson.Storage.RootPath = $dataDirectory
	$apiJson.ApiKeyOptions.AdminKey = $secureApiKey

	if ($null -ne $apiJson.CorsSettings) {
		$apiJson.CorsSettings.AllowedOrigins = @("http://localhost:$adminPort", "http://127.0.0.1:$adminPort")
	}

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

# 10. CREATE AND START WINDOWS SERVICES
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

# 11. OPEN WINDOWS FIREWALL PORTS
Write-Host "Configuring Windows Firewall..."
New-NetFirewallRule -DisplayName "InstantAIGate API (Port $apiPort)" -Direction Inbound -LocalPort $apiPort -Protocol TCP -Action Allow -ErrorAction SilentlyContinue | Out-Null
New-NetFirewallRule -DisplayName "InstantAIGate Admin (Port $adminPort)" -Direction Inbound -LocalPort $adminPort -Protocol TCP -Action Allow -ErrorAction SilentlyContinue | Out-Null

Write-Host "=====================================================================" -ForegroundColor Green
Write-Host "TEST Deployment Complete! App extracted locally and services are active." -ForegroundColor Green
Write-Host "API URL:   http://localhost:$apiPort" -ForegroundColor Cyan
Write-Host "Admin URL: http://localhost:$adminPort" -ForegroundColor Cyan
Write-Host "Models should be placed in: $dataDirectory" -ForegroundColor Yellow
Write-Host "=====================================================================" -ForegroundColor Green
