#!/bin/bash
# Summary: Builds Linux native libraries via Docker and extracts them preserving symlinks.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="$(dirname "$SCRIPT_DIR")/linux-x64"

echo "=== Building Linux native libraries ==="
docker-compose -f "$SCRIPT_DIR/docker-compose.yml" up --build

echo "=== Extracting artifacts with preserved symlinks ==="
mkdir -p "$OUTPUT_DIR"

# Copy tarball from container
docker cp llama-cpp-builder:/artifacts/linux-x64-artifacts.tar "$OUTPUT_DIR/"

# Extract tarball (preserves symlinks) and clean up
tar -xvf "$OUTPUT_DIR/linux-x64-artifacts.tar" -C "$OUTPUT_DIR"
rm "$OUTPUT_DIR/linux-x64-artifacts.tar"

docker-compose -f "$SCRIPT_DIR/docker-compose.yml" down

echo "=== Build completed successfully ==="