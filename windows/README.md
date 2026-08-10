# Windows build

Produces a single portable `FFIXSaveEditor.exe` with a simple point-and-click
GUI (no terminal/TUI knowledge needed) - copy that one file anywhere and run
it, nothing else to install.

**Copy the whole project folder to this machine, not just `windows\`.**
`build.bat` needs `..\ffix_save_gui.py` and its sibling files to actually
build from - if you only copied this `windows` subfolder on its own (e.g.
you zipped just this folder to move it), the build will fail immediately.
`build.bat` checks for this and tells you plainly if it's the problem.

## Why you have to build this yourself

Windows `.exe` files have to be built on Windows - PyInstaller bundles the
actual Python interpreter and native libraries for whatever OS it's run on,
and there's no reliable way to cross-compile a real Windows executable from
Linux/macOS (Wine-based hacks exist but produce unverified, often-broken
output, so this project doesn't rely on one). If you'd rather not build it
yourself, the TUI (`../ffix_save_tui.py`) and this same GUI
(`../ffix_save_gui.py`) both run directly from source with `python
ffix_save_gui.py` once you `pip install pycryptodome pyside6-essentials`
(and `textual` for the TUI) - no build step required either way.

## Build steps

1. Install Python 3.10+ from [python.org](https://www.python.org/downloads/)
   if you don't already have it. On the installer's first screen, check
   "Add python.exe to PATH".
2. Double-click `build.bat`, or run it from a command prompt in this folder.
3. Wait for it to finish - it creates a throwaway virtual environment,
   installs PyInstaller + pycryptodome + pyside6-essentials into it (this
   is a fairly large download, PySide6 bundles Qt itself), and builds the
   exe.
4. The finished portable exe is at `dist\FFIXSaveEditor.exe`.

## If the build fails

`build.bat` always leaves the window open (press a key to close it) and
writes everything to `build.log` next to it, whether the build succeeds or
fails - if the window closed on you before you could read anything, you're
looking at an older copy of this script; the current one shouldn't do that.
Check `build.log` for the actual error. Two causes seen in practice:

- **Python isn't on PATH.** Reinstall Python and make sure "Add python.exe
  to PATH" is checked, or the script (and pip) can't find it.
- **Antivirus interference.** PyInstaller's bootloader is a small, generic,
  unsigned exe that some antivirus products flag or quarantine on sight,
  which can make the build fail partway with no obvious Python-level error.
  If `build.log` doesn't point to a clear cause, check your antivirus's
  quarantine/history and add an exclusion for this folder if it's there.

## What it can't do (yet)

This spec builds a `--onefile --windowed` exe: no console window, and
PyInstaller unpacks itself to a temp folder on each launch (slightly slower
startup, totally normal for onefile builds). Icon is `../assets/icon.ico`.
