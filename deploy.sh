#!/bin/bash
# =============================================================================
# ConfigKeeper - Build & Deploy Script
# =============================================================================
# Compiles the mod, packages it as a .zip, and drops it into the VS Mods folder.
# Usage: ./deploy.sh
# =============================================================================

set -e

MOD_ID="configkeeper"
MOD_NAME="ConfigKeeper"

# --- Paths (adjust if your layout differs) ---
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR"
VS_DATA="$HOME/.config/VintagestoryData"
MODS_DIR="$VS_DATA/Mods"
BUILD_DIR="$PROJECT_DIR/bin/Release"
RELEASE_DIR="$PROJECT_DIR/Releases"

# Read version from modinfo.json
VERSION=$(grep -oP '"version"\s*:\s*"\K[^"]+' "$PROJECT_DIR/modinfo.json")

if [ -z "$VERSION" ]; then
    echo "ERROR: Could not read version from modinfo.json"
    exit 1
fi

ZIP_NAME="${MOD_NAME}_${VERSION}.zip"

echo "============================================="
echo "  Building $MOD_NAME v$VERSION"
echo "============================================="

# --- Clean previous build ---
echo "[1/5] Cleaning previous build..."
dotnet clean "$PROJECT_DIR/$MOD_NAME.csproj" -c Release --nologo -v quiet 2>/dev/null || true
rm -rf "$BUILD_DIR"
rm -rf "$RELEASE_DIR"

# --- Build ---
echo "[2/5] Compiling..."
dotnet build "$PROJECT_DIR/$MOD_NAME.csproj" -c Release --nologo

if [ $? -ne 0 ]; then
    echo "ERROR: Build failed!"
    exit 1
fi

echo "[3/5] Packaging $ZIP_NAME..."
mkdir -p "$RELEASE_DIR"

# Create a temp staging area
STAGE_DIR=$(mktemp -d)
trap "rm -rf $STAGE_DIR" EXIT

# Copy DLL
cp "$BUILD_DIR/$MOD_NAME.dll" "$STAGE_DIR/"

# Copy modinfo.json
cp "$PROJECT_DIR/modinfo.json" "$STAGE_DIR/"

# Copy modicon if present
if [ -f "$PROJECT_DIR/modicon.png" ]; then
    cp "$PROJECT_DIR/modicon.png" "$STAGE_DIR/"
fi

# Copy assets
if [ -d "$PROJECT_DIR/assets" ]; then
    cp -r "$PROJECT_DIR/assets" "$STAGE_DIR/"
fi

# Build the zip
cd "$STAGE_DIR"
zip -r "$RELEASE_DIR/$ZIP_NAME" . -x "*.pdb" > /dev/null
cd "$PROJECT_DIR"

echo "  -> $RELEASE_DIR/$ZIP_NAME"

# --- Deploy to Mods folder ---
echo "[4/5] Deploying to $MODS_DIR..."
mkdir -p "$MODS_DIR"

# Remove any old versions of this mod
rm -f "$MODS_DIR/${MOD_NAME}"_*.zip 2>/dev/null
rm -f "$MODS_DIR/${MOD_NAME}".zip 2>/dev/null

# Copy the new version
cp "$RELEASE_DIR/$ZIP_NAME" "$MODS_DIR/"

echo "  -> $MODS_DIR/$ZIP_NAME"

# --- Summary ---
echo "[5/5] Done!"
echo "============================================="
echo "  $MOD_NAME v$VERSION deployed successfully"
echo "  Restart Vintage Story to load the mod."
echo "============================================="

# Show zip contents for verification
echo ""
echo "Package contents:"
unzip -l "$RELEASE_DIR/$ZIP_NAME" | tail -n +4 | head -n -2
