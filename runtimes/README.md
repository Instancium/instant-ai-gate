# InstantAIGate Native Runtimes

This directory contains the build environment and precompiled native binaries for the `llama.cpp` inference engine.

## Version
- **Source**: [ggerganov/llama.cpp](https://github.com/ggerganov/llama.cpp)
- **Release Version**: `v0.4.0`

## Directory Structure
```text
runtimes/
├── build/          # Build scripts, Dockerfile, and Docker Compose configuration
── win-x64/        # Precompiled Windows binaries (.dll, .exe)
└── linux-x64/      # Precompiled Linux binaries (.so) and symlinks
```

## Build Instructions

### Windows
1. Ensure CMake and Visual Studio Build Tools are installed.
2. Execute the build script:
   ```powershell
   .\build\start.ps1
   ```
   *The script automatically clones the `llama.cpp` repository (if missing), checks out `v0.4.0`, configures CMake with Vulkan support, and deploys artifacts to `win-x64`.*

### Linux
1. Ensure Docker and Docker Compose are installed.
2. Execute the build script:
   ```bash
   chmod +x build/start.sh
   ./build/start.sh
   ```
   *The script builds the Docker image, compiles the libraries, and extracts them to `linux-x64`. Artifacts are packed into a `.tar` file inside the container to perfectly preserve symlinks across the Docker/host boundary.*

## Symlink Strategy (Linux)
To prevent duplicate physical files and MSBuild symlink resolution issues:
1. **Docker Extraction**: The `start.sh` script uses `tar` to extract artifacts, ensuring symlinks in `linux-x64` are created correctly instead of being copied as duplicate files.
2. **MSBuild Configuration**: `InstantAIGate.Native.csproj` is configured to copy only the base `.so` files.
3. **Post-Build Recreation**: An MSBuild post-build target dynamically generates the required `.so.0` symlinks in the final application output directory based on the deployed base files.

## Deployment
Native artifacts are automatically copied to the application root during the .NET build process. The `.csproj` file filters the copied files based on the target operating system (`win-x64` or `linux-x64`).