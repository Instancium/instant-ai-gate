#!/usr/bin/env bash
set -euo pipefail

# Default path is now relative to the script's location in the 'runtimes' directory
# Assuming script is run from its current directory (e.g., ./setup-symlinks.sh)
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
SETUP_DIR="${1:-$SCRIPT_DIR/linux-x64/native}"

if [ ! -d "$SETUP_DIR" ]; then
    echo "Warning: Target runtime directory $SETUP_DIR not found. Skipping symlink generation."
    exit 0
fi

cd "$SETUP_DIR"

for lib in $(ls lib*.so.*.*.* 2>/dev/null); do
    # Extract base name: libllama.so from libllama.so.0.4.0
    BASE_NAME=$(echo "$lib" | sed -E 's/(lib[a-zA-Z0-9_-]+)\.so\..*/\1.so/')
    MAJOR_NAME="${BASE_NAME}.0"
    
    rm -f "$MAJOR_NAME" "$BASE_NAME"
    ln -sf "$lib" "$MAJOR_NAME"
    ln -sf "$MAJOR_NAME" "$BASE_NAME"
    echo "Configured symlinks: $lib -> $MAJOR_NAME -> $BASE_NAME"
done