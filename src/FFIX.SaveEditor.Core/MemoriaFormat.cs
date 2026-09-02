// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.Text;

namespace FFIX.SaveEditor.Core;

public enum MemoriaValueKind { Array = 1, Dictionary = 2, String = 3, Int32 = 4, Double = 5 }

public sealed class MemoriaValue
{
    private MemoriaValue(MemoriaValueKind kind) => Kind = kind;
    public MemoriaValueKind Kind { get; }
    public List<MemoriaValue> ArrayItems { get; private init; } = [];
    public List<KeyValuePair<string, MemoriaValue>> DictionaryItems { get; private init; } = [];
    public string StringValue { get; set; } = string.Empty;
    public int Int32Value { get; set; }
    public double DoubleValue { get; set; }

    public static MemoriaValue Array(IEnumerable<MemoriaValue>? values = null) =>
        new(MemoriaValueKind.Array) { ArrayItems = values?.ToList() ?? [] };
    public static MemoriaValue Dictionary(IEnumerable<KeyValuePair<string, MemoriaValue>>? values = null) =>
        new(MemoriaValueKind.Dictionary) { DictionaryItems = values?.ToList() ?? [] };
    public static MemoriaValue String(string value) => new(MemoriaValueKind.String) { StringValue = value };
    public static MemoriaValue Int32(int value) => new(MemoriaValueKind.Int32) { Int32Value = value };
    public static MemoriaValue Double(double value) => new(MemoriaValueKind.Double) { DoubleValue = value };

    public MemoriaValue? Get(string key) => Kind != MemoriaValueKind.Dictionary
        ? null
        : DictionaryItems.FirstOrDefault(x => x.Key == key).Value;

    public MemoriaValue Require(string key) => Get(key) ?? throw new SaveFormatException($"Memoria save is missing required key '{key}'.");

    public void Set(string key, MemoriaValue value)
    {
        if (Kind != MemoriaValueKind.Dictionary) throw new InvalidOperationException("Value is not a dictionary.");
        var index = DictionaryItems.FindIndex(x => x.Key == key);
        if (index >= 0) DictionaryItems[index] = new(key, value);
        else DictionaryItems.Add(new(key, value));
    }

    public int AsInt(int fallback = 0) => Kind switch
    {
        MemoriaValueKind.Int32 => Int32Value,
        MemoriaValueKind.Double => (int)DoubleValue,
        _ => fallback,
    };

    public double AsDouble(double fallback = 0) => Kind switch
    {
        MemoriaValueKind.Double => DoubleValue,
        MemoriaValueKind.Int32 => Int32Value,
        _ => fallback,
    };

    public void SetNumberPreservingKind(int value)
    {
        if (Kind == MemoriaValueKind.Double) DoubleValue = value;
        else if (Kind == MemoriaValueKind.Int32) Int32Value = value;
        else throw new InvalidOperationException("Memoria value is not numeric.");
    }

    public MemoriaValue Clone() => Kind switch
    {
        MemoriaValueKind.Array => Array(ArrayItems.Select(x => x.Clone())),
        MemoriaValueKind.Dictionary => Dictionary(DictionaryItems.Select(x => new KeyValuePair<string, MemoriaValue>(x.Key, x.Value.Clone()))),
        MemoriaValueKind.String => String(StringValue),
        MemoriaValueKind.Int32 => Int32(Int32Value),
        MemoriaValueKind.Double => Double(DoubleValue),
        _ => throw new InvalidOperationException(),
    };
}

public static class MemoriaCodec
{
    public static MemoriaValue Parse(ReadOnlySpan<byte> data)
    {
        var parsed = ParsePreservingTrailing(data);
        if (parsed.TrailingBytes.Length != 0)
            throw new SaveFormatException($"Parsed {data.Length - parsed.TrailingBytes.Length} of {data.Length} bytes; trailing data remains.");
        return parsed.Root;
    }

    internal static (MemoriaValue Root, byte[] TrailingBytes) ParsePreservingTrailing(ReadOnlySpan<byte> data)
    {
        var reader = new Reader(data.ToArray());
        var root = reader.ReadValue();
        if (root.Kind != MemoriaValueKind.Dictionary)
            throw new SaveFormatException("Memoria top-level value is not a dictionary.");
        return (root, data[reader.Position..].ToArray());
    }

    public static byte[] Serialize(MemoriaValue root)
    {
        using var stream = new MemoryStream();
        WriteValue(stream, root);
        return stream.ToArray();
    }

