# Linux build

`FFIXSaveEditor-x86_64.AppImage` in this folder is a portable, self-contained
build of the simple GUI editor (no terminal/TUI knowledge needed) - it
bundles its own Python and Qt, so it works even on a system with no Python
installed at all. It's already built; you don't need to do anything to use
it:

```bash
chmod +x FFIXSaveEditor-x86_64.AppImage
./FFIXSaveEditor-x86_64.AppImage
```

If your system doesn't have FUSE set up (common in containers/minimal
installs - you'll see an error mentioning `fusermount` or `libfuse`), run it
with `--appimage-extract-and-run` instead, which needs no FUSE:

```bash
./FFIXSaveEditor-x86_64.AppImage --appimage-extract-and-run
```

You can also pass a save file path as an argument to open it immediately,
same as the other front-ends: `./FFIXSaveEditor-x86_64.AppImage /path/to/save`.

## "Could not load the Qt platform plugin" / xcb errors

Qt needs a handful of base X11 libraries at runtime - `libxkbcommon`,
`libxcb-cursor` (sometimes packaged as `xcb-util-cursor`), and friends.
Every normal Linux desktop (KDE, GNOME, XFCE, ...) already has these, since
literally every other X11/Wayland GUI app needs them too - this only tends
to show up on minimal server installs, some Docker/WSL setups, or other
non-desktop environments. Install your distro's base X11/xcb packages (on
Debian/Ubuntu-family systems: `libxkbcommon-x11-0`, `libxcb-cursor0`).

## Rebuilding it

```bash
./build.sh
```

This regenerates `FFIXSaveEditor-x86_64.AppImage` from the current source in
`../ffix_save_gui.py` etc. PySide6 (Qt for Python) ships as ordinary,
self-contained pip wheels - no system Qt/build tools needed - so this
mainly needs a working Python 3 with pip; if your system doesn't have one,
the script bootstraps its own local Python 3.11 via
[micromamba](https://mamba.readthedocs.io/en/latest/user_guide/micromamba.html)
(no root/sudo required) so the build works either way. `appimagetool` is
downloaded once and cached in this folder.

Everything `build.sh` creates other than the final `.AppImage` (build/,
dist/, AppDir/, .buildenv/, the cached micromamba/appimagetool binaries) is
gitignored scaffolding - safe to delete any time, it'll be regenerated.
