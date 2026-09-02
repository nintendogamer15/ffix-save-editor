using System.Buffers.Binary;
using FFIX.SaveEditor.Core;
using FFIX.SaveEditor.Gui.Saves;
using SaveEditor.Ui.Codecs;

namespace FFIX.SaveEditor.Gui.Tests;

public sealed class CodecTests
{
    [Fact]
    public async Task ThreeSupportedFormatsRoundTripByteForByte()
    {
        FfixSaveCodec codec = new();
        byte[][] inputs =
        [
            CreateLegacyBlock(),
            CreateRrContainer(),
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SavedData_ww_Memoria_0_0.dat")),
        ];

        foreach (byte[] input in inputs)
        {
            SaveDocument document = await codec.DecodeAsync(new MemoryStream(input));
            using MemoryStream output = new();
            await codec.SerializeAsync(document, output);
            Assert.Equal(input, output.ToArray());
        }
    }

    [Fact]
    public void ComparerChecksEverySerializedByte()
    {
        SaveDocument first = SaveDocument.Parse("save.ps1", CreateLegacyBlock());
        byte[] changed = CreateLegacyBlock();
        changed[500] ^= 0x5a;
        SaveDocument second = SaveDocument.Parse("save.ps1", changed);

        Assert.False(FfixDocumentComparer.Instance.Equals(first, second));
        Assert.True(FfixDocumentComparer.Instance.Equals(first, SaveDocument.Parse("save.ps1", first.ToArray())));
    }

