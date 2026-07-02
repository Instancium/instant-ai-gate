# Docker Deployment Configurations

This directory contains Docker configurations for InstantAIGate organized by deployment environment.

## Directory Structure

```
docker/
├── prod/          # Production configuration
│   ├── Dockerfile
│   ├── docker-compose.yml
│   ├── .dockerignore
│   └── README.md
├── dev/           # Development configuration
│   ├── Dockerfile
│   ├── docker-compose.yml
│   ├── .dockerignore
│   └── README.md
└── beta/          # Beta testing configuration
	├── docker-compose.yml
	└── README.md
```

## Quick Start

### Production (Using Pre-Built Images)

```bash
cd docker/prod
docker-compose up -d
```

### Development (Building from Source)

```bash
cd docker/dev
docker-compose up -d --build
```

### Beta (Using Beta Images)

```bash
cd docker/beta
docker-compose up -d
```

## Environment Overview

### Production (`docker/prod`)
- **Purpose**: End-user deployment using pre-built images from GitHub Container Registry
- **Build**: Uses images from `ghcr.io/instancium/instant-ai-gate-*:latest`
- **Best for**: Production deployments, stable releases
- **Build time**: Instant (no compilation needed)

### Development (`docker/dev`)
- **Purpose**: Local development with source compilation
- **Build**: Compiles from local source code
- **Best for**: Development, testing, custom builds
- **Build time**: ~2-5 minutes depending on machine

### Beta (`docker/beta`)
- **Purpose**: Pre-release testing using beta images from GitHub Container Registry
- **Build**: Uses images from `ghcr.io/instancium/instant-ai-gate-*-beta:latest`
- **Best for**: Testing beta releases before production
- **Build time**: Instant (no compilation needed)

## Common Commands

### All Environments

```bash
# Start services (build if needed)
docker-compose -f docker/{env}/docker-compose.yml up -d

# View logs
docker-compose -f docker/{env}/docker-compose.yml logs -f

# Stop services
docker-compose -f docker/{env}/docker-compose.yml down

# Remove images
docker-compose -f docker/{env}/docker-compose.yml down -v
```

### Development Specific

```bash
# Rebuild without cache
docker-compose -f docker/dev/docker-compose.yml up -d --build --no-cache

# Build only
docker-compose -f docker/dev/docker-compose.yml build --no-cache
```

## Build Context and Paths

**Important**: All compose files use the repository root as the build context. Commands must be run from the repository root or paths must be adjusted.

From repository root:
```bash
docker-compose -f docker/prod/docker-compose.yml up -d
```

From within docker directory:
```bash
# Adjust paths - not recommended
cd docker/prod
docker-compose -f docker-compose.yml up -d  # May fail due to path issues
```

## Volume Mounts

By default, all compose files create volumes in the repository root:
- `./storage/models` - AI model files
- `./data` or `./gateway/config` - Application configuration

To change volume paths, edit the corresponding `docker-compose.yml` file.

## GPU Support

All compose files include GPU support configuration. This requires:
- NVIDIA GPU
- NVIDIA Docker runtime installed
- NVIDIA Container Toolkit configured

If GPU is not available, comment out the `deploy.resources` section in the compose file.

## Network Configuration

- **API Service**: Port 49152 (localhost only by default)
- **Admin Service**: Port 49153 (localhost only by default)

To allow external access, modify the port mapping in the compose file:
```yaml
ports:
  - "0.0.0.0:49152:80"  # API exposed to all interfaces
  - "0.0.0.0:49153:80"  # Admin exposed to all interfaces
```

## Troubleshooting

### Build Fails Due to Missing Runtime Files
The development build may fail if `.runtimes/win-x64` is missing. This is expected on non-Windows systems or if runtime files have not been downloaded.

### Port Already in Use
If ports 49152/49153 are already in use:
1. Change the port mapping in the compose file
2. Stop other services using those ports
3. Ensure previous containers are stopped: `docker-compose down`

### GPU Support Not Working
1. Verify NVIDIA Docker is installed: `docker run --rm --gpus all nvidia/cuda:11.0-base nvidia-smi`
2. Check if GPU is available: `nvidia-smi`
3. Comment out the `deploy.resources` section if GPU is not needed

## Documentation

Each subdirectory contains a `README.md` with environment-specific information:
- `docker/prod/README.md` - Production details
- `docker/dev/README.md` - Development details
- `docker/beta/README.md` - Beta details

## Related Files

- Root `docker-compose.yml` - Symlink or reference to production (if needed)
- Root `Dockerfile` - Symlink or reference to production (if needed)
- `.dockerignore` - Files excluded from Docker build context
