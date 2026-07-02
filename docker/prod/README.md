# Production Docker Configuration

This directory contains the production-ready Docker configuration for InstantAIGate.

## Files

- `Dockerfile` - Production Docker image definition (uses pre-built binaries from NuGet packages)
- `docker-compose.yml` - Production compose file using pre-built images from GitHub Container Registry
- `.dockerignore` - Docker build context exclusion rules

## Usage

### Using Pre-Built Images (Recommended)

```bash
# Run from the repository root
docker-compose -f docker/prod/docker-compose.yml up -d
```

This uses pre-built images from GitHub Container Registry (GHCR):
- `ghcr.io/instancium/instant-ai-gate-api:latest`
- `ghcr.io/instancium/instant-ai-gate-admin:latest`

### Building Locally

```bash
# Build from the repository root
docker build -f docker/prod/Dockerfile -t instant-ai-gate-api:local .
```

## Configuration

The compose file sets up:
- API service on port 49152 (localhost only)
- Admin service on port 49153 (localhost only)
- Volume mounts for models and configuration
- GPU support (nvidia-docker required)

## Notes

- All paths in compose files are relative to the repository root
- Runtime binaries are managed via NuGet packages during build
- The `.dockerignore` file prevents unnecessary files from being included in the build context
