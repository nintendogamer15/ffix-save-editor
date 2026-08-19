// SPDX-License-Identifier: MIT
namespace FFIX.SaveEditor.Core;

public enum SaveFormat
{
    Legacy,
    Rr2016,
    Memoria,
}

public readonly record struct FieldSpec(int Offset, int Size)
{
    public int Maximum => Size switch { 1 => byte.MaxValue, 2 => ushort.MaxValue, 3 => 0xFF_FFFF, _ => int.MaxValue };
}

public sealed record SlotReference(
    SaveFormat Format,
    string Label,
    string Summary,
    int? BlockIndex = null,
    int? SlotId = null,
    int? SaveId = null,
    int? LegacyFrameRate = null);

// Memoria deliberately permits mod-defined regular-item IDs outside the vanilla
// byte range. Keep the shared model wide enough to represent those IDs exactly;
// the binary save implementations enforce their own 0-255 storage limit.
public sealed record InventoryItem(int ItemId, int Quantity, int SlotIndex)
{
    public string Name => GameData.ItemName(ItemId);
}

public sealed record CardInfo(
    int Index,
    byte TypeId,
    byte Arrows,
    byte Attack,
    byte AttackType,
    byte PhysicalDefense,
    byte MagicDefense)
{
    public string TypeName => GameData.CardTypeName(TypeId);
    public char AttackTypeName => "PMXA"[AttackType % 4];
}

public interface IEditableCharacter
{
    SaveFormat Format { get; }
    int Index { get; }
    string Name { get; set; }
    bool IsRecruited { get; }
    bool Has(string fieldName);
    int Get(string fieldName);
    void Set(string fieldName, int value);
    int MaximumFor(string fieldName);
    IReadOnlyDictionary<string, byte> Equipment();
    IReadOnlyList<int> SupportAbilities();
    void SetSupportAbility(int bitIndex, bool enabled);
    void MaxOut();
}

public interface IEditableSlot
{
    SaveFormat Format { get; }
    int Gil { get; set; }
    string LeaderName { get; }
    string? Location { get; }
    double PlaytimeSeconds { get; }
    IReadOnlyList<int>? PartyMemberIds { get; }
    IReadOnlyList<IEditableCharacter> Characters();
    IEditableCharacter Character(int index);
    IReadOnlyList<InventoryItem> Items();
    bool SetItem(int itemId, int count);
    void RemoveItem(int itemId);
    IReadOnlyList<CardInfo> Cards();
    (int Wins, int Losses, int Draws) CardRecord { get; }
    IEditableSlot Clone();
    void FinalizeEdits();
}

public sealed class SaveFormatException : Exception
{
    public SaveFormatException(string message) : base(message) { }
    public SaveFormatException(string message, Exception inner) : base(message, inner) { }
}
