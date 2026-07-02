# InstantAIGate Runtime Architecture Migration

## Overview
InstantAIGate has migrated from local `.runtimes` folder distribution to **NuGet package-based runtime delivery**. This document outlines the changes and their implications.

## Architecture Changes

### Old Architecture (Legacy)
```
Dockerfile:
  - Manual download: curl runtime-linux-x64.zip
  - Extract to .runtimes/
  - COPY .runtimes/ to Docker image

install.ps1:
  - Download separate runtime-win-x64.zip
  - Extract to API/.runtimes/
```

### New Architecture (Current)
```
NuGet Packages (v1.0.9+):
  - InstantAIGate.Runtimes.win-x64.Cpu
  - InstantAIGate.Runtimes.win-x64.Cuda
  - InstantAIGate.Runtimes.linux-x64.Cpu
  - InstantAIGate.Runtimes.linux-x64.Cuda

Package Contents (v1.0.9+):
  ├── .runtimes/{rid}/{backend}/{backend}.7z (compressed binaries)
  ├── build/*.props (MSBuild integration)
  └── README.md

Build Process:
  dotnet restore → Downloads runtime packages from NuGet
  dotnet build  → Imports .props files for content copy
  dotnet publish → Includes runtimes/{rid}/{backend}/*.7z in output

Docker/Deployment:
  - No manual downloads needed
  - No .runtimes folder required in Docker context
  - Runtime binaries automatically included via NuGet restore + MSBuild
  - NativeRuntimeExtractor decompresses .7z on first application startup
```

## Impact on Different Deployment Scenarios

### ✅ Docker Deployment (Recommended)
**Status:** Fully migrated and optimized

- `Dockerfile` updated to remove manual runtime downloads
- `.dockerignore` excludes `.runtimes/` to reduce build context
- `dotnet publish` automatically includes runtime packages
- No user action required

**Verification:**
```bash
# Start Docker Desktop
docker-compose up
```

### ✅ GitHub Actions CI/CD
**Status:** No changes needed

- `deploy.yml` - Builds Docker images, no manual runtime handling
- `publish.yml` - Publishes runtime NuGet packages (maintainer workflow)

### ⚠️ Windows PowerShell Scripts (Legacy)
**Status:** Requires update or deprecation

#### install.ps1 (Production deployment)
- **Current behavior:** Downloads `runtime-win-x64.zip` from GitHub releases
- **Issue:** Published binaries should already contain runtimes via NuGet restore
- **Recommendation:** Update GitHub release process to include runtimes in `api-win-x64.zip`

#### install.dev.ps1 (Maintainer testing)
- **Current behavior:** Packages local `.runtimes\win-x64` folder
- **Purpose:** Local Windows service testing for maintainers
- **Recommendation:** Keep as maintainer-only tool, document as legacy workflow

## Maintainer vs Consumer Modes

### Consumer Mode (End Users)
```csharp
// InstantAIGate.Infrastructure.csproj
<PackageReference Include="InstantAIGate.Runtimes.win-x64.Cpu" Version="1.0.9" />
<PackageReference Include="InstantAIGate.Runtimes.win-x64.Cuda" Version="1.0.9" />
```
- No `.runtimes` folder needed
- NuGet automatically downloads packages with MSBuild props
- Runtime binaries extracted during publish

### Maintainer Mode (Developers)
```bash
# .runtimes/ folder present in repository
├── .runtimes/
│   ├── win-x64/
│   │   ├── cpu/cpu.7z (7.91 MB)
│   │   └── cuda/cuda.7z (87.17 MB)
│   └── linux-x64/
│       ├── cpu/cpu.7z (7.01 MB)
│       └── cuda/cuda.7z (184.56 MB)
```
- Used to build NuGet packages via `Publish-RuntimePackages.ps1`
- Not required for building/running the application
- Ignored by Docker via `.dockerignore`

## Updated Workflows

### For End Users
1. **Docker Compose** (Recommended)
   ```bash
   docker-compose up
   ```
   ✅ Runtime binaries automatically included in GHCR images

