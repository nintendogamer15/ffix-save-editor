// SPDX-License-Identifier: MIT
using FFIX.SaveEditor.Core;

return Cli.Run(args);

internal static class Cli
{
    public static int Run(string[] args)
    {
        try
        {
            var options = Parse(args);
            if (options.Help) { PrintHelp(); return 0; }
            if (options.ListKnown is not null)
            {
                for (var itemId = 0; itemId < GameData.ItemNames.Count; itemId++)
                    if (itemId != GameData.EmptyItemId && GameData.ItemNames[itemId].Contains(options.ListKnown, StringComparison.OrdinalIgnoreCase))
                        Console.WriteLine($"0x{itemId:X2}  {GameData.ItemNames[itemId]}");
                return 0;
            }
            if (options.Interactive)
                return RunInteractive(options.Path);
            if (options.Path is null)
                throw new ArgumentException("A save path is required unless --list-known or --interactive is used.");

            var document = SaveDocument.Open(options.Path);
            if (options.ListSlots)
            {
                Inspect(document, null);
                return 0;
            }
            var reference = SelectReference(document, options);
            var hasEdits = options.MaxCharacter || options.MaxAll || options.SetGil is not null
                           || options.GiveItems.Count != 0 || options.GiveAllItems;
            if (options.Inspect && !hasEdits)
            {
                Inspect(document, reference);
                return 0;
            }
            if (!hasEdits)
            {
                Inspect(document, reference);
                return 0;
            }

            var slot = document.LoadSlot(reference);
            var changes = new List<string>();
            if (options.MaxCharacter)
            {
                if (options.Character is null) throw new ArgumentException("--max-character requires --character NAME_OR_INDEX.");
                var character = ResolveCharacter(slot, options.Character);
                character.MaxOut();
                changes.Add($"maxed {character.Name}");
            }
            if (options.MaxAll)
            {
                var characters = slot.Characters().Where(x => x.IsRecruited).ToArray();
                foreach (var character in characters) character.MaxOut();
                changes.Add($"maxed {characters.Length} character(s)");
            }
            if (options.SetGil is not null)
            {
                slot.Gil = options.SetGil.Value;
                changes.Add($"gil={slot.Gil}");
            }
            var quantity = Math.Clamp(options.Quantity, 0, 99);
            foreach (var token in options.GiveItems)
            {
                var itemId = GameData.ResolveItemId(token);
                var success = slot.SetItem(itemId, quantity);
                changes.Add($"gave {GameData.ItemName(itemId)} x{quantity}" + (success ? "" : " (inventory full!)"));
            }
            if (options.GiveAllItems)
                changes.Add($"gave {SaveDocument.GiveAllItems(slot, quantity)} items/gear pieces");

            document.CommitSlot(reference, slot);
            foreach (var change in changes) Console.WriteLine(change);
            if (options.Output is not null)
            {
                SafeFileWriter.WriteNew(options.Path, options.Output, document);
                Console.WriteLine($"wrote: {options.Output}");
            }
            else if (options.InPlace)
                Console.WriteLine($"wrote in-place; backup: {SafeFileWriter.WriteInPlaceWithBackup(options.Path, document)}");
            else
                Console.WriteLine("(no --out/--in-place given; nothing written to disk)");
            return 0;
        }
        catch (FileNotFoundException exception)
        {
            Console.Error.WriteLine($"Could not open save: {exception.Message}");
            return 1;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"Could not read/write save: {exception.Message}");
            return 1;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"Could not access save: {exception.Message}");
            return 1;
        }
        catch (Exception exception) when (exception is ArgumentException or SaveFormatException or InvalidOperationException
                                          or FormatException or OverflowException)
        {
            Console.Error.WriteLine($"Could not process save: {exception.Message}");
            return 2;
        }
    }

    private static Options Parse(string[] args)
    {
        var options = new Options();
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            string Value() => ++index < args.Length ? args[index] : throw new ArgumentException($"Missing value for {argument}.");
            switch (argument)
            {
                case "-h" or "--help": options.Help = true; break;
                case "--interactive" or "--tui": options.Interactive = true; break;
                case "--slot": options.Slot = Bounded(Value(), "slot", 1, SaveLayout.RrMaximumSlots); break;
                case "--save": options.Save = Bounded(Value(), "file", 1, SaveLayout.RrMaximumSaves); break;
                case "--block": options.Block = Bounded(Value(), "block", 1, SaveLayout.LegacyMaximumBlocks - 1); break;
                case "--inspect": options.Inspect = true; break;
                case "--list-slots": options.ListSlots = true; break;
                case "--character": options.Character = Value(); break;
                case "--max-character": options.MaxCharacter = true; break;
                case "--max-all": options.MaxAll = true; break;
                case "--set-gil": options.SetGil = int.Parse(Value()); break;
                case "--give-item": options.GiveItems.Add(Value()); break;
                case "--give-all-items": options.GiveAllItems = true; break;
                case "--quantity": options.Quantity = int.Parse(Value()); break;
                case "--out": options.Output = Value(); break;
                case "--in-place": options.InPlace = true; break;
                case "--list-known":
                    options.ListKnown = index + 1 < args.Length && !args[index + 1].StartsWith('-') ? args[++index] : "";
                    break;
                default:
                    if (argument.StartsWith('-')) throw new ArgumentException($"Unknown option: {argument}");
                    if (options.Path is not null) throw new ArgumentException("Only one save path may be supplied.");
                    options.Path = argument;
                    break;
            }
        }
        if (options.Output is not null && options.InPlace) throw new ArgumentException("Use either --out or --in-place, not both.");
        return options;
    }

    private static int Bounded(string value, string label, int minimum, int maximum)
    {
        if (!int.TryParse(value, out var number) || number < minimum || number > maximum)
            throw new ArgumentException($"{label} must be {minimum}-{maximum}.");
        return number;
    }

    private static SlotReference SelectReference(SaveDocument document, Options options)
    {
        var references = document.ListSlots();
        if (document.Format == SaveFormat.Rr2016)
        {
            if (options.Slot is not null && options.Save is not null)
                return document.ProbeRrSlot(options.Slot.Value - 1, options.Save.Value - 1)
                       ?? throw new ArgumentException($"Slot {options.Slot} / File {options.Save} is empty or unreadable.");
            if (references.Count == 1) return references[0];
            if (references.Count == 0) throw new ArgumentException("No occupied rr2016 slots found.");
            throw new ArgumentException("Multiple rr2016 saves found; pass --slot N --save M:\n" +
                                        string.Join('\n', references.Select(x => $"  --slot {x.SlotId + 1} --save {x.SaveId + 1}: {x.Summary}")));
        }
        if (options.Block is not null)
            return references.FirstOrDefault(x => x.BlockIndex == options.Block)
                   ?? throw new ArgumentException($"Block {options.Block} is empty or not an FFIX save.");
        if (references.Count == 1) return references[0];
        if (references.Count == 0) throw new ArgumentException("No FFIX save data found.");
        throw new ArgumentException("Multiple blocks found; pass --block N:\n" +
                                    string.Join('\n', references.Select(x => $"  --block {x.BlockIndex}: {x.Summary}")));
    }

    private static IEditableCharacter ResolveCharacter(IEditableSlot slot, string token)
    {
        if (int.TryParse(token, out var index)) return slot.Character(index);
        var exact = slot.Characters().FirstOrDefault(x => string.Equals(x.Name, token, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var matches = slot.Characters().Where(x => x.Name.Contains(token, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0] : throw new ArgumentException($"Unknown character: '{token}'.");
    }

    private static void Inspect(SaveDocument document, SlotReference? reference)
    {
        Console.WriteLine($"format: {SaveDocument.FormatLabel(document.Format)}");
        if (document.Metadata is { } metadata)
            Console.WriteLine($"metadata: version={metadata.SaveVersion} data_size={metadata.DataSize} " +
                              $"latest_slot={metadata.LatestSlot + 1} latest_save={metadata.LatestSave + 1}");
        if (reference is null)
        {
            foreach (var item in document.ListSlots()) Console.WriteLine($"  [{item.Label}] {item.Summary}");
            return;
        }
        var slot = document.LoadSlot(reference);
        Console.WriteLine($"slot: {reference.Label}  gil={slot.Gil:N0}  playtime={slot.PlaytimeSeconds / 3600:F1}h");
        if (slot.Location is not null) Console.WriteLine($"location: {slot.Location}");
        if (slot.PartyMemberIds is not null)
            Console.WriteLine("party: " + string.Join(", ", slot.PartyMemberIds.Select(x => slot.Character(x).Name)));
        Console.WriteLine($"{"#",-3}{"name",-10}{"lvl",4}{"hp",12}{"mp",10}{"str",5}{"spd",5}{"mag",5}{"spr",5}");
        foreach (var character in slot.Characters().Where(x => x.IsRecruited))
        {
            var maxHp = Field(character, "max_hp");
            var maxMp = Field(character, "max_mp");
            Console.WriteLine($"{character.Index,-3}{character.Name,-10}{character.Get("level"),4}" +
                              $"{$"{character.Get("cur_hp")}/{character.Get(maxHp)}",12}" +
                              $"{$"{character.Get("cur_mp")}/{character.Get(maxMp)}",10}" +
                              $"{character.Get(Field(character, "strength")),5}{character.Get(Field(character, "speed")),5}" +
                              $"{character.Get(Field(character, "magic")),5}{character.Get(Field(character, "spirit")),5}");
        }
        Console.WriteLine($"cards held: {slot.Cards().Count}  record: {slot.CardRecord.Wins}W-{slot.CardRecord.Losses}L-{slot.CardRecord.Draws}D");
        Console.WriteLine($"inventory: {slot.Items().Count} entries");
    }

    private static string Field(IEditableCharacter character, string name) => character.Has(name) ? name : name + "_base";

    private static int RunInteractive(string? initialPath)
    {
        Console.WriteLine("Final Fantasy IX Save Editor — interactive terminal mode");
        Console.Write("Save path" + (initialPath is null ? ": " : $" [{initialPath}]: "));
        var entered = Console.ReadLine()?.Trim();
        var path = string.IsNullOrWhiteSpace(entered) ? initialPath : entered;
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A save path is required.");
        var document = SaveDocument.Open(path);
        var reference = ChooseReference(document);
        var slot = document.LoadSlot(reference);
        var selectedCharacter = 0;
        var dirty = false;
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"{reference.Label}; character row {selectedCharacter}.  1 Inspect  2 Select character  3 Edit character");
            Console.WriteLine("4 Max selected  5 Max all  6 Set gil  7 Add item  8 Give all  9 Support abilities");
            Console.WriteLine("O Choose slot  S Write new  I Write in-place  Q Quit");
            Console.Write("> ");
            switch (Console.ReadLine()?.Trim().ToUpperInvariant())
            {
                case "1": document.CommitSlot(reference, slot); Inspect(document, reference); break;
                case "2": Console.Write("Character row: "); selectedCharacter = Bounded(Console.ReadLine() ?? "", "row", 0, slot.Characters().Count - 1); break;
                case "3": slot = EditCharacterTransaction(slot, selectedCharacter); document.CommitSlot(reference, slot); dirty = true; break;
                case "4": slot.Character(selectedCharacter).MaxOut(); document.CommitSlot(reference, slot); dirty = true; break;
                case "5": foreach (var character in slot.Characters().Where(x => x.IsRecruited)) character.MaxOut(); document.CommitSlot(reference, slot); dirty = true; break;
                case "6": Console.Write("Gil: "); slot.Gil = int.Parse(Console.ReadLine() ?? "0"); document.CommitSlot(reference, slot); dirty = true; break;
                case "7":
                    Console.Write("Item name/ID: "); var item = GameData.ResolveItemId(Console.ReadLine() ?? "");
                    Console.Write("Quantity [99]: "); var quantityText = Console.ReadLine();
                    slot.SetItem(item, string.IsNullOrWhiteSpace(quantityText) ? 99 : int.Parse(quantityText));
                    document.CommitSlot(reference, slot); dirty = true; break;
                case "8": SaveDocument.GiveAllItems(slot, 99); document.CommitSlot(reference, slot); dirty = true; break;
                case "9": slot = EditSupportAbilities(slot, selectedCharacter); document.CommitSlot(reference, slot); dirty = true; break;
                case "O": document.CommitSlot(reference, slot); reference = ChooseReference(document); slot = document.LoadSlot(reference); selectedCharacter = 0; break;
                case "S":
                    Console.Write($"Output [{DefaultOutput(path)}]: "); var output = Console.ReadLine()?.Trim();
                    SafeFileWriter.WriteNew(path, string.IsNullOrEmpty(output) ? DefaultOutput(path) : output, document); dirty = false; break;
                case "I":
                    Console.Write("Type OVERWRITE to confirm: ");
                    if (Console.ReadLine() == "OVERWRITE") { Console.WriteLine($"Backup: {SafeFileWriter.WriteInPlaceWithBackup(path, document)}"); dirty = false; }
                    break;
                case "Q": if (dirty) Console.WriteLine("Unsaved in-memory changes were not written."); return 0;
            }
        }
    }

    private static SlotReference ChooseReference(SaveDocument document)
    {
        var references = document.ListSlots();
        if (references.Count == 0) throw new ArgumentException("No occupied saves found.");
        if (references.Count == 1) return references[0];
        for (var index = 0; index < references.Count; index++) Console.WriteLine($"{index + 1}: {references[index].Label} {references[index].Summary}");
        Console.Write("Choose: ");
        return references[Bounded(Console.ReadLine() ?? "", "selection", 1, references.Count) - 1];
    }

    private static IEditableSlot EditCharacterTransaction(IEditableSlot slot, int index)
    {
        var candidate = slot.Clone();
        var character = candidate.Character(index);
        Console.Write($"Name [{character.Name}]: "); var name = Console.ReadLine();
        if (!string.IsNullOrEmpty(name)) character.Name = name;
        foreach (var field in new[] { "level", "exp", "cur_hp", "max_hp", "cur_mp", "max_mp", "strength", "speed", "magic", "spirit" })
        {
            var actual = character.Has(field) ? field : field + "_base";
            if (!character.Has(actual)) continue;
            Console.Write($"{field} [{character.Get(actual)}]: "); var value = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(value)) character.Set(actual, int.Parse(value));
        }
        foreach (var equipment in SaveLayout.EquipmentSlots.Where(character.Has))
        {
            Console.Write($"{equipment} [{GameData.ItemName(character.Get(equipment))}]: "); var value = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(value)) character.Set(equipment, GameData.ResolveItemId(value));
        }
        return candidate;
    }

    private static IEditableSlot EditSupportAbilities(IEditableSlot slot, int index)
    {
        var candidate = slot.Clone();
        var character = candidate.Character(index);
        if (character.Format != SaveFormat.Legacy) throw new InvalidOperationException("Support abilities are only editable in legacy saves.");
        Console.WriteLine("Enabled: " + string.Join(", ", character.SupportAbilities().Select(x => $"{x}:{GameData.SupportAbilityNames[x]}")));
        Console.Write("Enter comma-separated ability IDs to enable (blank clears all): ");
        var selected = (Console.ReadLine() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse).ToHashSet();
        for (var ability = 0; ability < GameData.SupportAbilityNames.Count; ability++) character.SetSupportAbility(ability, selected.Contains(ability));
        return candidate;
    }

    private static string DefaultOutput(string path) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,
        Path.GetFileNameWithoutExtension(path) + ".edited" + Path.GetExtension(path));

    private static void PrintHelp() => Console.WriteLine("""
        FFIXSaveEditor.Cli SAVE [options]
          --slot N --save N          rr2016 slot/file (1-9 / 1-15)
          --block N                  PS1 memory-card block (1-15)
          --inspect / --list-slots   Inspect one save or list occupied saves
          --character NAME_OR_INDEX --max-character
          --max-all                  Max every recruited character
          --set-gil N
          --give-item NAME_OR_ID     Repeatable item/gear upsert
          --give-all-items --quantity N
          --out PATH / --in-place    Safe output or numbered-backup overwrite
          --list-known [FILTER]
          --interactive, --tui       Interactive terminal editor
        """);

    private sealed class Options
    {
        public string? Path { get; set; }
        public bool Help { get; set; }
        public bool Interactive { get; set; }
        public int? Slot { get; set; }
        public int? Save { get; set; }
        public int? Block { get; set; }
        public bool Inspect { get; set; }
        public bool ListSlots { get; set; }
        public string? ListKnown { get; set; }
        public string? Character { get; set; }
        public bool MaxCharacter { get; set; }
        public bool MaxAll { get; set; }
        public int? SetGil { get; set; }
        public List<string> GiveItems { get; } = [];
        public bool GiveAllItems { get; set; }
        public int Quantity { get; set; } = 99;
        public string? Output { get; set; }
        public bool InPlace { get; set; }
    }
}
