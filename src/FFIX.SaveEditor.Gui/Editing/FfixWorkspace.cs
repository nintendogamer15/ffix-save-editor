// SPDX-License-Identifier: MIT
using FFIX.SaveEditor.Core;
using SaveEditor.Ui.Editing;
using SaveEditor.Ui.Workflow;

namespace FFIX.SaveEditor.Gui.Editing;

/// <summary>
/// Testable command seam for every FFIX edit. All commands mutate a cloned document and adopt
/// it only after Core accepts CommitSlot, so a rejection cannot alter the live bytes or history.
/// </summary>
public sealed class FfixWorkspace
{
    private readonly DocumentSession<SaveDocument> session;
    private readonly IEditHistory history;
    private SlotKey? selectedSlot;

    public FfixWorkspace(DocumentSession<SaveDocument> session, IEditHistory history)
    {
        this.session = session;
        this.history = history;
    }

    public event EventHandler? Changed;

    public SaveDocument? Document => session.Document;
    public IReadOnlyList<SlotReference> Slots { get; private set; } = [];
    public int SelectedSlotIndex { get; private set; } = -1;
    public int SelectedCharacterIndex { get; private set; } = -1;
    public bool HasPendingEdits { get; private set; }

    public IEditableSlot? CurrentSlot => ResolveCurrentSlot(Document);
    public IEditableCharacter? CurrentCharacter => CurrentSlot is { } slot &&
        (uint)SelectedCharacterIndex < slot.Characters().Count
            ? slot.Character(SelectedCharacterIndex)
            : null;

    public bool CanEditSupportAbilities => CurrentCharacter?.Format == SaveFormat.Legacy;

    public string Overview
    {
        get
        {
            if (SelectedSlotIndex < 0 || CurrentSlot is not { } slot)
            {
                return "No save loaded.";
            }

            SlotReference reference = Slots[SelectedSlotIndex];
            string party = slot.PartyMemberIds is null
                ? string.Empty
                : $" · party: {string.Join(", ", slot.PartyMemberIds.Select(id => slot.Character(id).Name))}";
            string location = string.IsNullOrWhiteSpace(slot.Location) ? string.Empty : $" · {slot.Location}";
            return $"{reference.Label} · {SaveDocument.FormatLabel(slot.Format)} · {slot.LeaderName} · " +
                   $"{slot.Gil:N0} gil · {slot.PlaytimeSeconds / 3600:F1} hours{location}{party}\n" +
                   $"Inventory: {slot.Items().Count} entries · Cards: {slot.Cards().Count} · " +
                   $"Record: {slot.CardRecord.Wins}W / {slot.CardRecord.Losses}L / {slot.CardRecord.Draws}D";
        }
    }