2. **Windows Service Deployment**
   - Download `api-win-x64.zip` from GitHub releases
   - Extract and run (runtimes should be included)
   - ⚠️ Current releases may need regeneration with new architecture

### For Maintainers

#### Publishing Runtime NuGet Packages
```powershell
# Ensure .runtimes/ folder exists with latest binaries
.\scripts\Publish-RuntimePackages.ps1 -Source nuget -ApiKey YOUR_KEY
```

#### Local Development
```bash
# Standard .NET workflow - NuGet handles runtimes
dotnet restore
dotnet build
dotnet run --project src/InstantAIGate.API
```

#### Docker Build Testing
```powershell
# Test NuGet runtime restoration in Docker
.\test-docker-build.ps1
```

## Files Modified

### Core Infrastructure
- ✅ `Dockerfile` - Removed manual runtime download/copy logic
- ✅ `.dockerignore` - Added `.runtimes/` exclusion
- ✅ `src/InstantAIGate.Infrastructure/InstantAIGate.Infrastructure.csproj` - Contains PackageReference to runtime packages

### Unchanged (Working as designed)
- ✅ `.github/workflows/deploy.yml` - Docker build works with NuGet restore
- ✅ `.github/workflows/publish.yml` - Runtime package publishing (maintainer-only)
- ✅ `scripts/Publish-RuntimePackages.ps1` - Runtime package creation tool

### Needs Future Updates
- ⚠️ `install.ps1` - Should work with updated GitHub releases
- ⚠️ `install.dev.ps1` - Maintainer-only tool, document as legacy
- ⚠️ GitHub Release process - Ensure `api-win-x64.zip` includes runtimes

## Testing Checklist

### Docker Deployment
- [ ] `docker-compose up` successfully starts services
- [ ] API container has `/app/runtimes/linux-x64/cpu/` folder
- [ ] API container has `/app/runtimes/linux-x64/cuda/` folder
- [ ] `libllama.so` and `libggml*.so` files present
- [ ] Runtime extraction works on first startup (`NativeRuntimeExtractor`)

### Windows Service Deployment
- [ ] Download latest `api-win-x64.zip` from GitHub releases
- [ ] Verify `runtimes/win-x64/cpu/` folder exists in extracted files
- [ ] Verify `runtimes/win-x64/cuda/` folder exists in extracted files
- [ ] Service starts and loads backend successfully

## Troubleshooting

### Issue: Docker build fails with "package not found"
**Solution:** Ensure runtime packages are published to NuGet.org or GitHub Packages:
```bash
# Check InstantAIGate.Infrastructure.csproj for correct version
<RuntimePackageVersion>1.0.9</RuntimePackageVersion>
```

### Issue: Runtime binaries not found in Docker container
**Solution:** Check if `dotnet publish` included runtimes:
```bash
docker exec -it instant-ai-gate-api ls -la /app/runtimes/linux-x64/
```

### Issue: install.ps1 fails to download runtime-win-x64.zip
**Solution:** Runtime should be included in `api-win-x64.zip`. Update release artifacts or regenerate with:
```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

## Migration Benefits

1. **Reduced Complexity:** No manual runtime downloads in CI/CD
2. **Versioning:** Runtime packages versioned independently
3. **Caching:** NuGet package cache improves build speed
4. **Size Limits:** 7z compression fits NuGet.org 250 MB limit
5. **Automatic Extraction:** `NativeRuntimeExtractor` handles decompression at runtime

## References

- [Runtime Package Changelog](CHANGELOG.md) - Version history and improvements
- [Publish-RuntimePackages.ps1](scripts/Publish-RuntimePackages.ps1)
- [NativeRuntimeExtractor.cs](src/InstantAIGate.Infrastructure/Inference/Drivers/NativeRuntimeExtractor.cs)
- [NuGet Package: InstantAIGate.Runtimes.win-x64.Cpu](https://www.nuget.org/packages/InstantAIGate.Runtimes.win-x64.Cpu)
- [NuGet Package: InstantAIGate.Runtimes.linux-x64.Cuda](https://www.nuget.org/packages/InstantAIGate.Runtimes.linux-x64.Cuda)
