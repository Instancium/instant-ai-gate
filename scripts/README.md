# InstantAIGate Deployment Scripts

This directory contains PowerShell scripts for deploying and managing InstantAIGate.

## Scripts

### Install-Production.ps1

Production deployment script that downloads and installs InstantAIGate as Windows services.

**Requirements:**
- Administrator privileges
- Windows PowerShell (or PowerShell Core)
- .NET Runtime (self-contained binaries included)

**Usage:**
```powershell
# Run as Administrator
.\Install-Production.ps1
```

**What it does:**
1. Downloads pre-built binaries from GitHub releases
2. Validates SHA256 hashes of downloaded files
3. Extracts files to `C:\Program Files\InstantAIGate`
4. Configures Windows services
5. Sets up Windows Firewall rules
6. Injects secure API keys and configuration

**Configuration:**
- API port: 49154
- Admin port: 49155
- Models directory: `C:\ProgramData\InstantAIGate\Models`

---

### Install-Development.ps1

Local development deployment script that builds from source or uses local build artifacts.

**Requirements:**
- Administrator privileges
- Windows PowerShell (or PowerShell Core)
- .NET SDK 10.0 (if building from source)
- `.runtimes\win-x64` folder present (for local runtime drivers)

**Usage:**
```powershell
# Run as Administrator
.\Install-Development.ps1
```

**What it does:**
1. Prompts to build from source or use existing binaries
2. If building: compiles API and Admin projects
3. Packages local runtime drivers
4. Extracts to `C:\Program Files\InstantAIGate`
5. Configures Windows services
6. Sets up Windows Firewall rules
7. Injects configuration with randomly generated API keys

**Configuration:**
- API port: 49154
- Admin port: 49155
- Models directory: `C:\ProgramData\InstantAIGate\Models`

---

### Uninstall.ps1

Complete uninstallation script that removes InstantAIGate from the system.

**Requirements:**
- Administrator privileges
- Windows PowerShell (or PowerShell Core)

**Usage:**
```powershell
# Run as Administrator
.\Uninstall.ps1
```

**What it does:**
1. Stops Windows services
2. Deletes Windows services
3. Removes Firewall rules
4. Deletes application files from `C:\Program Files\InstantAIGate`
5. Prompts to delete downloaded models from `C:\ProgramData\InstantAIGate` (optional)

---

### Show-Structure.ps1

Utility script to display the repository structure while hiding build artifacts.

**Usage:**
```powershell
.\Show-Structure.ps1
```

**What it does:**
1. Hides `bin`, `obj`, and `wwwroot` directories
2. Displays the directory tree using `tree /F`
3. Restores visibility of hidden directories

---

### Test-DockerBuild.ps1

Validation script to verify that Docker runtime restoration works correctly.

**Requirements:**
- Docker Desktop running
- Repository root accessible

**Usage:**
```powershell
# Run from the scripts directory
.\Test-DockerBuild.ps1
```

**What it does:**
1. Builds the `dotnet-build` stage from `docker/prod/Dockerfile`
2. Extracts runtime packages from the build output
3. Verifies presence of required libraries:
   - `libllama.so`
   - `libggml.so`
   - `libggml-cuda.so`
4. Reports results

---

## Common Workflows

### Fresh Production Deployment

```powershell
# Run from this scripts directory
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Install-Production.ps1
```

### Local Development Setup

```powershell
# Run from this scripts directory
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\Install-Development.ps1
# Select 'Y' to build from source
```

### Update Existing Installation

```powershell
# Run uninstall first
.\Uninstall.ps1
# Then run install
.\Install-Production.ps1
```

### Cleanup

```powershell
# Remove all services and files
.\Uninstall.ps1
```

---

## Notes

- All scripts require Administrator privileges
- The `$PSScriptRoot` variable is used to resolve repository paths correctly
- Scripts can be run from any directory as long as the repository structure is intact
- Firewall rules are automatically created for the configured ports
