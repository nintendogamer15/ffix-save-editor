# Third-party notices and attribution

## Save-format knowledge

The byte offsets, container formats, checksum algorithm, AES key/salt, and
item/ability/card name tables used by this project were reverse-engineered by
the **Memoria FF9 Save Editor** project (Gjoerulv; forum thread at
forums.qhimm.com, topic 11494), a copy of which was provided as reference
material for this project (`Memoria_FF9SaveEditor-main/`, a C#/WinForms
application). No source file from that project is copied or redistributed
here — this project is an independent C#/.NET re-implementation written from
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
  `Crc/AESCryptography.cs` and `ReUtils/DataManager.cs`. The reference calls
  `.ToString()` on a .NET `SecureString`; vanilla saves consequently use the
  literal type name `System.Security.SecureString` as the PBKDF2 password,
  rather than the UUID text that was appended to that object.
- The Tetra Master card record layout for both formats (including the
  reordered 11-byte record used by the re-release), from `Card.cs`.
- The item, support-ability, and Tetra Master card name tables, transcribed
  from `Properties/Resources.resx` (`Items`, `Abl`) and `Save.cs`
  (`cardTypes`). These are Final Fantasy IX's own in-game text, not the
  editor project's original creative work.
- The legacy PS1 menu/name font table (character <-> byte code), from
  `SelectSave.cs::characterTable`.

## Layout validation

Offsets that are exercised by the reference tool's live editing code paths
(not just declared as constants) are treated as confirmed:
gil, playtime, location, party-leader name/level (legacy); all legacy
character stat/equipment/item/card offsets; the rr2016 metadata/container
layout, item table, card table, gil, party-member array, and equipment slots;
and, for rr2016 characters, level/name (cross-validated against `SelectSave.cs`'s
`FillGrid`, which independently reads the same character at fixed offsets
+48/+57 for the grid preview).

The rr2016 equipment order (weapon/head/arm/armor/accessory at relative
offsets +33 through +37) is used directly by the reference editor's live GUI
control mappings and has also been cross-checked against plausible complete
loadouts in vanilla saves. The rr2016 per-character ability/AP-progress table
is not exposed for editing because its internal layout is still unknown.

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
save files, with no prior documentation consulted. See the implementation in
`src/FFIX.SaveEditor.Core/MemoriaFormat.cs` and its regression tests for the
tagged-value details.

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

## Runtime and build dependencies

- [.NET](https://dotnet.microsoft.com/) (MIT License) provides the runtime,
  standard cryptography APIs, and SDK. Release executables include the required
  runtime under Microsoft's .NET distribution terms.
- [Avalonia UI](https://avaloniaui.net/) (MIT License) provides the desktop
  interface and its DataGrid, Fluent theme, and Inter font packages.
- xUnit and Microsoft.NET.Test.Sdk are development-only test dependencies.

Dependencies are restored from NuGet and are not vendored. Standard .NET
RID-specific publishing creates the Windows and Linux releases.

## License of this project

Code in this repository is original work released under the MIT License (see
`LICENSE`). This does not
extend to Final Fantasy IX's own game data
(item/ability/card names), which is Square Enix's intellectual property and
is used here only as factual, non-executable reference data necessary for
the save editor to function — the same basis on which every other FF9 save
editor has published such tables.