    public void BindDocument()
    {
        SlotKey? previous = selectedSlot;
        int previousCharacter = SelectedCharacterIndex;
        Slots = Document?.ListSlots() ?? [];
        SelectedSlotIndex = previous is null ? (Slots.Count > 0 ? 0 : -1) : FindSlot(Slots, previous.Value);
        if (SelectedSlotIndex < 0 && Slots.Count > 0) SelectedSlotIndex = 0;
        selectedSlot = SelectedSlotIndex >= 0 ? SlotKey.From(Slots[SelectedSlotIndex]) : null;

        IReadOnlyList<IEditableCharacter> characters = CurrentSlot?.Characters() ?? [];
        SelectedCharacterIndex = (uint)previousCharacter < characters.Count
            ? previousCharacter
            : characters.FirstOrDefault(character => character.IsRecruited)?.Index ??
              (characters.Count > 0 ? characters[0].Index : -1);
        HasPendingEdits = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SelectSlot(int index)
    {
        if ((uint)index >= Slots.Count) throw new ArgumentOutOfRangeException(nameof(index));
        SelectedSlotIndex = index;
        selectedSlot = SlotKey.From(Slots[index]);
        IReadOnlyList<IEditableCharacter> characters = CurrentSlot?.Characters() ?? [];
        SelectedCharacterIndex = characters.FirstOrDefault(character => character.IsRecruited)?.Index ??
                                 (characters.Count > 0 ? characters[0].Index : -1);
        HasPendingEdits = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SelectCharacter(int index)
    {
        IReadOnlyList<IEditableCharacter> characters = CurrentSlot?.Characters() ?? [];
        if ((uint)index >= characters.Count) throw new ArgumentOutOfRangeException(nameof(index));
        SelectedCharacterIndex = index;
    }

    public void SetPendingEdits(bool value)
    {
        if (HasPendingEdits == value) return;
        HasPendingEdits = value;
    }

    public EditResult SetGil(string text) => EditSlot(
        slot => slot.Gil = ParseNumber(text, "gil", SaveLayout.MaximumGil),
        "Updated gil.");

    public EditResult ApplyCharacter(CharacterDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        int index = SelectedCharacterIndex;
        if (index < 0) return EditResult.Rejected("Select a character first.");

        return EditSlot(slot =>
        {
            IEditableCharacter character = slot.Character(index);
            character.Name = draft.Name;
            foreach ((string field, string value) in draft.Numbers)
            {
                string? actual = ActualField(character, field);
                if (actual is not null)
                    character.Set(actual, ParseNumber(value, field, character.MaximumFor(actual)));
            }

            foreach ((string field, string value) in draft.Equipment)
            {
                if (character.Has(field) && !string.IsNullOrWhiteSpace(value))
                    character.Set(field, GameData.ResolveItemId(value));
            }
        }, $"Updated character row {index}.");
    }

    public EditResult ApplySupportAbilities(IReadOnlyCollection<int> enabled)
    {
        ArgumentNullException.ThrowIfNull(enabled);
        int index = SelectedCharacterIndex;
        if (index < 0) return EditResult.Rejected("Select a character first.");
        if (!CanEditSupportAbilities)
            return EditResult.Rejected("Support-ability editing is only available for PS1 saves.");

        HashSet<int> chosen = enabled.ToHashSet();
        return EditSlot(slot =>
        {
            IEditableCharacter character = slot.Character(index);
            if (character.Format != SaveFormat.Legacy)
                throw new InvalidOperationException("Support-ability editing is only available for PS1 saves.");
            for (int ability = 0; ability < GameData.SupportAbilityNames.Count; ability++)
                character.SetSupportAbility(ability, chosen.Contains(ability));
        }, $"Updated support abilities for character row {index}.");
    }

    public EditResult MaxSelected()
    {
        int index = SelectedCharacterIndex;
        return index < 0
            ? EditResult.Rejected("Select a character first.")
            : EditSlot(slot => slot.Character(index).MaxOut(), $"Maxed character row {index}.");
    }

    public EditResult MaxAll() => EditSlot(slot =>
    {
        foreach (IEditableCharacter character in slot.Characters().Where(character => character.IsRecruited))
            character.MaxOut();
    }, "Maxed every recruited character.");

    public EditResult GiveAllItems(int quantity) => EditSlot(slot =>
    {
        int added = SaveDocument.GiveAllItems(slot, ValidateQuantity(quantity));
        if (added == 0)
            throw new InvalidOperationException("No item entries could be added; the inventory may be full.");
    }, $"Added all known items and gear at quantity {quantity} where space allowed.");

    public EditResult AddItem(string token, int quantity)
    {
        if (string.IsNullOrWhiteSpace(token))
            return EditResult.Rejected("Enter or pick an item/gear name first.");

        return EditSlot(slot =>
        {
            int itemId = GameData.ResolveItemId(token.Trim());
            if (!slot.SetItem(itemId, ValidateQuantity(quantity)))
                throw new InvalidOperationException("Inventory is full.");
        }, $"Added {token.Trim()} at quantity {quantity}.");
    }

    public EditResult ApplyInventory(IReadOnlyList<InventoryRowDraft> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach (InventoryRowDraft row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Token))
                return EditResult.Rejected("Enter or pick an item/gear name first.");
            if (row.Quantity is < 0 or > 99)
                return EditResult.Rejected("quantity must be a whole number from 0 to 99.");
        }

        return EditSlot(slot =>
        {
            foreach (InventoryRowDraft row in rows.OrderByDescending(entry => entry.SlotIndex))
            {
                int itemId = GameData.ResolveItemId(row.Token.Trim());
                if (!slot.ReplaceItemAt(row.SlotIndex, itemId, row.Quantity))
                    throw new InvalidOperationException("Inventory update failed.");
            }
        }, "Updated inventory.");
    }

    public EditResult ApplyCard(int index, int typeId, byte arrows, int attack, int attackType,
        int physicalDefense, int magicDefense)
    {
        if ((uint)typeId >= GameData.CardTypeNames.Count)
            return EditResult.Rejected("Pick a Tetra Master card type first.");
        if (attack is < 0 or > byte.MaxValue || physicalDefense is < 0 or > byte.MaxValue ||
            magicDefense is < 0 or > byte.MaxValue)
            return EditResult.Rejected("Card attack and defense must be whole numbers from 0 to 255.");
        if ((uint)attackType > 3)
            return EditResult.Rejected("Card class must be P, M, X, or A.");

        return EditSlot(
            slot => slot.SetCard(index, (byte)typeId, arrows, (byte)attack, (byte)attackType,
                (byte)physicalDefense, (byte)magicDefense),
            $"Updated card {index}.");
    }

