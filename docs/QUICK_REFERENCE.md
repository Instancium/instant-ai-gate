# Quick Reference

## For Maintainers (Publishing Packages)

```powershell
# Build all runtime packages
.\scripts\Publish-RuntimePackages.ps1 -Source local -Version "1.0.0"

# Publish to NuGet.org
.\scripts\Publish-RuntimePackages.ps1 -Source nuget -ApiKey "YOUR_KEY" -SkipBuild

# Publish to GitHub Packages
.\scripts\Publish-RuntimePackages.ps1 -Source github -ApiKey $env:GITHUB_TOKEN -SkipBuild
```

## For End Users (Consuming Packages)

```bash
# Just restore and build - packages download automatically
git clone https://github.com/Instancium/instant-ai-gate.git
cd instant-ai-gate
dotnet restore  # ← Automatically downloads runtime packages from NuGet.org
dotnet build
dotnet run --project src/InstantAiGate.API
```

**No scripts required for end users!**

## Package Contents

Each runtime package contains llama.cpp binaries for specific platform+backend:

- `InstantAIGate.Runtimes.win-x64.Cpu`: Windows CPU binaries (~150MB)
- `InstantAIGate.Runtimes.win-x64.Cuda`: Windows CUDA binaries (~200MB)
- `InstantAIGate.Runtimes.linux-x64.Cpu`: Linux CPU binaries (~150MB)
- `InstantAIGate.Runtimes.linux-x64.Cuda`: Linux CUDA binaries (~200MB)

**Total: ~700MB across all platforms**

Only the packages for your current OS are downloaded automatically via conditional `PackageReference` in `Infrastructure.csproj`.

## Architecture

```
MAINTAINER                    END USER
	↓                             ↓
.runtimes/                    git clone
	↓                             ↓
Build NuGet packages          dotnet restore
	↓                             ↓
Publish to NuGet.org     ←→   Download packages
	↓                             ↓
✓ Published               ✓ Zero-touch deployment
```

## Version Information

Check published versions at:
- NuGet.org: https://www.nuget.org/packages/InstantAIGate.Runtimes.win-x64.Cpu
- GitHub: https://github.com/Instancium/instant-ai-gate/packages

## Documentation

- **MAINTAINER_GUIDE.md**: Complete publishing guide for maintainers
- **README.md**: General project information
- **ARCHITECTURE.md**: System architecture details
