# Repository Organization Summary

## Structure Overview

This document describes the new repository structure after cleanup.

```
InstantAIGate/
├── scripts/              # Deployment and utility scripts
│   ├── Install-Production.ps1
│   ├── Install-Development.ps1
│   ├── Uninstall.ps1
│   ├── Show-Structure.ps1
│   ├── Test-DockerBuild.ps1
│   ├── Publish-RuntimePackages.ps1
│   └── README.md
├── docker/               # Docker configurations by environment
│   ├── prod/             # Production (pre-built GHCR images)
│   │   ├── Dockerfile
│   │   ├── docker-compose.yml
│   │   ├── .dockerignore
│   │   └── README.md
│   ├── dev/              # Development (build from source)
│   │   ├── Dockerfile
│   │   ├── docker-compose.yml
│   │   ├── .dockerignore
│   │   └── README.md
│   ├── beta/             # Beta testing (pre-built GHCR images)
│   │   ├── docker-compose.yml
│   │   └── README.md
│   └── README.md
├── Dockerfile            # Symlink/reference to production
├── docker-compose.yml    # Production compose file
├── .dockerignore         # Build context exclusion
├── README.md             # Main documentation
├── INSTALLATION.md       # Installation guide
├── SECURITY.md           # Security guide
└── ...
```

## Changes Made

### 1. Scripts Reorganization

**Moved from root to `scripts/`:**
- `install.ps1` → `scripts/Install-Production.ps1`
- `install.dev.ps1` → `scripts/Install-Development.ps1`
- `uninstall.ps1` → `scripts/Uninstall.ps1`
- `structure.ps1` → `scripts/Show-Structure.ps1`
- `test-docker-build.ps1` → `scripts/Test-DockerBuild.ps1`

**Updated paths:**
- All scripts use `$PSScriptRoot` for repo-relative path resolution
- `Test-DockerBuild.ps1` now references `docker/prod/Dockerfile`

**Added:**
- `scripts/README.md` - Comprehensive guide for all scripts

### 2. Docker Files Reorganization

**Moved and restructured:**
- `Dockerfile` → `docker/prod/Dockerfile` (copy retained in root for backward compatibility)
- `Dockerfile.dev` → `docker/dev/Dockerfile` (no `.dev` suffix)
- `docker-compose.yml` → `docker/prod/docker-compose.yml` (copy retained in root)
- `docker-compose.dev.yml` → `docker/dev/docker-compose.yml`
- `docker-compose.beta.yml` → `docker/beta/docker-compose.yml`

**Added:**
- `docker/prod/.dockerignore`
- `docker/dev/.dockerignore`
- `docker/README.md` - Overview of all Docker configurations
- `docker/prod/README.md` - Production Docker guide
- `docker/dev/README.md` - Development Docker guide
- `docker/beta/README.md` - Beta Docker guide

**Updated compose files:**
- Dev and beta compose files now use correct build context and Dockerfile paths:
  - Context: `../..` (repository root)
  - Dockerfile: `docker/dev/Dockerfile` or `docker/beta/docker-compose.yml`

### 3. Root Directory Cleanup

**Root now contains only:**
- Production Docker files (`Dockerfile`, `docker-compose.yml`)
- Application files (`src/`, `tests/`, etc.)
- Configuration files (`.env`, `.gitignore`, etc.)
- Documentation (README.md, LICENSE.txt, etc.)

**Removed from root:**
- ✅ All deployment scripts (moved to `scripts/`)
- ✅ Dev/Beta Docker files (moved to `docker/dev/` and `docker/beta/`)
- ✅ All `.dev` and `.beta` suffixed files (consolidated by folder)

## Usage Examples

### Production Deployment

From repository root:
```bash
# Using PowerShell
.\scripts\Install-Production.ps1

# Or with docker-compose
docker-compose up -d
```

### Development Deployment

From repository root:
```bash
# Build and run locally
.\scripts\Install-Development.ps1

# Or with docker-compose
docker-compose -f docker/dev/docker-compose.yml up -d --build
```

### Validate Docker Build

From repository root:
```bash
.\scripts\Test-DockerBuild.ps1
```

### Uninstall

From repository root:
```bash
.\scripts\Uninstall.ps1
```

## Benefits of New Structure

1. **Clarity**: Each type of file has its designated location
   - Scripts in `scripts/`
   - Docker configurations by environment in `docker/{env}/`
   - No confusing suffixes (`.dev`, `.beta`)

2. **Maintainability**: Easier to find and update files
   - All production files in root (backward compatible)
   - Non-production variants clearly separated
   - Each environment has its own README

3. **Consistency**: Uniform file naming within each environment
   - No more `Dockerfile.dev`, `Dockerfile.beta`
   - All become just `Dockerfile` within their environment folder
   - Same for `docker-compose.yml`

4. **Scalability**: Easy to add new environments
   - Simply create new folder under `docker/{env_name}/`
   - Add README explaining the environment

5. **Documentation**: Each area is self-documented
   - `scripts/README.md` explains all scripts
   - `docker/README.md` explains Docker structure
   - Each `docker/{env}/README.md` has environment-specific details

## Migration Notes

- **Backward Compatibility**: Production files remain in root
- **Path Resolution**: All scripts use `$PSScriptRoot` for automatic path resolution
- **Build Context**: Docker build contexts correctly point to repository root
- **Git Ignore**: `.runtimes/`, `build/`, and other artifacts are already ignored

## Next Steps

1. Update CI/CD workflows to reference new script locations:
   - Old: `./Install-Production.ps1`
   - New: `./scripts/Install-Production.ps1`

2. Update GitHub Actions to reference new Docker paths:
   - Old: `./Dockerfile.dev`
   - New: `./docker/dev/Dockerfile`

3. Update documentation links:
   - Scripts: Point to `scripts/README.md`
   - Docker: Point to `docker/README.md`

4. Archive old root files if any remain for historical reference

## Verification

To verify the new structure:

```bash
# View clean repository tree
.\scripts\Show-Structure.ps1

# Test docker build
.\scripts\Test-DockerBuild.ps1

# Check that old files are gone
Get-ChildItem -Path . -File -Filter "install*.ps1"  # Should be empty
Get-ChildItem -Path . -File -Filter "Dockerfile.*"  # Should be empty (only Dockerfile)
Get-ChildItem -Path . -File -Filter "docker-compose*.yml"  # Should be empty (only docker-compose.yml)
```
