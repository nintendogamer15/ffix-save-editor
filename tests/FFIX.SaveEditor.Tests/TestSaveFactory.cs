using System.Buffers.Binary;
using FFIX.SaveEditor.Core;

namespace FFIX.SaveEditor.Tests;

internal static class TestSaveFactory
{
    public static byte[] CreateLegacyBlock(int gil = 3_435, string name = "Zidane")
    {
        var block = new byte[SaveLayout.LegacyBlockSize];
        "SC"u8.CopyTo(block);
        block[SaveLayout.LegacyLeaderLevelOffset] = 7;
        LegacyTextCodec.Encode(name, SaveLayout.LegacyLeaderNameLength).CopyTo(block, SaveLayout.LegacyLeaderNameOffset);
        LegacyTextCodec.Encode("Lindblum/Inn", SaveLayout.LegacyLocationLength).CopyTo(block, SaveLayout.LegacyLocationOffset);
        WriteUInt(block, SaveLayout.LegacyGil, gil);
        WriteUInt(block, SaveLayout.LegacyPreviewGil, gil);
        WriteUInt(block, SaveLayout.LegacyPlaytime, 60 * 60 * 60);
        var slot = new BinarySlot(block, SaveFormat.Legacy);
        var character = slot.Character(0);
        character.Name = name;
        character.Set("level", 7);
        LegacyChecksum.Repair(block);
        return block;
    }

    public static byte[] CreateRrContainer()
    {
        var container = new byte[SaveLayout.RrContainerSize];
        var metadata = new byte[SaveLayout.RrMetadataPlaintextSize];
        "SAVE"u8.CopyTo(metadata);
        BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(4), BitConverter.SingleToInt32Bits(1.0f));
        BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(8), SaveLayout.RrSlotPlaintextSize - 4);
        BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(12), 0);
        BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(16), 0);
        BinaryPrimitives.WriteInt64LittleEndian(metadata.AsSpan(20), BitConverter.DoubleToInt64Bits(0));
        BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(28), 0);
        BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(32), -1);
        RrCrypto.Encrypt(metadata).CopyTo(container, 0);

        var slotBytes = new byte[SaveLayout.RrSlotPlaintextSize];
        SaveLayout.RrOccupiedHeader.CopyTo(slotBytes);
        var slot = new BinarySlot(slotBytes, SaveFormat.Rr2016);
        var character = slot.Character(0);
        character.Name = "Zidane";
        character.Set("level", 1);
        character.Set("cur_hp", 105);
        character.Set("max_hp", 105);
        character.Set("max_hp_base", 105);
        character.Set("cur_mp", 36);
        character.Set("max_mp", 36);
        character.Set("max_mp_base", 36);
        RrCrypto.Encrypt(slotBytes).CopyTo(container, RrCrypto.ChunkOffset(0, 0));

        var empty = new byte[SaveLayout.RrSlotPlaintextSize];
        SaveLayout.RrEmptyHeader.CopyTo(empty);
        RrCrypto.Encrypt(empty).CopyTo(container, RrCrypto.ChunkOffset(0, 1));
        return container;
    }

    public static MemoriaValue CreateMemoriaTree() => MemoriaValue.Dictionary([
        new("95000_Setting", MemoriaValue.Dictionary([new("00001_time", MemoriaValue.Double(12.5))])),
        new("20000_Event", MemoriaValue.Dictionary()),
        new("40000_Common", MemoriaValue.Dictionary([
            new("gil", MemoriaValue.Int32(500)),
            new("items", MemoriaValue.Array([MemoriaValue.Dictionary([
                new("id", MemoriaValue.Int32(236)), new("count", MemoriaValue.Int32(2))])])),
            new("players", MemoriaValue.Array([MemoriaValue.Dictionary([
                new("name", MemoriaValue.String("Zidane")), new("level", MemoriaValue.Int32(1)),
                new("exp", MemoriaValue.Int32(0)),
                new("cur", MemoriaValue.Dictionary([new("hp", MemoriaValue.Int32(105)), new("mp", MemoriaValue.Int32(36))])),
                new("max", MemoriaValue.Dictionary([new("hp", MemoriaValue.Int32(105)), new("mp", MemoriaValue.Int32(36))])),
                new("basis", MemoriaValue.Dictionary([
                    new("max_hp", MemoriaValue.Int32(105)), new("max_mp", MemoriaValue.Int32(36)),
                    new("dex", MemoriaValue.Int32(23)), new("str", MemoriaValue.Int32(21)),
                    new("mgc", MemoriaValue.Int32(18)), new("wpr", MemoriaValue.Int32(23))])),
                new("elem", MemoriaValue.Dictionary([
                    new("dex", MemoriaValue.Int32(23)), new("str", MemoriaValue.Int32(21)),
                    new("mgc", MemoriaValue.Int32(18)), new("wpr", MemoriaValue.Int32(23))])),
                new("trance", MemoriaValue.Int32(0)),
                new("equip", MemoriaValue.Array(new[] { 1, 112, 88, 149, 255 }.Select(MemoriaValue.Int32)))
            ])]))
        ])),
        new("30000_MiniGame", MemoriaValue.Dictionary([
            new("MiniGameCard", MemoriaValue.Array()), new("sWin", MemoriaValue.Int32(0)),
            new("sLose", MemoriaValue.Int32(0)), new("sDraw", MemoriaValue.Int32(0))]))
    ]);

    public static void SetDirectoryEntry(byte[] card, int blockIndex, ReadOnlySpan<byte> product)
    {
        var header = new byte[SaveLayout.LegacyBlockHeaderSize];
        header[0] = 0x51;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5), SaveLayout.LegacyBlockSize);
        header[8] = header[9] = 0xFF;
        product.CopyTo(header.AsSpan(SaveLayout.LegacyRegionCodeOffset));
        byte checksum = 0;
        foreach (var value in header.AsSpan(0, header.Length - 1)) checksum ^= value;
        header[^1] = checksum;
        header.CopyTo(card, blockIndex * SaveLayout.LegacyBlockHeaderSize);
    }

    public static void WriteUInt(byte[] data, FieldSpec field, int value)
    {
        switch (field.Size)
        {
            case 1: data[field.Offset] = (byte)value; break;
            case 2: BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(field.Offset), (ushort)value); break;
            case 3:
                data[field.Offset] = (byte)value; data[field.Offset + 1] = (byte)(value >> 8); data[field.Offset + 2] = (byte)(value >> 16);
                break;
            case 4: BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(field.Offset), (uint)value); break;
        }
    }

    public static int ReadUInt(byte[] data, FieldSpec field) => field.Size switch
    {
        1 => data[field.Offset],
        2 => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(field.Offset)),
        3 => data[field.Offset] | data[field.Offset + 1] << 8 | data[field.Offset + 2] << 16,
        4 => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(field.Offset))),
        _ => throw new ArgumentOutOfRangeException(),
    };
}
