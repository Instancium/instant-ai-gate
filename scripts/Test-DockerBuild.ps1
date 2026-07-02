# Test Docker Build Script
# Verifies that runtime packages are correctly restored via NuGet during Docker build

Write-Host "Testing Docker build with NuGet runtime package restoration..." -ForegroundColor Cyan

# Get repo root (parent of scripts folder)
$repoRoot = Split-Path -Parent -Path $PSScriptRoot
$dockerfilePath = Join-Path -Path $repoRoot -ChildPath "docker/prod/Dockerfile"

# Build only the dotnet-build stage to check runtime restoration
Write-Host "`nStep 1: Building dotnet-build stage..." -ForegroundColor Yellow
docker build --target dotnet-build -f "$dockerfilePath" -t instant-ai-gate-build-test:latest "$repoRoot"

if ($LASTEXITCODE -ne 0) {
	Write-Host "`n[FAIL] Docker build failed!" -ForegroundColor Red
	exit 1
}

Write-Host "`nStep 2: Inspecting runtime packages in build output..." -ForegroundColor Yellow
$containerId = docker create instant-ai-gate-build-test:latest
$testOutputDir = Join-Path -Path $repoRoot -ChildPath "test-runtimes-output"
docker cp "${containerId}:/app/publish/api/runtimes" "$testOutputDir" 2>$null

if (Test-Path "$testOutputDir") {
	Write-Host "`n[SUCCESS] Runtime packages extracted successfully!" -ForegroundColor Green

	Write-Host "`nRuntime folder structure:" -ForegroundColor Cyan
	Get-ChildItem -Path "$testOutputDir" -Recurse -Directory | ForEach-Object {
		Write-Host "  $($_.FullName.Replace($repoRoot, '.'))" -ForegroundColor Gray
	}

	Write-Host "`nChecking for required libraries..." -ForegroundColor Cyan
	$requiredLibs = @("libllama.so", "libggml.so", "libggml-cuda.so")
	foreach ($lib in $requiredLibs) {
		$found = Get-ChildItem -Path "$testOutputDir" -Recurse -Filter $lib -ErrorAction SilentlyContinue
		if ($found) {
			Write-Host "  [OK] Found: $lib" -ForegroundColor Green
		} else {
			Write-Host "  [MISSING] $lib" -ForegroundColor Red
		}
	}

	Write-Host "`nCleaning up test output..." -ForegroundColor Yellow
	Remove-Item -Path "$testOutputDir" -Recurse -Force
} else {
	Write-Host "`n[FAIL] Runtime packages not found in build output!" -ForegroundColor Red
	Write-Host "NuGet package restoration may have failed." -ForegroundColor Red
}

docker rm $containerId | Out-Null
docker rmi instant-ai-gate-build-test:latest | Out-Null

Write-Host "`nTest completed." -ForegroundColor Cyan
