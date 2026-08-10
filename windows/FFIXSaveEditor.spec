# -*- mode: python ; coding: utf-8 -*-
# PyInstaller spec for the portable Windows build.
# Build ON Windows: see build.bat / README.md in this folder.
#
# This is deliberately close to what `pyinstaller --onefile --windowed
# --icon assets/icon.ico ffix_save_gui.py` generates on its own - hand
# edited only to use paths relative to this folder so it works regardless
# of where the repo is cloned.
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(SPEC)), ".."))

a = Analysis(
    [os.path.join(ROOT, "ffix_save_gui.py")],
    pathex=[ROOT],
    binaries=[],
    datas=[],
    hiddenimports=[],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name="FFIXSaveEditor",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=[os.path.join(ROOT, "assets", "icon.ico")],
)
