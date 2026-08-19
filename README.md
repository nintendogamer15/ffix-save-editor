# FFIX Save Editor

A cross-platform save editor for Final Fantasy IX. Version 0.3.0 is implemented in C# on .NET 10, with an Avalonia desktop interface, a batch CLI, and an interactive terminal mode.

## Supported saves

| Format | Source | Recognized form |
|---|---|---|
| PS1 legacy | Original game via emulator | 8,192-byte raw block; 8,320-byte single-save wrapper; 131,072-byte memory-card image |
| rr2016 | Vanilla Steam/PC and mobile rerelease | 2,937,152-byte encrypted `.dat`/`.sav` container |
| Memoria | Memoria mod | Variable-size unencrypted `SavedData_*_Memoria_*.dat` |

The editor detects and validates the format, lists occupied saves, and preserves container bytes that are not part of an edit. A fresh-start Memoria fixture is included under `examples/`.

## Features

- View and edit character names, level, EXP, HP, MP, base stats, and equipment.
- Max one character or every recruited character.
- View and edit gil.
- View inventory and add any of the 256 known item/gear IDs.
- View Tetra Master cards and the win/loss/draw record.
- Edit all 64 support-ability bits in PS1 saves.
- Repair the PS1 CRC automatically after edits.
- Decrypt and re-encrypt vanilla rr2016 slots while preserving reserved container data.
- Round-trip Memoria's tagged format while preserving dictionary order and numeric value types.
- Write a new file by default; explicit in-place writes create non-overwriting numbered backups.

## Download and use

Tagged releases provide:

- `FFIXSaveEditor-vX.Y.Z-windows-x64.exe`
- `FFIXSaveEditor-vX.Y.Z-linux-x64`

Both are self-contained and do not require a separate .NET installation. Windows builds are unsigned.

On Windows, run the `.exe`. On Linux:

```bash
chmod +x FFIXSaveEditor-vX.Y.Z-linux-x64
./FFIXSaveEditor-vX.Y.Z-linux-x64
```

Open a save, select an occupied slot/block, make edits in memory, then use **Write New File**. Keep your original until the edited save has loaded successfully in-game. In-place writing is available but requires confirmation and creates `.bak`, `.bak.1`, and later numbered backups.

## Command line

Examples from the source tree:

```bash
dotnet run --project src/FFIX.SaveEditor.Cli -- SAVE.dat --inspect
dotnet run --project src/FFIX.SaveEditor.Cli -- SAVE.dat --slot 1 --save 1 --character Zidane --max-character --give-item Ragnarok --out SAVE.edited.dat
dotnet run --project src/FFIX.SaveEditor.Cli -- SAVE.mcr --block 1 --interactive
dotnet run --project src/FFIX.SaveEditor.Cli -- --list-known ragnarok
```

Run with `--help` for all batch and slot-selection options. `--interactive` and `--tui` start the same keyboard-driven terminal editor.

## Build from source

Install the .NET 10 SDK, then run:

```bash
dotnet restore FFIX.SaveEditor.slnx
dotnet build FFIX.SaveEditor.slnx --configuration Release
dotnet test FFIX.SaveEditor.slnx --configuration Release
./scripts/build-release.sh v0.3.0
```

The release script runs on Linux and publishes both `win-x64` and `linux-x64` self-contained single-file applications. Output goes to `artifacts/` unless another directory is supplied.

## Limitations

- rr2016 support targets the known vanilla fixed-key container. Saves using a modded/newer encryption scheme are rejected rather than guessed at.
- Tetra Master cards are displayed but not edited by the interfaces.
- rr2016 ability/AP records and Memoria `sa_extended` support-ability data remain unknown and are not edited.
- Memoria mod item IDs are preserved and displayed in full, but names unavailable in the vanilla table are shown as numeric IDs because the save does not contain the mod's item-name catalog.

## License and credits

The project is MIT licensed. The earlier Memoria FF9 Save Editor's format research and the origins of the in-game tables are credited in [`NOTICES.md`](NOTICES.md). Final Fantasy IX and its data belong to Square Enix.
