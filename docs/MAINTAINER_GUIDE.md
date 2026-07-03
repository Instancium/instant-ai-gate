# Maintainer Guide

This guide is for project maintainers who need to build and publish runtime NuGet packages.

## Prerequisites

- **.runtimes folder** with llama.cpp binaries (win-x64, linux-x64, CPU & CUDA)
- **NuGet.org API key** (for public publishing)
- **GitHub token** with `write:packages` permission (for GitHub Packages)

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│ MAINTAINERS (Build & Publish)                          │
│                                                         │
│ .runtimes/            ← Binary files (not in git)      │
│ ├── win-x64/                                           │
│ │   ├── cpu/                                           │
│ │   └── cuda/                                          │
│ └── linux-x64/                                         │
│     ├── cpu/                                           │
│     └── cuda/                                          │
│                                                         │
│ scripts/Publish-RuntimePackages.ps1                    │
│ └── Builds 4 NuGet packages                            │
└────────────────┬────────────────────────────────────────┘
				 │
				 ▼
┌─────────────────────────────────────────────────────────┐
│ NuGet.org / GitHub Packages                             │
│ ├─ InstantAIGate.Runtimes.win-x64.Cpu                 │
│ ├─ InstantAIGate.Runtimes.linux-x64.Cpu               │
│ ├─ InstantAIGate.Runtimes.win-x64.Cuda                │
│ └─ InstantAIGate.Runtimes.linux-x64.Cuda              │
└────────────────┬────────────────────────────────────────┘
				 │
				 ▼
┌─────────────────────────────────────────────────────────┐
│ END USERS (Automatic Download)                          │
│ $ git clone ...                                         │
│ $ dotnet restore  ← Downloads runtime packages          │
│ $ dotnet build                                          │
│ ✓ Zero-touch deployment                                │
└─────────────────────────────────────────────────────────┘
```

## Publishing Workflow

### Option 1: Manual Publishing (Recommended for testing)

```powershell
# 1. Build packages locally (saved to nuget-packages/)
.\scripts\Publish-RuntimePackages.ps1 -Source local -Version "1.0.0"

# 2. Verify packages
ls nuget-packages/

# 3. Publish to NuGet.org
.\scripts\Publish-RuntimePackages.ps1 `
  -Source nuget `
  -ApiKey "your-nuget-api-key" `
  -SkipBuild

# 4. Or publish to GitHub Packages
.\scripts\Publish-RuntimePackages.ps1 `
  -Source github `
  -ApiKey $env:GITHUB_TOKEN `
  -SkipBuild
```

### Option 2: Automated CI/CD (GitHub Actions)

**Trigger manually:**
1. Go to: https://github.com/Instancium/instant-ai-gate/actions
2. Select "Publish Runtime Packages" workflow
3. Click "Run workflow"
4. Enter version (e.g., "1.0.0")
5. Select target: nuget, github, or both

**Automatic trigger:**
- Pushes to `main` branch that modify `.runtimes/**` automatically publish to NuGet.org

### Option 3: Quick one-liner

```powershell
# Build and publish in one command
dotnet pack src/Runtimes/*/*.csproj -c Release -o nupkg
dotnet nuget push "nupkg/*.nupkg" --source https://api.nuget.org/v3/index.json --api-key YOUR_KEY
```

## Version Management

Runtime package versions should follow semantic versioning:
- **Major.Minor.Patch** (e.g., 1.0.0, 1.1.0)
- Increment **patch** for bug fixes (1.0.1)
- Increment **minor** for new llama.cpp versions (1.1.0)
- Increment **major** for breaking changes (2.0.0)

**Update version in:**
1. All 4 `.csproj` files in `src/Runtimes/` (change `<Version>` property)
2. `Infrastructure.csproj` PackageReference versions (when publishing new version)

## Secrets Setup

### NuGet.org API Key
1. Login to https://www.nuget.org/
2. Go to API Keys: https://www.nuget.org/account/apikeys
3. Create new key with "Push new packages and package versions" permission
4. Add to GitHub Secrets as `NUGET_API_KEY`

### GitHub Packages Token
- Automatically available as `${{ secrets.GITHUB_TOKEN }}` in workflows
- For local development: https://github.com/settings/tokens
  - Scope: `write:packages`, `read:packages`

## Troubleshooting

### "Package already exists" error
- Use `--skip-duplicate` flag (already in scripts)
- Or increment version number

### ".runtimes folder not found"
- Ensure `.runtimes/` exists in project root
- Contains subfolders: `win-x64/cpu`, `win-x64/cuda`, `linux-x64/cpu`, `linux-x64/cuda`

### Large package size warnings
- Expected: Each package is 100-200MB
- NU5100 warning is suppressed in .csproj

## Adding New Platforms

To add ARM64 or macOS support:
1. Add binaries to `.runtimes/osx-arm64/cpu/` etc.
2. Create new project: `InstantAIGate.Runtimes.osx-arm64.Cpu.csproj`
3. Update `Publish-RuntimePackages.ps1` with new project path
4. Update `Infrastructure.csproj` with new conditional PackageReference
5. Build and publish new packages

## For End Users

End users **do NOT need** the `.runtimes/` folder or this script.

They simply:
```bash
git clone https://github.com/Instancium/instant-ai-gate.git
cd instant-ai-gate
dotnet restore  # ← Automatically downloads published runtime packages
dotnet build
dotnet run --project src/InstantAiGate.API
```

**Zero scripts, zero manual steps!** ✅