    [Fact]
    public async Task DeepMemoriaInputFailsWithoutRecursingInCore()
    {
        FfixSaveCodec codec = new();
        byte[] hostile = NestedArrays(129);

        SaveFormatException exception = await Assert.ThrowsAsync<SaveFormatException>(async () =>
            await codec.DecodeAsync(new MemoryStream(hostile)));

        Assert.Contains("depth", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MemoriaPreflightAcceptsExactDepthAndRejectsOneOver()
    {
        MemoriaPreflight.Validate(NestedArrays(MemoriaPreflight.MaximumDepth));
        Assert.Throws<SaveFormatException>(() =>
            MemoriaPreflight.Validate(NestedArrays(MemoriaPreflight.MaximumDepth + 1)));
    }

    [Fact]
    public void MemoriaPreflightAcceptsExactCollectionAndStringLimitsAndRejectsOneOver()
    {
        MemoriaPreflight.Validate(ArrayOfIntegers(MemoriaPreflight.MaximumCollectionEntries));
        Assert.Throws<SaveFormatException>(() =>
            MemoriaPreflight.Validate(ArrayOfIntegers(MemoriaPreflight.MaximumCollectionEntries + 1)));

        MemoriaPreflight.Validate(RootString(MemoriaPreflight.MaximumStringBytes));
        Assert.Throws<SaveFormatException>(() =>
            MemoriaPreflight.Validate(RootString(MemoriaPreflight.MaximumStringBytes + 1)));
    }

    [Fact]
    public void MemoriaPreflightAcceptsExactTotalValueLimitAndRejectsOneOver()
    {
        MemoriaPreflight.Validate(ValueCorpus(extraPrimitiveArrays: 49_999));
        Assert.Throws<SaveFormatException>(() =>
            MemoriaPreflight.Validate(ValueCorpus(extraPrimitiveArrays: 50_000)));
    }

    [Fact]
    public void MemoriaPreflightRejectsTruncatedTraversalAndMalformedUtf8()
    {
        byte[] truncated = NestedArrays(16);
        MemoriaPreflight.Validate(truncated);
        Assert.Throws<SaveFormatException>(() => MemoriaPreflight.Validate(truncated.AsSpan(0, truncated.Length - 1)));

        using MemoryStream malformed = new();
        WriteInt(malformed, (int)MemoriaValueKind.String);
        Write7Bit(malformed, 1);
        malformed.WriteByte(0xff);
        Assert.Throws<SaveFormatException>(() => MemoriaPreflight.Validate(malformed.ToArray()));
    }

    [Fact]
    public async Task ZeroSlotCardIsRejectedBeforeAdoption()
    {
        byte[] emptyCard = new byte[SaveLayout.LegacyCardSize];
        "MC"u8.CopyTo(emptyCard);

        SaveFormatException exception = await Assert.ThrowsAsync<SaveFormatException>(async () =>
            await new FfixSaveCodec().DecodeAsync(new MemoryStream(emptyCard)));

        Assert.Contains("no occupied", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DetectorUsesOnlyItsFixedPrefixAndDefersEncryptedInputs()
    {
        FfixSaveDetector detector = new();
        Assert.Equal(4, detector.HeaderBytesRequired);
        Assert.Equal(DetectionVerdict.Confident, detector.Detect("SCxx"u8));
        Assert.Equal(DetectionVerdict.Confident, detector.Detect("MCxx"u8));
        Assert.Equal(DetectionVerdict.RequiresDecode, detector.Detect([0x12, 0x34, 0x56, 0x78]));
    }

    private static byte[] NestedArrays(int depth)
    {
        using MemoryStream stream = new();
        WriteInt(stream, (int)MemoriaValueKind.Dictionary);
        WriteInt(stream, 1);
        stream.WriteByte(1);
        stream.WriteByte((byte)'x');
        for (int index = 2; index < depth; index++)
        {
            WriteInt(stream, (int)MemoriaValueKind.Array);
            WriteInt(stream, 1);
        }
        WriteInt(stream, (int)MemoriaValueKind.Int32);
        WriteInt(stream, 0);
        return stream.ToArray();
    }

    private static byte[] ArrayOfIntegers(int count)
    {
        using MemoryStream stream = new();
        WriteInt(stream, (int)MemoriaValueKind.Array);
        WriteInt(stream, count);
        for (int index = 0; index < Math.Min(count, MemoriaPreflight.MaximumCollectionEntries); index++)
        {
            WriteInt(stream, (int)MemoriaValueKind.Int32);
            WriteInt(stream, index);
        }
        return stream.ToArray();
    }

    private static byte[] RootString(int length)
    {
        using MemoryStream stream = new();
        WriteInt(stream, (int)MemoriaValueKind.String);
        Write7Bit(stream, length);
        stream.Write(new byte[length]);
        return stream.ToArray();
    }

    private static byte[] ValueCorpus(int extraPrimitiveArrays)
    {
        using MemoryStream stream = new();
        WriteInt(stream, (int)MemoriaValueKind.Array);
        WriteInt(stream, MemoriaPreflight.MaximumCollectionEntries);
        for (int index = 0; index < MemoriaPreflight.MaximumCollectionEntries; index++)
        {
            int children = index < extraPrimitiveArrays ? 2 : 1;
            WriteInt(stream, (int)MemoriaValueKind.Array);
            WriteInt(stream, children);
            for (int child = 0; child < children; child++)
            {
                WriteInt(stream, (int)MemoriaValueKind.Int32);
                WriteInt(stream, child);
            }
        }
        return stream.ToArray();
    }

    private static void Write7Bit(Stream stream, int length)
    {
        uint value = (uint)length;
        do
        {
            byte next = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0) next |= 0x80;
            stream.WriteByte(next);
        } while (value != 0);
    }

    private static byte[] CreateLegacyBlock()
    {
        byte[] block = new byte[SaveLayout.LegacyBlockSize];
        "SC"u8.CopyTo(block);
        block[SaveLayout.LegacyLeaderLevelOffset] = 1;
        LegacyTextCodec.Encode("Zidane", SaveLayout.LegacyLeaderNameLength).CopyTo(block, SaveLayout.LegacyLeaderNameOffset);
        BinarySlot slot = new(block, SaveFormat.Legacy);
        slot.Character(0).Set("level", 1);
        slot.Character(0).Name = "Zidane";
        LegacyChecksum.Repair(block);
        return block;
    }

    private static byte[] CreateRrContainer()
    {
        byte[] container = new byte[SaveLayout.RrContainerSize];
        byte[] metadata = new byte[SaveLayout.RrMetadataPlaintextSize];
        "SAVE"u8.CopyTo(metadata);
        BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(4), BitConverter.SingleToInt32Bits(1));
        BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(8), SaveLayout.RrSlotPlaintextSize - 4);
        RrCrypto.Encrypt(metadata).CopyTo(container, 0);
        byte[] bytes = new byte[SaveLayout.RrSlotPlaintextSize];
        SaveLayout.RrOccupiedHeader.CopyTo(bytes);
        BinarySlot slot = new(bytes, SaveFormat.Rr2016);
        slot.Character(0).Set("level", 1);
        slot.Character(0).Name = "Zidane";
        RrCrypto.Encrypt(bytes).CopyTo(container, RrCrypto.ChunkOffset(0, 0));
        return container;
    }

    private static void WriteInt(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