    public static bool LooksLikeSave(ReadOnlySpan<byte> data)
    {
        try
        {
            var root = ParsePreservingTrailing(data).Root;
            return root.Get("40000_Common")?.Get("players")?.Kind == MemoriaValueKind.Array;
        }
        catch (Exception exception) when (exception is SaveFormatException or DecoderFallbackException)
        {
            return false;
        }
    }

    private static void WriteValue(Stream stream, MemoriaValue value)
    {
        WriteInt32(stream, (int)value.Kind);
        switch (value.Kind)
        {
            case MemoriaValueKind.Array:
                WriteInt32(stream, value.ArrayItems.Count);
                foreach (var item in value.ArrayItems) WriteValue(stream, item);
                break;
            case MemoriaValueKind.Dictionary:
                WriteInt32(stream, value.DictionaryItems.Count);
                foreach (var pair in value.DictionaryItems)
                {
                    WriteString(stream, pair.Key);
                    WriteValue(stream, pair.Value);
                }
                break;
            case MemoriaValueKind.String:
                WriteString(stream, value.StringValue);
                break;
            case MemoriaValueKind.Int32:
                WriteInt32(stream, value.Int32Value);
                break;
            case MemoriaValueKind.Double:
                Span<byte> doubleBytes = stackalloc byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(doubleBytes, BitConverter.DoubleToInt64Bits(value.DoubleValue));
                stream.Write(doubleBytes);
                break;
            default:
                throw new SaveFormatException($"Unknown Memoria value kind {(int)value.Kind}.");
        }
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Write7BitLength(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void Write7BitLength(Stream stream, int length)
    {
        var value = (uint)length;
        do
        {
            var next = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) next |= 0x80;
            stream.WriteByte(next);
        } while (value != 0);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private sealed class Reader
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly byte[] _data;
        public Reader(byte[] data) => _data = data;
        public int Position { get; private set; }

        public MemoriaValue ReadValue()
        {
            var tagOffset = Position;
            var tag = ReadInt32();
            return tag switch
            {
                1 => ReadArray(),
                2 => ReadDictionary(),
                3 => MemoriaValue.String(ReadString()),
                4 => MemoriaValue.Int32(ReadInt32()),
                5 => MemoriaValue.Double(BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(Take(8)))),
                _ => throw new SaveFormatException($"Unknown Memoria type tag {tag} at offset {tagOffset}.")
            };
        }

        private MemoriaValue ReadArray()
        {
            var offset = Position;
            var count = ReadInt32();
            if (count < 0 || count > (_data.Length - Position) / 4)
                throw new SaveFormatException($"Invalid Memoria array count {count} at offset {offset}.");
            var values = new List<MemoriaValue>(count);
            for (var index = 0; index < count; index++) values.Add(ReadValue());
            return MemoriaValue.Array(values);
        }

        private MemoriaValue ReadDictionary()
        {
            var offset = Position;
            var count = ReadInt32();
            if (count < 0 || count > (_data.Length - Position) / 5)
                throw new SaveFormatException($"Invalid Memoria dictionary count {count} at offset {offset}.");
            var values = new List<KeyValuePair<string, MemoriaValue>>(count);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < count; index++)
            {
                var key = ReadString();
                if (!keys.Add(key)) throw new SaveFormatException($"Duplicate Memoria dictionary key '{key}'.");
                values.Add(new(key, ReadValue()));
            }
            return MemoriaValue.Dictionary(values);
        }

        private int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

        private string ReadString()
        {
            var length = Read7BitLength();
            return StrictUtf8.GetString(Take(length));
        }

        private int Read7BitLength()
        {
            uint result = 0;
            for (var shift = 0; shift < 35; shift += 7)
            {
                var value = Take(1)[0];
                result |= (uint)(value & 0x7F) << shift;
                if ((value & 0x80) == 0)
                {
                    if (result > int.MaxValue)
                        throw new SaveFormatException($"Memoria 7-bit length is too large at offset {Position}.");
                    return (int)result;
                }
            }
            throw new SaveFormatException($"Invalid Memoria 7-bit length at offset {Position}.");
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || Position > _data.Length - count)
                throw new SaveFormatException($"Unexpected end of Memoria data at offset {Position}.");
            var result = _data.AsSpan(Position, count);
            Position += count;
            return result;
        }
    }
}

