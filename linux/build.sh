#!/usr/bin/env bash
# Builds FFIXSaveEditor-x86_64.AppImage: a portable, self-contained Linux
# build of the simple GUI editor. PySide6 ships as self-contained wheels (no
# system Qt/GTK needed to build it), so this only needs a working Python 3
# with pip; if that's missing this script bootstraps its own local Python
# (via micromamba, no root/sudo needed) so the build still works either way.
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$(cd .. && pwd)"
HERE="$(pwd)"

PYTHON=python3
if ! command -v python3 >/dev/null 2>&1 || ! python3 -m pip --version >/dev/null 2>&1; then
    echo "=== No usable system Python (with pip) found; bootstrapping a local one (no root needed) ==="
    if [ ! -x "$HERE/.buildenv/bin/python3" ]; then
        if [ ! -x "$HERE/micromamba" ]; then
            curl -Ls "https://micro.mamba.pm/api/micromamba/linux-64/latest" | tar -xj -C "$HERE" bin/micromamba
            mv "$HERE/bin/micromamba" "$HERE/micromamba"
            rmdir "$HERE/bin" 2>/dev/null || true
        fi
        MAMBA_ROOT_PREFIX="$HERE/.mamba_root" "$HERE/micromamba" create -y -p "$HERE/.buildenv" -c conda-forge python=3.11
    fi
    PYTHON="$HERE/.buildenv/bin/python3"
fi

echo "=== Using $("$PYTHON" --version) at $PYTHON ==="

echo "=== Installing build dependencies ==="
"$PYTHON" -m pip install --quiet --upgrade pip
"$PYTHON" -m pip install --quiet pyinstaller pycryptodome pyside6-essentials

echo "=== Building standalone binary with PyInstaller ==="
"$PYTHON" -m PyInstaller --noconfirm --clean FFIXSaveEditor.spec

echo "=== Assembling AppDir ==="
rm -rf AppDir
mkdir -p AppDir/usr/bin
cp dist/FFIXSaveEditor AppDir/usr/bin/FFIXSaveEditor
cp "$ROOT/assets/icon.png" AppDir/ffixsaveeditor.png

cat > AppDir/ffixsaveeditor.desktop <<'EOF'
[Desktop Entry]
Type=Application
Name=FFIX Save Editor
Comment=Save editor for Final Fantasy IX
Exec=FFIXSaveEditor %f
Icon=ffixsaveeditor
Categories=Utility;Game;
Terminal=false
EOF

cat > AppDir/AppRun <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/FFIXSaveEditor" "$@"
EOF
chmod +x AppDir/AppRun

echo "=== Fetching appimagetool (cached after first run) ==="
if [ ! -x "$HERE/appimagetool" ]; then
    curl -Ls -o "$HERE/appimagetool" \
        "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
    chmod +x "$HERE/appimagetool"
fi

echo "=== Packaging AppImage ==="
APPIMAGETOOL="$HERE/appimagetool"
if ! "$APPIMAGETOOL" --appimage-extract-and-run ./AppDir FFIXSaveEditor-x86_64.AppImage 2>/tmp/appimagetool.err; then
    if grep -qi fuse /tmp/appimagetool.err; then
        # No FUSE (common in containers/CI): extract appimagetool once and
        # run it directly instead of relying on it mounting itself.
        if [ ! -d "$HERE/.appimagetool-extracted" ]; then
            (cd "$HERE" && "$APPIMAGETOOL" --appimage-extract >/dev/null && mv squashfs-root .appimagetool-extracted)
        fi
        "$HERE/.appimagetool-extracted/AppRun" ./AppDir FFIXSaveEditor-x86_64.AppImage
    else
        cat /tmp/appimagetool.err >&2
        exit 1
    fi
fi

echo
echo "=== Done ==="
echo "Portable app: $HERE/FFIXSaveEditor-x86_64.AppImage"
echo "chmod +x it and run it directly - no installation needed."
