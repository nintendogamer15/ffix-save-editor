// SPDX-License-Identifier: MIT
using System.Buffers.Binary;

namespace FFIX.SaveEditor.Core;

public sealed class BinaryCharacter : IEditableCharacter
{
    private readonly byte[] _data;
    private readonly int _baseOffset;
    private readonly IReadOnlyDictionary<string, FieldSpec> _fields;

    internal BinaryCharacter(byte[] data, int baseOffset, SaveFormat format, int index)
    {
        _data = data;
        _baseOffset = baseOffset;
        Format = format;
        Index = index;
        _fields = format == SaveFormat.Legacy ? SaveLayout.LegacyCharacterFields : SaveLayout.RrCharacterFields;
    }

    public SaveFormat Format { get; }
    public int Index { get; }

    public string Name
    {
        get
        {
            if (Format == SaveFormat.Legacy)
                return LegacyTextCodec.Decode(_data.AsSpan(_baseOffset, SaveLayout.LegacyCharacterNameLength));
            var bytes = _data.AsSpan(_baseOffset + SaveLayout.RrCharacterNameOffset, SaveLayout.RrCharacterNameLength);
            var terminator = bytes.IndexOf((byte)0);
            if (terminator >= 0) bytes = bytes[..terminator];
            return System.Text.Encoding.Latin1.GetString(bytes);
        }
        set
        {
            if (Format == SaveFormat.Legacy)
            {
                LegacyTextCodec.Encode(value, SaveLayout.LegacyCharacterNameLength)
                    .CopyTo(_data, _baseOffset);
                return;
            }
            var bytes = System.Text.Encoding.Latin1.GetBytes(value);
            var destination = _data.AsSpan(_baseOffset + SaveLayout.RrCharacterNameOffset, SaveLayout.RrCharacterNameLength);
            destination.Clear();
            bytes.AsSpan(0, Math.Min(bytes.Length, destination.Length)).CopyTo(destination);
        }
    }

    public bool IsRecruited => Get("level") > 0;
    public bool Has(string fieldName) => _fields.ContainsKey(fieldName);
    public int Get(string fieldName) => BinarySave.GetField(_data, _baseOffset, _fields[fieldName]);
    public void Set(string fieldName, int value) => BinarySave.SetField(_data, _baseOffset, _fields[fieldName], value);
    public int MaximumFor(string fieldName) => _fields[fieldName].Maximum;

    public IReadOnlyDictionary<string, byte> Equipment() => SaveLayout.EquipmentSlots
        .Where(Has).ToDictionary(x => x, x => (byte)Get(x));

    public IReadOnlyList<int> SupportAbilities()
    {
        if (Format != SaveFormat.Legacy)
            return [];
        var bitmap = BinaryPrimitives.ReadUInt64LittleEndian(
            _data.AsSpan(_baseOffset + SaveLayout.LegacySupportBitmapOffset, SaveLayout.LegacySupportBitmapLength));
        return Enumerable.Range(0, GameData.SupportAbilityNames.Count).Where(index => (bitmap & (1UL << index)) != 0).ToArray();
    }

    public void SetSupportAbility(int bitIndex, bool enabled)
    {
        if (Format != SaveFormat.Legacy)
            throw new InvalidOperationException("Support-ability editing is only available for legacy saves.");
        if ((uint)bitIndex >= GameData.SupportAbilityNames.Count)
            throw new ArgumentOutOfRangeException(nameof(bitIndex));
        var span = _data.AsSpan(_baseOffset + SaveLayout.LegacySupportBitmapOffset, SaveLayout.LegacySupportBitmapLength);
        var bitmap = BinaryPrimitives.ReadUInt64LittleEndian(span);
        bitmap = enabled ? bitmap | (1UL << bitIndex) : bitmap & ~(1UL << bitIndex);
        BinaryPrimitives.WriteUInt64LittleEndian(span, bitmap);
    }

    public void MaxOut()
    {
        Set("level", 99);
        Set("exp", 9_999_999);
        foreach (var field in new[] { "speed", "strength", "magic", "spirit", "speed_base", "strength_base", "magic_base", "spirit_base" })
            if (Has(field)) Set(field, 99);
        if (Has("max_hp")) Set("max_hp", 9_999);
        if (Has("max_hp_base")) Set("max_hp_base", 9_999);
        if (Has("max_hp_bonus")) Set("max_hp_bonus", 0);
        if (Has("max_mp")) Set("max_mp", 999);
        if (Has("max_mp_base")) Set("max_mp_base", 999);
        if (Has("max_mp_bonus")) Set("max_mp_bonus", 0);
        Set("cur_hp", 9_999);
        Set("cur_mp", 999);
        if (Has("cur_magic_stones") && Has("max_magic_stones"))
        {
            Set("max_magic_stones", MaximumFor("max_magic_stones"));
            Set("cur_magic_stones", Get("max_magic_stones"));
        }
        if (Has("trance")) Set("trance", MaximumFor("trance"));
    }
}