public sealed class MemoriaCharacter : IEditableCharacter
{
    private static IReadOnlyDictionary<string, string[]> Paths { get; } = new Dictionary<string, string[]>
    {
        ["level"] = ["level"], ["exp"] = ["exp"], ["cur_hp"] = ["cur", "hp"], ["max_hp"] = ["max", "hp"],
        ["cur_mp"] = ["cur", "mp"], ["max_mp"] = ["max", "mp"], ["strength"] = ["elem", "str"],
        ["speed"] = ["elem", "dex"], ["magic"] = ["elem", "mgc"], ["spirit"] = ["elem", "wpr"],
        ["trance"] = ["trance"],
    };
    private static IReadOnlyDictionary<string, int> EquipmentIndices { get; } =
        new Dictionary<string, int> { ["weapon"] = 0, ["head"] = 1, ["arm"] = 2, ["armor"] = 3, ["accessory"] = 4 };
    private readonly MemoriaValue _record;

    internal MemoriaCharacter(MemoriaValue record, int index) { _record = record; Index = index; }
    public SaveFormat Format => SaveFormat.Memoria;
    public int Index { get; }
    public string Name
    {
        get => _record.Get("name")?.StringValue ?? string.Empty;
        set => _record.Set("name", MemoriaValue.String(value));
    }
    public bool IsRecruited => Get("level") > 0;
    public bool Has(string fieldName) => Paths.ContainsKey(fieldName) || EquipmentIndices.ContainsKey(fieldName);

    public int Get(string fieldName)
    {
        if (EquipmentIndices.TryGetValue(fieldName, out var equipmentIndex))
        {
            var equipment = _record.Get("equip");
            return equipment?.Kind == MemoriaValueKind.Array && equipmentIndex < equipment.ArrayItems.Count
                ? equipment.ArrayItems[equipmentIndex].AsInt(0xFF) : 0xFF;
        }
        return Walk(Paths[fieldName]).AsInt();
    }

    public void Set(string fieldName, int value)
    {
        if (EquipmentIndices.TryGetValue(fieldName, out var equipmentIndex))
        {
            var equipment = _record.Get("equip");
            if (equipment?.Kind != MemoriaValueKind.Array)
            {
                equipment = MemoriaValue.Array(Enumerable.Range(0, 5).Select(_ => MemoriaValue.Int32(0xFF)));
                _record.Set("equip", equipment);
            }
            while (equipment.ArrayItems.Count <= equipmentIndex) equipment.ArrayItems.Add(MemoriaValue.Int32(0xFF));
            equipment.ArrayItems[equipmentIndex].SetNumberPreservingKind(value & 0xFF);
            return;
        }
        Walk(Paths[fieldName]).SetNumberPreservingKind(value);
    }

    public int MaximumFor(string fieldName) => fieldName switch
    {
        "level" => 99, "exp" => 9_999_999, "cur_hp" or "max_hp" => 9_999,
        "cur_mp" or "max_mp" => 999, "strength" or "speed" or "magic" or "spirit" => 99,
        "trance" => 100, _ => int.MaxValue,
    };

    public IReadOnlyDictionary<string, byte> Equipment() => EquipmentIndices.Keys.ToDictionary(x => x, x => (byte)Get(x));
    public IReadOnlyList<int> SupportAbilities() => [];
    public void SetSupportAbility(int bitIndex, bool enabled) =>
        throw new InvalidOperationException("Support-ability editing is not available for Memoria mod saves.");

    public void MaxOut()
    {
        foreach (var field in new[] { "level", "exp", "cur_hp", "max_hp", "cur_mp", "max_mp", "strength", "speed", "magic", "spirit", "trance" })
            Set(field, MaximumFor(field));
        var basis = _record.Get("basis");
        if (basis?.Kind != MemoriaValueKind.Dictionary) return;
        foreach (var pair in new[] { ("max_hp", "max_hp"), ("max_mp", "max_mp"), ("dex", "speed"),
                     ("str", "strength"), ("mgc", "magic"), ("wpr", "spirit") })
            basis.Get(pair.Item1)?.SetNumberPreservingKind(Get(pair.Item2));
    }

    private MemoriaValue Walk(IReadOnlyList<string> path)
    {
        var current = _record;
        foreach (var key in path) current = current.Require(key);
        return current;
    }
}

public sealed class MemoriaSlot : IEditableSlot
{
    private readonly MemoriaValue _root;
    public MemoriaSlot(MemoriaValue root) => _root = root;
    public SaveFormat Format => SaveFormat.Memoria;
    internal MemoriaValue Root => _root;
    private MemoriaValue Common => _root.Require("40000_Common");
    private MemoriaValue Players => Common.Require("players");