    public EditResult SetCardRecord(int wins, int losses, int draws)
    {
        if (wins is < 0 or > ushort.MaxValue || losses is < 0 or > ushort.MaxValue ||
            draws is < 0 or > ushort.MaxValue)
            return EditResult.Rejected("Card record values must be whole numbers from 0 to 65,535.");

        return EditSlot(slot => slot.SetCardRecord(wins, losses, draws), "Updated Tetra Master record.");
    }

    public EditResult RemoveCard(int index) => EditSlot(
        slot => slot.SetCard(index, GameData.NoCardType, 0, 0, 0, 0, 0),
        $"Removed card {index}.");

    public EditResult AddCard() => EditSlot(slot =>
    {
        int index = FindEmptyCardIndex(slot);
        if (index < 0)
            throw new InvalidOperationException("No empty Tetra Master card slots remain.");
        slot.SetCard(index, 0, 0xFF, 255, 3, 255, 255);
    }, "Added a maxed Tetra Master card.");

    private EditResult EditSlot(Action<IEditableSlot> operation, string label)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Document is not { } live || selectedSlot is not { } slotKey)
            return EditResult.Rejected("Load a save and select a slot before editing.");

        byte[] before = live.ToArray();
        try
        {
            SaveDocument candidate = SaveDocument.Parse("edited-save.dat", before);
            SlotReference reference = ResolveReference(candidate, slotKey);
            IEditableSlot slot = candidate.LoadSlot(reference).Clone();
            operation(slot);
            candidate.CommitSlot(reference, slot);
            byte[] after = candidate.ToArray();

            if (before.AsSpan().SequenceEqual(after))
            {
                HasPendingEdits = false;
                Changed?.Invoke(this, EventArgs.Empty);
                return EditResult.Accepted(label);
            }

            session.ReplaceDocument(candidate);
            history.Record(new HistoryEntry(
                label,
                () => Restore(before, slotKey),
                () => Restore(after, slotKey)));
            selectedSlot = slotKey;
            BindDocument();
            return EditResult.Accepted(label + " The edited slot was finalized and reloaded successfully.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
                                           SaveFormatException or OverflowException)
        {
            return EditResult.Rejected($"Edit failed; no partial edit was applied: {exception.Message}");
        }
    }

    private void Restore(byte[] snapshot, SlotKey slotKey)
    {
        SaveDocument restored = SaveDocument.Parse("history-save.dat", snapshot);
        session.ReplaceDocument(restored);
        selectedSlot = slotKey;
        BindDocument();
    }

    private IEditableSlot? ResolveCurrentSlot(SaveDocument? document)
    {
        if (document is null || selectedSlot is null) return null;
        try { return document.LoadSlot(ResolveReference(document, selectedSlot.Value)); }
        catch (ArgumentException) { return null; }
    }

    private static SlotReference ResolveReference(SaveDocument document, SlotKey key)
    {
        return document.ListSlots().FirstOrDefault(reference => SlotKey.From(reference) == key)
            ?? throw new SaveFormatException("The selected save slot is no longer present.");
    }

    private static int FindSlot(IReadOnlyList<SlotReference> slots, SlotKey key)
    {
        for (int index = 0; index < slots.Count; index++)
            if (SlotKey.From(slots[index]) == key) return index;
        return -1;
    }

    private static int ParseNumber(string text, string label, int maximum)
    {
        if (!int.TryParse(text, out int value) || value < 0 || value > maximum)
            throw new ArgumentException($"{label} must be a whole number from 0 to {maximum:N0}.");
        return value;
    }

    private static int ValidateQuantity(int quantity) => quantity is >= 1 and <= 99
        ? quantity
        : throw new ArgumentException("quantity must be a whole number from 1 to 99.");

    private static int FindEmptyCardIndex(IEditableSlot slot)
    {
        HashSet<int> occupied = slot.Cards().Select(card => card.Index).ToHashSet();
        for (int index = 0; index < slot.CardSlotCount; index++)
        {
            if (!occupied.Contains(index))
                return index;
        }

        return slot.Format == SaveFormat.Memoria ? slot.CardSlotCount : -1;
    }

    private static string? ActualField(IEditableCharacter character, string requested)
    {
        if (character.Has(requested)) return requested;
        string basis = requested + "_base";
        return character.Has(basis) ? basis : null;
    }

    private readonly record struct SlotKey(SaveFormat Format, int? Block, int? Slot, int? Save)
    {
        public static SlotKey From(SlotReference reference) =>
            new(reference.Format, reference.BlockIndex, reference.SlotId, reference.SaveId);
    }
}

public sealed record CharacterDraft(
    string Name,
    IReadOnlyDictionary<string, string> Numbers,
    IReadOnlyDictionary<string, string> Equipment);

public sealed record InventoryRowDraft(int SlotIndex, string Token, int Quantity);

public sealed record EditResult(bool Success, string Message)
{
    public static EditResult Accepted(string message) => new(true, message);
    public static EditResult Rejected(string message) => new(false, message);
}
