# FFIX Save Editor

A modern save editor for **Final Fantasy IX**: a CLI (`ffix_save_tool.py`),
a keyboard-driven TUI (`ffix_save_tui.py`, built on
[Textual](https://github.com/Textualize/textual)), and a point-and-click
GUI (`ffix_save_gui.py`, built on [Qt/PySide6](https://doc.qt.io/qtforpython/))
— plus portable, ready-to-run builds of that GUI for Windows and Linux, see
[Portable builds](#portable-builds) below. It's a from-scratch Python rewrite
in the spirit of the excellent but aging WinForms "Memoria" editor (see
`NOTICES.md` for full attribution) — same save-format knowledge, a faster
and more approachable interface.

All three front-ends share the exact same save-format code
(`ffix_save_tool.py` / `ffix_save_data.py` / `ffix_save_memoria.py`), so
format detection and save-file handling stay consistent between them.

## Supported save files

| Format | Where it's from | Typical extension / size |
|---|---|---|
| **legacy** | PS1 original release, played via emulator (DuckStation, PCSX, ePSXe, RetroArch, ...) | `.mcr`/`.mcd`/`.bin` (131072 B, full memory card), `.mcs`/`.ps1` (8320 B, single save + header), or a bare 8192 B block |
| **rr2016** | The 2016 Steam/PC and mobile re-release, vanilla | `.dat` (PC) / `.sav` (iOS/Android), 2937152 B, AES-encrypted |
| **memoria** | Save/autosave files written by the [Memoria mod](https://github.com/Albeoris/Memoria) | `SavedData_*_Memoria_*.dat`, unencrypted, size varies |

The tool auto-detects which one you have by file size and validates the
format-specific headers (with a self-validating trial parse for `memoria`) —
just point it at the file. If you're running the Memoria mod, look for
`SavedData_ww_Memoria_0_0.dat`
(or similarly-numbered) and `SavedData_ww_Memoria_Autosave.dat` next to your
`SavedData_ww.dat` — those are the ones Memoria actually reads/writes and the
ones this tool can edit; the plain `SavedData_ww.dat` may not decrypt if the
mod or a newer game patch changed the vanilla save encryption (see
`NOTICES.md`).

**Always keep a backup of your original save before editing.** The `--out`
option and "Write New File" action refuse to overwrite the input file;
`--in-place` and "Write In-Place" write a `.bak` backup first, automatically.
Existing backups are preserved as `.bak.1`, `.bak.2`, and so on.

Don't have a save file handy to try this on? `examples/` has a real,
fresh-start Memoria-format save you can point any front-end at right away —
see `examples/README.md`.

## Setup

Requires Python 3.10+. Third-party packages needed: `pycryptodome` (AES,
needed by the CLI/TUI/GUI for rr2016 saves), `textual` (TUI only), and
`pyside6-essentials` (GUI only — this one's a fairly large download, it
bundles Qt itself).

```bash
pip install ".[tui,gui]"
```

For only the CLI, use `pip install .`; for only one interface, use
`pip install ".[tui]"` or `pip install ".[gui]"`.

If your system Python is externally managed (PEP 668) and you'd rather not
touch it, install into a local, non-system folder instead and point
`PYTHONPATH` at it:

```bash
pip install --target=.vendor pycryptodome textual pyside6-essentials
PYTHONPATH=.vendor python3 ffix_save_tui.py
```

## TUI

```bash
python3 ffix_save_tui.py [path/to/save]
```

Or launch it with no arguments and type a path into the sidebar. Once
loaded:

- The **Slot** dropdown lists every save the tool found in the file — every
  FFIX memory-card block for legacy saves, or every occupied
  (slot, file) pair for rr2016 saves (it tries all 135 combinations; this
  takes well under a second).
- The **Party** tab shows all character records (9 for legacy/rr2016, 12 for
  Memoria-mod saves). Click a row to load it
  into the editor panel below: name, level, EXP, HP/MP, stats, and equipment
  are all editable Inputs; press **Apply Changes** to write them into memory
  (nothing touches disk until you explicitly write out). Legacy saves also
  show a support-ability checklist.
- **Max Selected Character** / **Max All Characters** in the sidebar set
  level 99, HP 9999, MP 999, and stats 99 in one click.
- **Add item / gear** takes a name from the dropdown or typed text (partial
  matches and `0xNN` hex IDs both work), plus a quantity.
- **Items** and **Cards** tabs show your current inventory and held Tetra
  Master cards read-only.
- **Write New File** writes to the path in the box (or an auto `*.edited.*`
  name next to the input). **Write In-Place** asks for confirmation, then
  backs up the original to a new numbered `.bak` file before overwriting it.

## CLI

```bash
python3 ffix_save_tool.py SAVE.dat --inspect
```

```
format: Steam/PC/mobile (2016) save
slot: Slot 1 / File 1  gil=1,234  playtime=12.3h
party: Zidane, Vivi, Dagger, Steiner
#  name       lvl          hp        mp  str  spd  mag  spr
0  Zidane      12       180/180    30/30   14   16   10   11
...
```

Max out a character and hand them the best sword:

```bash
python3 ffix_save_tool.py SAVE.dat --slot 1 --save 1 \
    --character Zidane --max-character \
    --give-item Ragnarok --out SAVE.edited.dat
```

Give everything, in place (writes `SAVE.dat.bak` first):

```bash
python3 ffix_save_tool.py SAVE.dat --slot 1 --save 1 \
    --give-all-items --quantity 99 --in-place
```

Legacy `.mcr` memory-card images with more than one save need `--block N`
instead of `--slot`/`--save`; single-save legacy files (`.mcs`/`.ps1`/raw)
need neither.

Look up an item's ID:

```bash
python3 ffix_save_tool.py --list-known ragnarok
```

Run `python3 ffix_save_tool.py --help` for the full flag list.

## GUI

Prefer a plain window with buttons and text boxes over a terminal UI? Run:

```bash
python3 ffix_save_gui.py [path/to/save]
```

It has the same core editing features as the TUI (party editor, items, cards,
gil, write out / write in-place with backup) in a plain window instead. The
legacy support-ability checklist is currently TUI-only. File
pickers are Qt's, which on Linux means your desktop's actual native dialog
(KDE, GNOME, etc. via the XDG desktop portal) rather than a generic
fallback. It opens in dark mode by default (a Fusion-style dark palette,
built into Qt — no extra theming dependency); click "Toggle Theme" for
light mode. This is also what's packaged into the portable builds below.

## Portable builds

Don't want to install Python at all? Grab the portable build for your OS
from the [Releases page](../../releases) — no Python, no dependencies,
just download and run:

- **Windows**: `FFIXSaveEditor-vX.Y.Z-windows.exe`
- **Linux**: `FFIXSaveEditor-vX.Y.Z-linux-x86_64.AppImage` — `chmod +x` it
  and go (see `linux/README.md` if your system has no FUSE, or can't find
  the Qt xcb platform plugin — needs the same base X11 libraries any Linux
  desktop already has, just occasionally missing on minimal/server/WSL
  installs).

Both are built automatically by [`.github/workflows/release.yml`](.github/workflows/release.yml)
on a real Windows runner and a real Linux runner whenever a version tag is
pushed — no cross-compilation involved, and no local build step needed
unless you're modifying the source. To build them yourself instead:
`windows\build.bat` (must run on an actual Windows machine — see
`windows/README.md` for why) and `linux/build.sh` (see `linux/README.md`).

## What's editable

Gil, playtime (read-only), location (legacy, read-only), the current party
(rr2016, read-only), and per-character level/EXP/HP/MP/base stats/equipment,
plus inventory (add or raise the quantity of any of the 256 known items and gear
pieces) and, for legacy saves, the 64 support abilities. Tetra Master cards
are listed but not yet editable from the UI (the record layout is known for
both formats — see `ffix_save_tool.py::Slot.set_card` — a CLI/TUI hook is a
reasonable follow-up).

The rr2016 equipment-slot offsets are confirmed by the reference editor's live
control mappings and cross-checked against real vanilla saves; see
`NOTICES.md` for validation details.

## Tests

```bash
python -m unittest discover -v
```

The committed tests generate synthetic PS1, encrypted rr2016, and Memoria
fixtures in memory. The included fresh-start Memoria save is useful for manual
testing, but the automated suite does not depend on redistributed player saves.

## Project layout

- `ffix_save_data.py` — item/ability/card name tables and the legacy PS1
  text font codec. No save-format logic.
- `ffix_save_memoria.py` — parser/serializer for the separate, unencrypted
  Memoria-mod save format.
- `ffix_save_tool.py` — all other save-format logic (detection, AES,
  checksums, field layout, read/write) plus the CLI. No UI code.
- `ffix_save_tui.py` — the Textual UI. Imports everything from the files
  above, so the CLI and TUI can never drift apart.
- `ffix_save_gui.py` — the Qt (PySide6) GUI. Same idea: no save-format
  logic of its own.
- `assets/` — the app icon (`icon.png` / `icon.ico`), used by the portable
  builds.
- `windows/`, `linux/` — portable builds of the GUI for each platform; see
  [Portable builds](#portable-builds) above.
- `examples/` — a sample save file to try the tools on.

## License

MIT (see `pyproject.toml`). Save-format knowledge and in-game name tables
are attributed in `NOTICES.md`.
