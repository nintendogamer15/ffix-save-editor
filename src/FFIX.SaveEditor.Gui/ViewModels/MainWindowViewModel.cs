// SPDX-License-Identifier: MIT
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FFIX.SaveEditor.Core;

namespace FFIX.SaveEditor.Gui.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private SlotChoice? _selectedSlot;
    private CharacterRow? _selectedCharacter;
    private string _overview = "No save loaded.";
    private string _log = "Ready. Choose a supported FFIX save and press Load.";

    public ObservableCollection<SlotChoice> Slots { get; } = [];
    public ObservableCollection<CharacterRow> Characters { get; } = [];
    public ObservableCollection<InventoryItem> Items { get; } = [];
    public ObservableCollection<CardInfo> Cards { get; } = [];
    public ObservableCollection<AbilityChoice> Abilities { get; } = [];
    public IReadOnlyList<string> KnownItems { get; } = GameData.ItemNames;

    public SlotChoice? SelectedSlot
    {
        get => _selectedSlot;
        set => Set(ref _selectedSlot, value);
    }

    public CharacterRow? SelectedCharacter
    {
        get => _selectedCharacter;
        set => Set(ref _selectedCharacter, value);
    }

    public string Overview
    {
        get => _overview;
        private set => Set(ref _overview, value);
    }

    public string Log
    {
        get => _log;
        private set => Set(ref _log, value);
    }

    public void LoadSlots(SaveDocument document)
    {
        Slots.Clear();
        foreach (var reference in document.ListSlots()) Slots.Add(new(reference));
        SelectedSlot = Slots.FirstOrDefault();
    }

    public void LoadSlot(SlotReference reference, IEditableSlot slot)
    {
        Characters.Clear();
        foreach (var character in slot.Characters()) Characters.Add(new(character));
        SelectedCharacter = Characters.FirstOrDefault(x => x.IsRecruited) ?? Characters.FirstOrDefault();

        Items.Clear();
        foreach (var item in slot.Items()) Items.Add(item);
        Cards.Clear();
        foreach (var card in slot.Cards()) Cards.Add(card);

        var party = slot.PartyMemberIds is null
            ? string.Empty
            : $" · party: {string.Join(", ", slot.PartyMemberIds.Select(id => slot.Character(id).Name))}";
        var location = string.IsNullOrWhiteSpace(slot.Location) ? string.Empty : $" · {slot.Location}";
        Overview = $"{reference.Label} · {SaveDocument.FormatLabel(slot.Format)} · {slot.LeaderName} · " +
                   $"{slot.Gil:N0} gil · {slot.PlaytimeSeconds / 3600:F1} hours{location}{party}\n" +
                   $"Inventory: {Items.Count} entries · Cards: {Cards.Count} · " +
                   $"Record: {slot.CardRecord.Wins}W / {slot.CardRecord.Losses}L / {slot.CardRecord.Draws}D";
    }

    public void LoadAbilities(IEditableCharacter? character)
    {
        Abilities.Clear();
        if (character?.Format != SaveFormat.Legacy) return;
        var enabled = character.SupportAbilities().ToHashSet();
        for (var index = 0; index < GameData.SupportAbilityNames.Count; index++)
            Abilities.Add(new(index, GameData.SupportAbilityNames[index], enabled.Contains(index)));
    }

    public void AppendLog(string message) => Log = string.IsNullOrEmpty(Log) ? message : $"{Log}{Environment.NewLine}{message}";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}

public sealed record SlotChoice(SlotReference Reference)
{
    public string Label => $"{Reference.Label} — {Reference.Summary}";
}

public sealed class CharacterRow
{
    public CharacterRow(IEditableCharacter character)
    {
        Index = character.Index;
        Name = character.Name;
        IsRecruited = character.IsRecruited;
        Level = Value(character, "level");
        Experience = Value(character, "exp");
        Hp = Pair(character, "cur_hp", "max_hp");
        Mp = Pair(character, "cur_mp", "max_mp");
        Strength = Value(character, "strength");
        Speed = Value(character, "speed");
        Magic = Value(character, "magic");
        Spirit = Value(character, "spirit");
    }

    public int Index { get; }
    public string Name { get; }
    public bool IsRecruited { get; }
    public int Level { get; }
    public int Experience { get; }
    public string Hp { get; }
    public string Mp { get; }
    public int Strength { get; }
    public int Speed { get; }
    public int Magic { get; }
    public int Spirit { get; }

    private static int Value(IEditableCharacter character, string field)
    {
        var actual = character.Has(field) ? field : field + "_base";
        return character.Has(actual) ? character.Get(actual) : 0;
    }

    private static string Pair(IEditableCharacter character, string current, string maximum) =>
        $"{Value(character, current)} / {Value(character, maximum)}";
}

public sealed class AbilityChoice
{
    public AbilityChoice(int index, string name, bool isEnabled) { Index = index; Name = name; IsEnabled = isEnabled; }
    public int Index { get; }
    public string Name { get; }
    public bool IsEnabled { get; set; }
    public string Label => $"{Index}: {Name}";
}
