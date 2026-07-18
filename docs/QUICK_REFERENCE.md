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