    public IReadOnlyList<IEditableCharacter> Characters() => Players.ArrayItems.Select((x, i) => (IEditableCharacter)new MemoriaCharacter(x, i)).ToArray();
    public IEditableCharacter Character(int index)
    {
        if ((uint)index >= Players.ArrayItems.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return new MemoriaCharacter(Players.ArrayItems[index], index);
    }

    public IReadOnlyList<InventoryItem> Items()
    {
        var items = Common.Get("items");
        if (items?.Kind != MemoriaValueKind.Array) return [];
        return items.ArrayItems.Select((entry, index) => new { Entry = entry, Index = index })
            .Where(x => x.Entry.Get("count")?.AsInt() > 0)
            .Select(x => new InventoryItem(x.Entry.Require("id").AsInt(), x.Entry.Require("count").AsInt(), x.Index)).ToArray();
    }

    public bool SetItem(int itemId, int count)
    {
        if (itemId < 0)
            throw new ArgumentOutOfRangeException(nameof(itemId), "Memoria item IDs cannot be negative.");
        var bounded = Math.Clamp(count, 0, 99);
        var items = Common.Get("items");
        if (items?.Kind != MemoriaValueKind.Array)
        {
            items = MemoriaValue.Array();
            Common.Set("items", items);
        }
        var existing = items.ArrayItems.FindIndex(x => x.Get("id")?.AsInt() == itemId);
        if (existing >= 0)
        {
            if (bounded == 0) items.ArrayItems.RemoveAt(existing);
            else
            {
                var countValue = items.ArrayItems[existing].Require("count");
                countValue.SetNumberPreservingKind(Math.Max(countValue.AsInt(), bounded));
            }
            return true;
        }
        if (bounded > 0)
            items.ArrayItems.Add(MemoriaValue.Dictionary([
                new("id", MemoriaValue.Int32(itemId)), new("count", MemoriaValue.Int32(bounded))]));
        return true;
    }

    public bool ReplaceItemAt(int slotIndex, int itemId, int count)
    {
        var items = Common.Get("items");
        if (items?.Kind != MemoriaValueKind.Array)
        {
            items = MemoriaValue.Array();
            Common.Set("items", items);
        }
        var bounded = Math.Clamp(count, 0, 99);
        if (bounded == 0)
        {
            if ((uint)slotIndex < items.ArrayItems.Count)
                items.ArrayItems.RemoveAt(slotIndex);
            return true;
        }
        if (itemId < 0)
            throw new ArgumentOutOfRangeException(nameof(itemId), "Memoria item IDs cannot be negative.");
        if (slotIndex == items.ArrayItems.Count)
        {
            items.ArrayItems.Add(MemoriaValue.Dictionary([
                new("id", MemoriaValue.Int32(itemId)), new("count", MemoriaValue.Int32(bounded))]));
            return true;
        }
        if ((uint)slotIndex >= items.ArrayItems.Count)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        var entry = items.ArrayItems[slotIndex];
        if (entry.Kind != MemoriaValueKind.Dictionary)
        {
            items.ArrayItems[slotIndex] = MemoriaValue.Dictionary([
                new("id", MemoriaValue.Int32(itemId)), new("count", MemoriaValue.Int32(bounded))]);
            return true;
        }
        SetNumber(entry, "id", itemId);
        SetNumber(entry, "count", bounded);
        return true;
    }

    public void RemoveItem(int itemId) => SetItem(itemId, 0);

    public int Gil
    {
        get => Common.Get("gil")?.AsInt() ?? 0;
        set => Common.Require("gil").SetNumberPreservingKind(Math.Clamp(value, 0, SaveLayout.MaximumGil));
    }

    public string LeaderName => Characters().FirstOrDefault(x => x.IsRecruited && !string.IsNullOrEmpty(x.Name))?.Name ?? string.Empty;
    public string? Location => null;
    public double PlaytimeSeconds => _root.Get("95000_Setting")?.Get("00001_time")?.AsDouble() ?? 0;
    public IReadOnlyList<int>? PartyMemberIds => null;

    public IReadOnlyList<CardInfo> Cards()
    {
        var cards = MiniGameCards;
        if (cards is null) return [];
        return cards.ArrayItems.Select((entry, index) => new CardInfo(
                index,
                (byte)(entry.Get("id")?.AsInt(GameData.NoCardType) ?? GameData.NoCardType),
                (byte)(entry.Get("arrow")?.AsInt() ?? 0),
                (byte)(entry.Get("atk")?.AsInt() ?? 0),
                (byte)(entry.Get("type")?.AsInt() ?? 0),
                (byte)(entry.Get("pdef")?.AsInt() ?? 0),
                (byte)(entry.Get("mdef")?.AsInt() ?? 0)))
            .Where(card => card.TypeId < GameData.CardTypeNames.Count)
            .ToArray();
    }

    public int CardSlotCount => MiniGameCards?.ArrayItems.Count ?? 0;

    public void SetCard(int index, byte typeId, byte arrows, byte attack, byte attackType, byte physicalDefense, byte magicDefense)
    {
        if (typeId >= GameData.CardTypeNames.Count)
        {
            var existing = MiniGameCards;
            if (existing is not null && (uint)index < existing.ArrayItems.Count)
                existing.ArrayItems.RemoveAt(index);
            return;
        }

        var cards = EnsureMiniGameCards();
        if (index < 0 || index > cards.ArrayItems.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var boundedType = (byte)(attackType & 0x03);
        if (index == cards.ArrayItems.Count)
        {
            cards.ArrayItems.Add(NewCard(typeId, boundedType, attack, physicalDefense, magicDefense, arrows));
            return;
        }

        var entry = cards.ArrayItems[index];
        if (entry.Kind != MemoriaValueKind.Dictionary)
        {
            cards.ArrayItems[index] = NewCard(typeId, boundedType, attack, physicalDefense, magicDefense, arrows);
            return;
        }

        SetNumber(entry, "id", typeId);
        SetNumber(entry, "type", boundedType);
        SetNumber(entry, "atk", attack);
        SetNumber(entry, "pdef", physicalDefense);
        SetNumber(entry, "mdef", magicDefense);
        SetNumber(entry, "arrow", arrows);
        if (entry.Get("side") is null)
            entry.Set("side", MemoriaValue.Int32(0));
        if (entry.Get("cpoint") is null)
            entry.Set("cpoint", MemoriaValue.Int32(10));
    }

    public void SetCardRecord(int wins, int losses, int draws)
    {
        var mini = _root.Get("30000_MiniGame");
        if (mini?.Kind != MemoriaValueKind.Dictionary)
        {
            mini = MemoriaValue.Dictionary();
            _root.Set("30000_MiniGame", mini);
        }
        SetNumber(mini, "sWin", ClampCardRecord(wins));
        SetNumber(mini, "sLose", ClampCardRecord(losses));
        SetNumber(mini, "sDraw", ClampCardRecord(draws));
    }

    public (int Wins, int Losses, int Draws) CardRecord
    {
        get
        {
            var mini = _root.Get("30000_MiniGame");
            return (mini?.Get("sWin")?.AsInt() ?? 0, mini?.Get("sLose")?.AsInt() ?? 0, mini?.Get("sDraw")?.AsInt() ?? 0);
        }
    }

    private MemoriaValue? MiniGameCards
    {
        get
        {
            var cards = _root.Get("30000_MiniGame")?.Get("MiniGameCard");
            return cards?.Kind == MemoriaValueKind.Array ? cards : null;
        }
    }

    private MemoriaValue EnsureMiniGameCards()
    {
        var mini = _root.Get("30000_MiniGame");
        if (mini?.Kind != MemoriaValueKind.Dictionary)
        {
            mini = MemoriaValue.Dictionary();
            _root.Set("30000_MiniGame", mini);
        }
        var cards = mini.Get("MiniGameCard");
        if (cards?.Kind != MemoriaValueKind.Array)
        {
            cards = MemoriaValue.Array();
            mini.Set("MiniGameCard", cards);
        }
        return cards;
    }

    private static int ClampCardRecord(int value) => Math.Clamp(value, 0, ushort.MaxValue);

    private static MemoriaValue NewCard(byte typeId, byte attackType, byte attack, byte physicalDefense, byte magicDefense, byte arrows) =>
        MemoriaValue.Dictionary([
            new("id", MemoriaValue.Int32(typeId)),
            new("type", MemoriaValue.Int32(attackType)),
            new("atk", MemoriaValue.Int32(attack)),
            new("pdef", MemoriaValue.Int32(physicalDefense)),
            new("mdef", MemoriaValue.Int32(magicDefense)),
            new("arrow", MemoriaValue.Int32(arrows)),
            new("side", MemoriaValue.Int32(0)),
            new("cpoint", MemoriaValue.Int32(10)),
        ]);

    private static void SetNumber(MemoriaValue dictionary, string key, int value)
    {
        var existing = dictionary.Get(key);
        if (existing is not null && existing.Kind is MemoriaValueKind.Int32 or MemoriaValueKind.Double)
            existing.SetNumberPreservingKind(value);
        else
            dictionary.Set(key, MemoriaValue.Int32(value));
    }

    public IEditableSlot Clone() => new MemoriaSlot(_root.Clone());
    public void FinalizeEdits() { }
}
