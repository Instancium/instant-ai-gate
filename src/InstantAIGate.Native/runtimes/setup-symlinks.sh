#!/usr/bin/env bash
set -euo pipefail

# The script is now located in the 'runtimes' output folder, 
# so the flattened .so files are one directory up (in the root)
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"
SETUP_DIR="${1:-$SCRIPT_DIR/..}"

if [ ! -d "$SETUP_DIR" ]; then
    echo "Warning: Target runtime directory $SETUP_DIR not found. Skipping symlink generation."
    exit 0
fi

cd "$SETUP_DIR"

for lib in $(ls lib*.so.*.*.* 2>/dev/null); do
    BASE_NAME=$(echo "$lib" | sed -E 's/(lib[a-zA-Z0-9_-]+)\.so\..*/\1.so/')
    MAJOR_NAME="${BASE_NAME}.0"
    
    rm -f "$MAJOR_NAME" "$BASE_NAME"
    ln -sf "$lib" "$MAJOR_NAME"
    ln -sf "$MAJOR_NAME" "$BASE_NAME"
    echo "Configured symlinks: $lib -> $MAJOR_NAME -> $BASE_NAME"
done