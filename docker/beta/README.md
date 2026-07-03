# Beta Docker Configuration

This directory contains the beta Docker configuration for InstantAIGate.

## Files

- `docker-compose.yml` - Beta compose file using pre-built images from GitHub Container Registry (beta tag)

## Usage

```bash
# Run from the repository root
docker-compose -f docker/beta/docker-compose.yml up -d
```

This uses beta images from GitHub Container Registry (GHCR):
- `ghcr.io/instancium/instant-ai-gate-api-beta:latest`
- `ghcr.io/instancium/instant-ai-gate-admin-beta:latest`

## Configuration

The compose file sets up:
- API service on port 49152 (localhost only)
- Admin service on port 49153 (localhost only)
- Volume mounts for models and configuration
- GPU support (nvidia-docker required)

## Purpose

This configuration is used for testing beta releases before they are promoted to production.

## Notes

- Beta builds are typically built from the `develop` branch or release candidates
- Images are hosted on GitHub Container Registry with the `-beta` suffix
- All paths in the compose file are relative to the repository root
