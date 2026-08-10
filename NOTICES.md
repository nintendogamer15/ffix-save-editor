# Third-party notices and attribution

## Save-format knowledge

The byte offsets, container formats, checksum algorithm, AES key/salt, and
item/ability/card name tables used by this project were reverse-engineered by
the **Memoria FF9 Save Editor** project (Gjoerulv; forum thread at
forums.qhimm.com, topic 11494), a copy of which was provided as reference
material for this project (`Memoria_FF9SaveEditor-main/`, a C#/WinForms
application). No source file from that project is copied or redistributed
here — this project is an independent Python re-implementation written from
scratch, but it would not have been possible without that prior
reverse-engineering work, especially for the AES-encrypted 2016 re-release
format. No license file was included with the reference copy; this project
credits it in good faith under standard reverse-engineering/interoperability
norms.

Specifically derived from that project's analysis:

- The legacy PS1 memory-card save layout (character table, item table, card
  table, checksum region) in `Memoria_FF9SaveEditor-main/SaveMap.cs` and
  `PSX.cs`.
- The CRC16-CCITT checksum algorithm and byte range, from
  `SelectSave.cs::SetChecksum` and `Crc/Crc16.cs`.
- The AES-256-CBC / PBKDF2-HMAC-SHA1 key derivation, salt, and container
  layout for the 2016 Steam/PC/mobile re-release format, from
  `Crc/AESCryptography.cs` and `ReUtils/DataManager.cs`.
- The Tetra Master card record layout for both formats (including the
  reordered 11-byte record used by the re-release), from `Card.cs`.
- The item, support-ability, and Tetra Master card name tables, transcribed
  from `Properties/Resources.resx` (`Items`, `Abl`) and `Save.cs`
  (`cardTypes`). These are Final Fantasy IX's own in-game text, not the
  editor project's original creative work.
- The legacy PS1 menu/name font table (character <-> byte code), from
  `SelectSave.cs::characterTable`.

## What is confirmed vs. experimental

Offsets that are exercised by the reference tool's live editing code paths
(not just declared as constants) are treated as confirmed:
gil, playtime, location, party-leader name/level (legacy); all legacy
character stat/equipment/item/card offsets; the rr2016 metadata/container
layout, item table, card table, gil, and party-member array; and, for
rr2016 characters, level/name (cross-validated against `SelectSave.cs`'s
`FillGrid`, which independently reads the same character at fixed offsets
+48/+57 for the grid preview).

The rr2016 **equipment slot** offsets (weapon/head/arm/armor/accessory) are
**not** independently confirmed. `SaveMap.cs` only declares
`CHARECTER1_EQUIP_START_OFFSET_RR` (where the block starts); the individual
slot order was inferred by analogy with the legacy layout. Editing them is
exposed in this tool but flagged in the UI as experimental — worst case is a
garbled equipment display recoverable by editing it again, not a corrupted
save. The rr2016 per-character ability/AP-progress table is not exposed for
editing at all for the same reason (offset known, internal layout unknown).

## The Memoria mod save format

The [Memoria mod](https://github.com/Albeoris/Memoria) writes its own
additional save files (e.g. `SavedData_ww_Memoria_0_0.dat`,
`SavedData_ww_Memoria_Autosave.dat`) in a completely different, third
format — an unencrypted, self-describing tagged binary tree, apparently a
hand-rolled variant of .NET's `BinaryWriter`/`BinaryReader` string
convention. This is *not* the same file as the vanilla encrypted
`SavedData_ww.dat` container (that file is still handled by the rr2016 path
above) and was not covered by anything in the reference project — it was
reverse-engineered from scratch for this project directly from two real
save files, with no prior documentation consulted. See
`ffix_save_memoria.py` for the full format writeup.

Confidence: the parser/serializer round-trips both sample files
byte-for-byte (parse → reserialize reproduces the exact original bytes),
which is strong confirmation of the tagged-value encoding itself. The
character-stat field *names* Memoria uses internally (`elem.dex`,
`elem.str`, `elem.mgc`, `elem.wpr`) are mapped to FF9's four base stats
(Speed, Strength, Magic, Spirit respectively) by inference — plausible
given the four-stat model, cross-checked against one real character's
starting equipment (`equip: [1, 112, 88, 149, 255]` decodes as Dagger/
Leather Hat/Wrist/Leather Shirt/none — an exactly correct level-1 Zidane
loadout), but not confirmed the way the legacy/rr2016 field names are.
Support-ability data (`sa_extended`) is present in the format but not
decoded or exposed for editing.

## Python dependencies

This project uses:

- [Textual](https://github.com/Textualize/textual) (MIT License) for the TUI.
- [PyCryptodome](https://github.com/Legrandin/pycryptodome) (BSD-2-Clause /
  public domain) for AES.
- [PySide6](https://doc.qt.io/qtforpython/) (LGPL-3.0 — the official Qt for
  Python bindings; only the `pyside6-essentials` subset is used, not the
  full `pyside6` metapackage) for the GUI. LGPL-3.0 permits use in
  differently-licensed applications as long as PySide6 itself isn't
  modified and users can still replace/relink it, both true here: it's an
  unmodified pip dependency, and running from source (`pip install
  pyside6-essentials`) always works as an alternative to the bundled
  portable builds.

These are installed via `pip` and are not vendored into this repository
(see README.md). The portable builds in `windows/` and `linux/` additionally
use [PyInstaller](https://github.com/pyinstaller/pyinstaller) (GPL-2.0-or-later
with an explicit exception allowing use with proprietary/differently-licensed
applications, per PyInstaller's own license terms) and, for the AppImage,
[appimagetool](https://github.com/AppImage/appimagetool) (MIT License) —
both are build-time tools only, not bundled as source in this repository.

## License of this project

Code in this repository (`ffix_save_tool.py`, `ffix_save_tui.py`,
`ffix_save_data.py`) is original work released under the MIT License (see
`pyproject.toml`). This does not extend to Final Fantasy IX's own game data
(item/ability/card names), which is Square Enix's intellectual property and
is used here only as factual, non-executable reference data necessary for
the save editor to function — the same basis on which every other FF9 save
editor has published such tables.
