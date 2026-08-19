// SPDX-License-Identifier: MIT
using System.Buffers.Binary;

namespace FFIX.SaveEditor.Core;

public sealed class SaveDocument
{
    private static IReadOnlySet<string> LegacyCardExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mcr", ".mcd", ".bin", ".mc", ".mci", ".ps", ".psm", ".dff" };
    private static IReadOnlySet<string> LegacySimpleExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ps1", ".mcs" };
    private static IReadOnlySet<string> RrExtensions { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dat", ".sav" };
    private static IReadOnlySet<string> LegacyFfixDiscCodes { get; } = BuildDiscCodes();

    private readonly byte[] _raw;
    private readonly byte[] _header;
    private readonly byte[] _memoriaTrailing;
    private MemoriaSlot? _memoriaSlot;

    private SaveDocument(SaveFormat format, byte[] raw, byte[]? header = null, RrMetadata? metadata = null,
        MemoriaSlot? memoriaSlot = null, byte[]? memoriaTrailing = null)
    {
        Format = format;
        _raw = raw;
        _header = header ?? [];
        _memoriaTrailing = memoriaTrailing ?? [];
        Metadata = metadata;
        _memoriaSlot = memoriaSlot;
    }

    public SaveFormat Format { get; }
    public RrMetadata? Metadata { get; }
    public static SaveDocument Open(string path)
    {
        var raw = File.ReadAllBytes(path);
        return Parse(path, raw);
    }

    public static SaveDocument Parse(string path, ReadOnlySpan<byte> source)
    {
        var kind = DetectFormat(path, source);
        if (kind == DetectedFormat.Rr2016)
        {
            var bytes = source.ToArray();
            return new(SaveFormat.Rr2016, bytes, metadata: RrCrypto.ParseMetadata(bytes));
        }
        if (kind == DetectedFormat.Memoria)
        {
            var parsed = MemoriaCodec.ParsePreservingTrailing(source);
            return new(SaveFormat.Memoria, [], memoriaSlot: new MemoriaSlot(parsed.Root),
                memoriaTrailing: parsed.TrailingBytes);
        }
        if (kind == DetectedFormat.LegacySimple)
        {
            var header = source[..SaveLayout.LegacyBlockHeaderSize].ToArray();
            var body = source[SaveLayout.LegacyBlockHeaderSize..].ToArray();
            if (!body.AsSpan(0, 2).SequenceEqual("SC"u8))
                throw new SaveFormatException("Legacy save-data block is missing the PS1 'SC' header.");
            return new(SaveFormat.Legacy, body, header);
        }
        if (kind == DetectedFormat.LegacyRaw && !source[..2].SequenceEqual("SC"u8))
            throw new SaveFormatException("Legacy save-data block is missing the PS1 'SC' header.");
        if (kind == DetectedFormat.LegacyCard && !source[..2].SequenceEqual("MC"u8))
            throw new SaveFormatException("Memory-card image is missing the PS1 'MC' header.");
        return new(SaveFormat.Legacy, source.ToArray());
    }

    public static string FormatLabel(SaveFormat format) => format switch
    {
        SaveFormat.Legacy => "PS1 memory-card save",
        SaveFormat.Rr2016 => "Steam/PC/mobile (2016) save",
        SaveFormat.Memoria => "Memoria mod save (unencrypted)",
        _ => format.ToString(),
    };

    public IReadOnlyList<SlotReference> ListSlots()
    {
        if (Format == SaveFormat.Memoria)
        {
            var slot = _memoriaSlot!;
            return [new(SaveFormat.Memoria, "Save data", $"{slot.LeaderName}  gil={slot.Gil:N0}")];
        }
        if (Format == SaveFormat.Rr2016)
            return ListRrSlots();
        if (_raw.Length == SaveLayout.LegacyCardSize)
        {
            var result = new List<SlotReference>();
            for (var blockIndex = 1; blockIndex < SaveLayout.LegacyMaximumBlocks; blockIndex++)
            {
                if (LegacyBlockLooksEmpty(blockIndex) || !LegacyBlockIsFfix(blockIndex)) continue;
                result.Add(CreateLegacyReference(blockIndex, LegacyBlock(blockIndex), LegacyFrameRate(blockIndex)));
            }
            return result;
        }
        return [CreateLegacyReference(null, _raw, LegacyFrameRate(null))];
    }

    public IEditableSlot LoadSlot(SlotReference reference)
    {
        if (reference.Format == SaveFormat.Memoria)
            return _memoriaSlot!.Clone();
        if (reference.Format == SaveFormat.Legacy)
        {
            var bytes = reference.BlockIndex is null ? (byte[])_raw.Clone() : LegacyBlock(reference.BlockIndex.Value).ToArray();
            return new BinarySlot(bytes, SaveFormat.Legacy, reference.LegacyFrameRate ?? 60);
        }
        return LoadRrSlot(reference.SlotId ?? throw new ArgumentException("rr2016 reference has no slot ID."),
            reference.SaveId ?? throw new ArgumentException("rr2016 reference has no file ID."));
    }

    public void CommitSlot(SlotReference reference, IEditableSlot slot)
    {
        if (slot.Format != reference.Format)
            throw new ArgumentException("Slot format does not match its document reference.", nameof(slot));
        slot.FinalizeEdits();
        if (reference.Format == SaveFormat.Memoria)
        {
            if (slot is not MemoriaSlot memoria) throw new ArgumentException("Expected a Memoria slot.", nameof(slot));
            _memoriaSlot = (MemoriaSlot)memoria.Clone();
            return;
        }
        if (reference.Format == SaveFormat.Legacy)
        {
            if (slot is not BinarySlot binary || binary.Bytes.Length != SaveLayout.LegacyBlockSize)
                throw new ArgumentException("Expected a complete legacy block.", nameof(slot));
            if (reference.BlockIndex is null) binary.Bytes.CopyTo(_raw, 0);
            else binary.Bytes.CopyTo(_raw, reference.BlockIndex.Value * SaveLayout.LegacyBlockSize);
            return;
        }
        if (slot is not BinarySlot rr) throw new ArgumentException("Expected an rr2016 binary slot.", nameof(slot));
        CommitRrSlot(reference.SlotId!.Value, reference.SaveId!.Value, rr);
    }

    public byte[] ToArray()
    {
        if (Format == SaveFormat.Memoria)
        {
            var tree = MemoriaCodec.Serialize(_memoriaSlot!.Root);
            if (_memoriaTrailing.Length == 0) return tree;
            var memoriaOutput = new byte[tree.Length + _memoriaTrailing.Length];
            tree.CopyTo(memoriaOutput, 0);
            _memoriaTrailing.CopyTo(memoriaOutput, tree.Length);
            return memoriaOutput;
        }
        if (_header.Length == 0)
            return (byte[])_raw.Clone();
        var output = new byte[_header.Length + _raw.Length];
        _header.CopyTo(output, 0);
        _raw.CopyTo(output, _header.Length);
        return output;
    }

    public SlotReference? ProbeRrSlot(int slotId, int saveId)
    {
        if (Format != SaveFormat.Rr2016) throw new InvalidOperationException("Document is not rr2016.");
        var start = RrCrypto.ChunkOffset(slotId, saveId);
        var cipherLength = RrCrypto.CipherSize(Metadata!.SlotPlaintextSize);
        if (start > _raw.Length - cipherLength) return null;
        var ciphertext = _raw.AsSpan(start, cipherLength);
        if (!ciphertext.ContainsAnyExcept((byte)0)) return null;
        byte[] plaintext;
        try { plaintext = RrCrypto.Decrypt(ciphertext); }
        catch (SaveFormatException) { return null; }
        if (plaintext.Length != Metadata.SlotPlaintextSize || plaintext.AsSpan(0, 4).SequenceEqual(SaveLayout.RrEmptyHeader)
            || !plaintext.AsSpan(0, 4).SequenceEqual(SaveLayout.RrOccupiedHeader))
            return null;
        var slot = new BinarySlot(plaintext, SaveFormat.Rr2016);
        return new(SaveFormat.Rr2016, $"Slot {slotId + 1} / File {saveId + 1}", slot.LeaderName,
            SlotId: slotId, SaveId: saveId);
    }

    public static int GiveAllItems(IEditableSlot slot, int quantity)
    {
        var count = 0;
        for (var itemId = 0; itemId < GameData.ItemNames.Count; itemId++)
            if (itemId != GameData.EmptyItemId && slot.SetItem(itemId, quantity)) count++;
        return count;
    }

    private IReadOnlyList<SlotReference> ListRrSlots()
    {
        var result = new List<SlotReference>();
        for (var slotId = 0; slotId < SaveLayout.RrMaximumSlots; slotId++)
            for (var saveId = 0; saveId < SaveLayout.RrMaximumSaves; saveId++)
                if (ProbeRrSlot(slotId, saveId) is { } reference) result.Add(reference);
        return result;
    }

    private BinarySlot LoadRrSlot(int slotId, int saveId)
    {
        var start = RrCrypto.ChunkOffset(slotId, saveId);
        var cipherLength = RrCrypto.CipherSize(Metadata!.SlotPlaintextSize);
        if (start > _raw.Length - cipherLength) throw new SaveFormatException("rr2016 slot extends beyond the container.");
        var plaintext = RrCrypto.Decrypt(_raw.AsSpan(start, cipherLength));
        if (plaintext.Length != Metadata.SlotPlaintextSize || !plaintext.AsSpan(0, 4).SequenceEqual(SaveLayout.RrOccupiedHeader))
            throw new SaveFormatException($"Slot {slotId + 1} / File {saveId + 1} is empty or invalid.");
        return new BinarySlot(plaintext, SaveFormat.Rr2016);
    }

    private void CommitRrSlot(int slotId, int saveId, BinarySlot slot)
    {
        if (slot.Bytes.Length != Metadata!.SlotPlaintextSize || !slot.Bytes.AsSpan(0, 4).SequenceEqual(SaveLayout.RrOccupiedHeader))
            throw new SaveFormatException("Refusing to write an invalid rr2016 slot.");
        var encrypted = RrCrypto.Encrypt(slot.Bytes);
        encrypted.CopyTo(_raw, RrCrypto.ChunkOffset(slotId, saveId));
    }

    private SlotReference CreateLegacyReference(int? blockIndex, ReadOnlySpan<byte> block, int frameRate)
    {
        var leader = LegacyTextCodec.Decode(block.Slice(SaveLayout.LegacyLeaderNameOffset, SaveLayout.LegacyLeaderNameLength));
        var level = block[SaveLayout.LegacyLeaderLevelOffset];
        var location = LegacyTextCodec.Decode(block.Slice(SaveLayout.LegacyLocationOffset, SaveLayout.LegacyLocationLength));
        var stored = BinaryPrimitives.ReadUInt16LittleEndian(block[SaveLayout.LegacyChecksumOffset..]);
        var suffix = stored == LegacyChecksum.Calculate(block) ? "" : "  [checksum mismatch]";
        return new(SaveFormat.Legacy, blockIndex is null ? "Save data" : $"Block {blockIndex}",
            $"{(string.IsNullOrEmpty(leader) ? "?" : leader)} Lv{level}  {location}{suffix}".Trim(),
            BlockIndex: blockIndex, LegacyFrameRate: frameRate);
    }

    private ReadOnlySpan<byte> LegacyBlock(int blockIndex) => _raw.AsSpan(blockIndex * SaveLayout.LegacyBlockSize, SaveLayout.LegacyBlockSize);

    private bool LegacyBlockLooksEmpty(int blockIndex)
    {
        var header = _raw.AsSpan(blockIndex * SaveLayout.LegacyBlockHeaderSize, SaveLayout.LegacyBlockHeaderSize);
        var region = header.Slice(SaveLayout.LegacyRegionCodeOffset, 4);
        if (!region.ContainsAnyExcept((byte)0) && header[0] == 0xA0) return true;
        return !LegacyBlock(blockIndex).ContainsAnyExcept((byte)0);
    }

    private bool LegacyBlockIsFfix(int blockIndex)
    {
        var header = _raw.AsSpan(blockIndex * SaveLayout.LegacyBlockHeaderSize, SaveLayout.LegacyBlockHeaderSize);
        if (header[0] != 0x51) return false;
        var product = header.Slice(SaveLayout.LegacyRegionCodeOffset, 20);
        var discCode = System.Text.Encoding.ASCII.GetString(product[..17]);
        return LegacyFfixDiscCodes.Contains(discCode) && product[17] == (byte)'-'
               && product[18] is >= (byte)'0' and <= (byte)'9' && product[19] is >= (byte)'0' and <= (byte)'9';
    }

    private int LegacyFrameRate(int? blockIndex)
    {
        ReadOnlySpan<byte> header = blockIndex is null
            ? _header
            : _raw.AsSpan(blockIndex.Value * SaveLayout.LegacyBlockHeaderSize, SaveLayout.LegacyBlockHeaderSize);
        return header.Length >= SaveLayout.LegacyRegionCodeOffset + 7
               && header.Slice(SaveLayout.LegacyRegionCodeOffset, 7).SequenceEqual("BESLES-"u8) ? 50 : 60;
    }

    private static DetectedFormat DetectFormat(string path, ReadOnlySpan<byte> source)
    {
        if (source.Length == SaveLayout.RrContainerSize) return DetectedFormat.Rr2016;
        if (source.Length == SaveLayout.LegacyCardSize) return DetectedFormat.LegacyCard;
        if (source.Length == SaveLayout.LegacyBlockSize) return DetectedFormat.LegacyRaw;
        if (source.Length == SaveLayout.LegacyBlockHeaderSize + SaveLayout.LegacyBlockSize) return DetectedFormat.LegacySimple;
        if (MemoriaCodec.LooksLikeSave(source)) return DetectedFormat.Memoria;
        var extension = Path.GetExtension(path);
        if (RrExtensions.Contains(extension))
            throw new SaveFormatException($"{Path.GetFileName(path)} is {source.Length:N0} bytes instead of the rr2016 size {SaveLayout.RrContainerSize:N0}, and is not a Memoria save.");
        if (LegacyCardExtensions.Contains(extension) || LegacySimpleExtensions.Contains(extension))
            throw new SaveFormatException($"{Path.GetFileName(path)} has a legacy extension but is {source.Length:N0} bytes; expected 8,192, 8,320, or 131,072.");
        throw new SaveFormatException($"Unrecognized save: {source.Length:N0} bytes, extension '{extension}'.");
    }

    private static IReadOnlySet<string> BuildDiscCodes()
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (var disc in new[] { "01251", "01295", "01296", "01297" }) values.Add($"BASLUS-{disc}00000");
        foreach (var disc in new[] { "02000", "02001", "02002", "02003" }) values.Add($"BISLPS-{disc}00000");
        foreach (var disc in new[] { "0", "1", "2", "3" })
            foreach (var language in new[] { "2965", "2966", "2967", "2968", "2969" })
                values.Add($"BESLES-{disc}{language}00000");
        return values;
    }

    private enum DetectedFormat { LegacyRaw, LegacySimple, LegacyCard, Rr2016, Memoria }
}
