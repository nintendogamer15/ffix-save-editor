using System.Buffers.Binary;
using System.Security.Cryptography;
using FFIX.SaveEditor.Core;

namespace FFIX.SaveEditor.Tests;

public sealed class MemoriaTests
{
    [Fact]
    public void SyntheticTreeRoundTrips()
    {
        var encoded = MemoriaCodec.Serialize(TestSaveFactory.CreateMemoriaTree());
        Assert.Equal(encoded, MemoriaCodec.Serialize(MemoriaCodec.Parse(encoded)));
        Assert.True(MemoriaCodec.LooksLikeSave(encoded));
    }

    [Fact]
    public void RealFixtureRoundTripsByteForByte()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SavedData_ww_Memoria_0_0.dat");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(32_820, bytes.Length);
        Assert.Equal("2bdc97ac6c4160ae6c359b8f675852b8b635c26c8e5c3dd6ba9f351cb563add3",
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        Assert.Equal(bytes, MemoriaCodec.Serialize(MemoriaCodec.Parse(bytes)));
    }

    [Fact]
    public void DocumentPreservesStaleBytesLeftByMemoriasNonTruncatingWriter()
    {
        var tree = MemoriaCodec.Serialize(TestSaveFactory.CreateMemoriaTree());
        byte[] staleTail = [4, (byte)'s', (byte)'W', (byte)'i', (byte)'n', 4, 0, 0, 0, 3, 0, 0, 0];
        var source = tree.Concat(staleTail).ToArray();

        Assert.Throws<SaveFormatException>(() => MemoriaCodec.Parse(source));
        var document = SaveDocument.Parse("SavedData_ww_Memoria_0_0.dat", source);
        Assert.Equal(SaveFormat.Memoria, document.Format);
        Assert.Equal(source, document.ToArray());

        var reference = document.ListSlots()[0];
        var slot = document.LoadSlot(reference);
        slot.Gil = 123_456;
        document.CommitSlot(reference, slot);
        var edited = document.ToArray();
        Assert.True(edited.AsSpan().EndsWith(staleTail));
        Assert.Equal(123_456, document.LoadSlot(reference).Gil);
    }

    [Fact]
    public void InvalidCountsGenericTreesAndDuplicateKeysAreRejected()
    {
        var negative = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(negative, 2);
        BinaryPrimitives.WriteInt32LittleEndian(negative.AsSpan(4), -1);
        Assert.Throws<SaveFormatException>(() => MemoriaCodec.Parse(negative));

        var emptyDictionary = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(emptyDictionary, 2);
        Assert.False(MemoriaCodec.LooksLikeSave(emptyDictionary));

        using var duplicate = new MemoryStream();
        duplicate.Write(new byte[] { 2, 0, 0, 0, 2, 0, 0, 0 });
        foreach (var value in new[] { 1, 2 })
        {
            duplicate.Write(new byte[] { 1, (byte)'x', 4, 0, 0, 0 });
            var number = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(number, value); duplicate.Write(number);
        }
        Assert.Throws<SaveFormatException>(() => MemoriaCodec.Parse(duplicate.ToArray()));

        var overflowingStringLength = new byte[] { 3, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F };
        Assert.Throws<SaveFormatException>(() => MemoriaCodec.Parse(overflowingStringLength));
        Assert.False(MemoriaCodec.LooksLikeSave(overflowingStringLength));
    }

    [Fact]
    public void GilClampsAndLoadedSlotStaysDetachedUntilCommit()
    {
        var bytes = MemoriaCodec.Serialize(TestSaveFactory.CreateMemoriaTree());
        var document = SaveDocument.Parse("SavedData_ww_Memoria_0_0.dat", bytes);
        var reference = document.ListSlots()[0];
        var loaded = document.LoadSlot(reference);
        loaded.Gil = 99_999_999;
        Assert.Equal(9_999_999, loaded.Gil);
        Assert.Equal(500, SaveDocument.Parse("same.dat", document.ToArray()).LoadSlot(reference).Gil);
        document.CommitSlot(reference, loaded);
        Assert.Equal(9_999_999, SaveDocument.Parse("edited.dat", document.ToArray()).LoadSlot(reference).Gil);
    }

    [Fact]
    public void AllVanillaItemsAtMaximumQuantityKeepTheirOwnIdsAndNames()
    {
        var root = TestSaveFactory.CreateMemoriaTree();
        var entries = Enumerable.Range(0, 255).Select(itemId => MemoriaValue.Dictionary([
            new("id", MemoriaValue.Int32(itemId)),
            new("count", MemoriaValue.Int32(99)),
        ]));
        root.Require("40000_Common").Set("items", MemoriaValue.Array(entries));

        var items = new MemoriaSlot(root).Items();

        Assert.Equal(255, items.Count);
        Assert.Equal(Enumerable.Range(0, 255), items.Select(item => item.ItemId));
        Assert.Equal("Hammer", items[0].Name);
        Assert.Equal("Dragon Wrist", items[99].Name);
        Assert.Equal("Potion", items[236].Name);
    }

    [Fact]
    public void ModdedItemIdsAreNeverNarrowedToVanillaBytes()
    {
        var root = TestSaveFactory.CreateMemoriaTree();
        root.Require("40000_Common").Set("items", MemoriaValue.Array([
            Item(0x063, 99),
            Item(0x163, 99),
            Item(0x263, 99),
        ]));

        var slot = new MemoriaSlot(MemoriaCodec.Parse(MemoriaCodec.Serialize(root)));
        var items = slot.Items();

        Assert.Equal([0x063, 0x163, 0x263], items.Select(item => item.ItemId));
        Assert.Equal(["Dragon Wrist", "0x163", "0x263"], items.Select(item => item.Name));

        slot.SetItem(0x363, 12);
        Assert.Equal(12, slot.Items().Single(item => item.ItemId == 0x363).Quantity);
    }

    private static MemoriaValue Item(int id, int count) => MemoriaValue.Dictionary([
        new("id", MemoriaValue.Int32(id)),
        new("count", MemoriaValue.Int32(count)),
    ]);
}