public sealed class BinarySlot : IEditableSlot
{
    private readonly byte[] _data;
    private readonly int _characterStart;
    private readonly int _characterSize;
    private readonly int _characterCount;
    private readonly int _itemStart;
    private readonly int _itemCount;
    private readonly int _cardStart;
    private readonly int _cardRecordSize;
    private readonly int _cardCount;
    private readonly IReadOnlyList<int> _cardLayout;
    private readonly int _legacyFrameRate;

    public BinarySlot(byte[] data, SaveFormat format, int legacyFrameRate = 60)
    {
        if (format is not (SaveFormat.Legacy or SaveFormat.Rr2016))
            throw new ArgumentOutOfRangeException(nameof(format));
        _data = data;
        Format = format;
        _legacyFrameRate = legacyFrameRate;
        if (format == SaveFormat.Legacy)
        {
            if (data.Length != SaveLayout.LegacyBlockSize) throw new SaveFormatException("Legacy save block must be exactly 8192 bytes.");
            _characterStart = SaveLayout.LegacyCharacterStart;
            _characterSize = SaveLayout.LegacyCharacterSize;
            _characterCount = SaveLayout.LegacyCharacterCount;
            _itemStart = SaveLayout.LegacyItemStart;
            _itemCount = SaveLayout.LegacyItemCount;
            _cardStart = SaveLayout.LegacyCardStart;
            _cardRecordSize = SaveLayout.LegacyCardRecordSize;
            _cardCount = SaveLayout.LegacyCardCount;
            _cardLayout = SaveLayout.LegacyCardLayout;
        }
        else
        {
            if (data.Length != SaveLayout.RrSlotPlaintextSize) throw new SaveFormatException("rr2016 slot has an unexpected plaintext size.");
            _characterStart = SaveLayout.RrCharacterStart;
            _characterSize = SaveLayout.RrCharacterSize;
            _characterCount = SaveLayout.RrCharacterCount;
            _itemStart = SaveLayout.RrItemStart;
            _itemCount = SaveLayout.RrItemCount;
            _cardStart = SaveLayout.RrCardStart;
            _cardRecordSize = SaveLayout.RrCardRecordSize;
            _cardCount = SaveLayout.RrCardCount;
            _cardLayout = SaveLayout.RrCardLayout;
        }
    }

    public SaveFormat Format { get; }
    internal byte[] Bytes => _data;

    public IEditableCharacter Character(int index)
    {
        if ((uint)index >= _characterCount) throw new ArgumentOutOfRangeException(nameof(index));
        return new BinaryCharacter(_data, _characterStart + index * _characterSize, Format, index);
    }

    public IReadOnlyList<IEditableCharacter> Characters() => Enumerable.Range(0, _characterCount).Select(Character).ToArray();

    public IReadOnlyList<InventoryItem> Items()
    {
        var result = new List<InventoryItem>();
        for (var index = 0; index < _itemCount; index++)
        {
            var offset = _itemStart + index * 2;
            var itemId = _data[offset + ItemIdByte];
            var count = _data[offset + ItemCountByte];
            if (itemId != GameData.EmptyItemId && count > 0)
                result.Add(new(itemId, count, index));
        }
        return result;
    }

    public bool SetItem(int itemId, int count)
    {
        if (itemId is < byte.MinValue or > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(itemId), "PS1 and rr2016 item IDs must be between 0 and 255.");
        var storedItemId = (byte)itemId;
        var bounded = (byte)Math.Clamp(count, 0, 99);
        for (var index = 0; index < _itemCount; index++)
        {
            var offset = _itemStart + index * 2;
            if (_data[offset + ItemIdByte] != storedItemId || _data[offset + ItemCountByte] == 0) continue;
            if (bounded == 0)
            {
                _data[offset + ItemIdByte] = GameData.EmptyItemId;
                _data[offset + ItemCountByte] = 0;
            }
            else
                _data[offset + ItemCountByte] = Math.Max(_data[offset + ItemCountByte], bounded);
            return true;
        }
        if (bounded == 0) return true;
        for (var index = 0; index < _itemCount; index++)
        {
            var offset = _itemStart + index * 2;
            if (_data[offset + ItemIdByte] != GameData.EmptyItemId && _data[offset + ItemCountByte] != 0) continue;
            _data[offset + ItemIdByte] = storedItemId;
            _data[offset + ItemCountByte] = bounded;
            return true;
        }
        return false;
    }

