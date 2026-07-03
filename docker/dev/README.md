# Development Docker Configuration

This directory contains the development Docker configuration for InstantAIGate.

## Files

- `Dockerfile` - Development Docker image definition (builds locally from source)
- `docker-compose.yml` - Development compose file that builds images locally
- `.dockerignore` - Docker build context exclusion rules

## Usage

### Local Build and Run

```bash
# Build and run from the repository root
docker-compose -f docker/dev/docker-compose.yml up -d --build
```

This will:
1. Build Docker images locally from the source code
2. Restore NuGet packages
3. Publish the API and Admin applications
4. Start the services

### Rebuild

```bash
# Force rebuild without cache
docker-compose -f docker/dev/docker-compose.yml up -d --build --no-cache
```

### View Logs

```bash
docker-compose -f docker/dev/docker-compose.yml logs -f
```

### Stop Services

```bash
docker-compose -f docker/dev/docker-compose.yml down
```

## Configuration

The compose file sets up:
- API service on port 49152 (localhost only)
- Admin service on port 49153 (localhost only)
- Volume mounts for models and configuration
- GPU support (nvidia-docker required)

## Build Context

- The build context is the repository root (`../../`)
- The Dockerfile path is `docker/dev/Dockerfile`
- All relative paths in the Dockerfile are based on the repository root

## Notes

- Development builds include local runtime packages for faster iteration
- The `.runtimes/` folder is copied into the build if it exists
- Runtime binaries are still managed via NuGet packages for consistency with production
