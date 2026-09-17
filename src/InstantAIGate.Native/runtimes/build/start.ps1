# Summary: Clones, builds, and deploys llama.cpp native libraries for Windows.
param(
    [string]$RepoUrl = "https://github.com/ggerganov/llama.cpp",
    [string]$Tag = "v0.4.0",
    [string]$BuildDir = "build",
    [string]$OutputDir = "..\win-x64"
)

$ErrorActionPreference = "Stop"
$RepoPath = Join-Path $PSScriptRoot "llama.cpp"
$CMakeBuildDir = Join-Path $RepoPath $BuildDir

# 1. Clone repository if missing
if (-Not (Test-Path $RepoPath)) {
    Write-Host "Cloning llama.cpp repository..." -ForegroundColor Cyan
    git clone $RepoUrl $RepoPath
}

# 2. Checkout specific version
Set-Location $RepoPath
Write-Host "Checking out $Tag..." -ForegroundColor Cyan
git fetch --tags
git checkout $Tag

# 3. Clean previous build
if (Test-Path $CMakeBuildDir) {
    Write-Host "Cleaning previous build..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $CMakeBuildDir
}

# 4. Configure and Build
Write-Host "Configuring CMake with Vulkan support..." -ForegroundColor Cyan
cmake -B $CMakeBuildDir -DGGML_VULKAN=1 `
    -DLLAMA_BUILD_EXAMPLES=OFF `
    -DLLAMA_BUILD_TESTS=OFF `
    -DLLAMA_BUILD_SERVER=ON `
    -DLLAMA_BUILD_TOOLS=ON `
    -DLLAMA_BUILD_CLI=ON `
    -DLLAMA_NATIVE=ON `
    -DCMAKE_BUILD_TYPE=Release

Write-Host "Building native libraries..." -ForegroundColor Cyan
cmake --build $CMakeBuildDir --config Release -j 12

# 5. Deploy to win-x64
Write-Host "Deploying artifacts to $OutputDir..." -ForegroundColor Green
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Copy-Item -Path "$CMakeBuildDir\bin\Release\*.dll" -Destination $OutputDir -Force
Copy-Item -Path "$CMakeBuildDir\bin\Release\*.exe" -Destination $OutputDir -Force

Write-Host "Build completed successfully!" -ForegroundColor Green