    public void RemoveItem(int itemId) => SetItem(itemId, 0);

    // The PS1 FF9ITEM record is [id, count]. The 2016 serializer writes the
    // same fields in the opposite order: [count, id]. Keep this distinction
    // local to the binary implementation so callers always see (id, count).
    private int ItemIdByte => Format == SaveFormat.Rr2016 ? 1 : 0;
    private int ItemCountByte => Format == SaveFormat.Rr2016 ? 0 : 1;

    public int Gil
    {
        get => BinarySave.GetField(_data, 0, Format == SaveFormat.Legacy ? SaveLayout.LegacyGil : SaveLayout.RrGil);
        set
        {
            var bounded = Math.Clamp(value, 0, SaveLayout.MaximumGil);
            BinarySave.SetField(_data, 0, Format == SaveFormat.Legacy ? SaveLayout.LegacyGil : SaveLayout.RrGil, bounded);
            if (Format == SaveFormat.Legacy)
                BinarySave.SetField(_data, 0, SaveLayout.LegacyPreviewGil, bounded);
        }
    }

    public string LeaderName
    {
        get
        {
            if (Format == SaveFormat.Legacy)
                return LegacyTextCodec.Decode(_data.AsSpan(SaveLayout.LegacyLeaderNameOffset, SaveLayout.LegacyLeaderNameLength));
            return Characters().FirstOrDefault(x => x.IsRecruited)?.Name ?? string.Empty;
        }
    }

    public string? Location => Format == SaveFormat.Legacy
        ? LegacyTextCodec.Decode(_data.AsSpan(SaveLayout.LegacyLocationOffset, SaveLayout.LegacyLocationLength))
        : null;

    public double PlaytimeSeconds => Format == SaveFormat.Legacy
        ? BinarySave.GetField(_data, 0, SaveLayout.LegacyPlaytime) / (double)_legacyFrameRate
        : BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(_data.AsSpan(SaveLayout.RrPlaytimeOffset)));

    public IReadOnlyList<int>? PartyMemberIds => Format == SaveFormat.Rr2016
        ? _data.AsSpan(SaveLayout.RrPartyOffset, SaveLayout.RrPartySlots).ToArray().Where(x => x != byte.MaxValue).Select(x => (int)x).ToArray()
        : null;

    public IReadOnlyList<CardInfo> Cards()
    {
        var result = new List<CardInfo>();
        for (var index = 0; index < _cardCount; index++)
        {
            var card = ReadCard(index);
            if (card.TypeId < GameData.CardTypeNames.Count)
                result.Add(card);
        }
        return result;
    }

    public void SetCard(int index, byte typeId, byte arrows = 0, byte attack = 0, byte attackType = 0,
        byte physicalDefense = 0, byte magicDefense = 0)
    {
        if ((uint)index >= _cardCount) throw new ArgumentOutOfRangeException(nameof(index));
        var offset = _cardStart + index * _cardRecordSize;
        _data[offset + _cardLayout[0]] = typeId;
        _data[offset + _cardLayout[1]] = arrows;
        _data[offset + _cardLayout[2]] = attack;
        _data[offset + _cardLayout[3]] = (byte)(attackType & 0x03);
        _data[offset + _cardLayout[4]] = physicalDefense;
        _data[offset + _cardLayout[5]] = magicDefense;
    }

    public (int Wins, int Losses, int Draws) CardRecord
    {
        get
        {
            var wins = Format == SaveFormat.Legacy ? SaveLayout.LegacyCardWins : SaveLayout.RrCardWins;
            var losses = Format == SaveFormat.Legacy ? SaveLayout.LegacyCardLosses : SaveLayout.RrCardLosses;
            var draws = Format == SaveFormat.Legacy ? SaveLayout.LegacyCardDraws : SaveLayout.RrCardDraws;
            return (BinarySave.GetField(_data, 0, wins), BinarySave.GetField(_data, 0, losses), BinarySave.GetField(_data, 0, draws));
        }
    }

    public IEditableSlot Clone() => new BinarySlot((byte[])_data.Clone(), Format, _legacyFrameRate);

    public void FinalizeEdits()
    {
        if (Format == SaveFormat.Legacy)
            LegacyChecksum.Repair(_data);
    }

    private CardInfo ReadCard(int index)
    {
        var offset = _cardStart + index * _cardRecordSize;
        return new(index, _data[offset + _cardLayout[0]], _data[offset + _cardLayout[1]],
            _data[offset + _cardLayout[2]], _data[offset + _cardLayout[3]],
            _data[offset + _cardLayout[4]], _data[offset + _cardLayout[5]]);
    }
}
