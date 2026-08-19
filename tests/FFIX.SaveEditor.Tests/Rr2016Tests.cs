using System.Buffers.Binary;
using FFIX.SaveEditor.Core;

namespace FFIX.SaveEditor.Tests;

public sealed class Rr2016Tests
{
    [Fact]
    public void VanillaKeyDerivationMatchesReference()
    {
        var (key, iv) = RrCrypto.DeriveKeyAndIv();
        Assert.Equal("10b45d06bbc66bedb11a2f44f1911e072e97ffc29ce44b92f97474a710b8a5d5", Convert.ToHexString(key).ToLowerInvariant());
        Assert.Equal("a83e99c0c895e1acaf11ccd982f9715f", Convert.ToHexString(iv).ToLowerInvariant());
    }

    [Fact]
    public void EmptyEncryptedSlotsAreNotListed()
    {
        var document = SaveDocument.Parse("SavedData_ww.dat", TestSaveFactory.CreateRrContainer());
        Assert.Equal(new[] { (0, 0) }, document.ListSlots().Select(x => (x.SlotId!.Value, x.SaveId!.Value)));
        Assert.Null(document.ProbeRrSlot(0, 1));
    }

    [Fact]
    public void MaxOutUpdatesBasisAndLiveHpMp()
    {
        var bytes = new byte[SaveLayout.RrSlotPlaintextSize];
        SaveLayout.RrOccupiedHeader.CopyTo(bytes);
        var character = new BinarySlot(bytes, SaveFormat.Rr2016).Character(0);
        character.MaxOut();
        Assert.Equal((9_999, 9_999, 9_999), (character.Get("cur_hp"), character.Get("max_hp"), character.Get("max_hp_base")));
        Assert.Equal((999, 999, 999), (character.Get("cur_mp"), character.Get("max_mp"), character.Get("max_mp_base")));
    }

    [Fact]
    public void CharacterNameIsEightBytesAndPreservesFollowingByte()
    {
        var bytes = new byte[SaveLayout.RrSlotPlaintextSize];
        var following = SaveLayout.RrCharacterStart + SaveLayout.RrCharacterNameOffset + SaveLayout.RrCharacterNameLength;
        bytes[following] = 0xA5;
        var character = new BinarySlot(bytes, SaveFormat.Rr2016).Character(0);
        character.Name = "123456789";
        Assert.Equal("12345678", character.Name);
        Assert.Equal(0xA5, bytes[following]);
    }

    [Fact]
    public void CardRecordLabelsUseCorrectOffsets()
    {
        var bytes = new byte[SaveLayout.RrSlotPlaintextSize];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(SaveLayout.RrCardWins.Offset), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(SaveLayout.RrCardLosses.Offset), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(SaveLayout.RrCardDraws.Offset), 1);
        Assert.Equal((3, 2, 1), new BinarySlot(bytes, SaveFormat.Rr2016).CardRecord);
    }

    [Fact]
    public void InventoryUsesCountThenItemIdAndWritesTheSameLayout()
    {
        var bytes = new byte[SaveLayout.RrSlotPlaintextSize];
        SaveLayout.RrOccupiedHeader.CopyTo(bytes);
        bytes[SaveLayout.RrItemStart] = 99;
        bytes[SaveLayout.RrItemStart + 1] = 236;
        bytes[SaveLayout.RrItemStart + 2] = 0;
        bytes[SaveLayout.RrItemStart + 3] = GameData.EmptyItemId;
        var slot = new BinarySlot(bytes, SaveFormat.Rr2016);

        var item = Assert.Single(slot.Items());
        Assert.Equal((236, 99, 0), (item.ItemId, item.Quantity, item.SlotIndex));
        Assert.Equal("Potion", item.Name);

        slot.RemoveItem(236);
        Assert.Equal(0, bytes[SaveLayout.RrItemStart]);
        Assert.Equal(GameData.EmptyItemId, bytes[SaveLayout.RrItemStart + 1]);
        slot.SetItem(29, 7);
        Assert.Equal(7, bytes[SaveLayout.RrItemStart]);
        Assert.Equal(29, bytes[SaveLayout.RrItemStart + 1]);
    }

    [Fact]
    public void EditReencryptsReopensAndPreservesChunkPadding()
    {
        var source = TestSaveFactory.CreateRrContainer();
        var cipherLength = RrCrypto.CipherSize(SaveLayout.RrSlotPlaintextSize);
        var marker = RrCrypto.ChunkOffset(0, 0) + cipherLength + 10;
        source[marker] = 0xA5;
        var document = SaveDocument.Parse("SavedData_ww.dat", source);
        var reference = document.ListSlots()[0];
        var slot = document.LoadSlot(reference);
        slot.Gil = 1_234_567;
        document.CommitSlot(reference, slot);
        var output = document.ToArray();
        Assert.Equal(0xA5, output[marker]);
        var reopened = SaveDocument.Parse("edited.dat", output);
        Assert.Equal(1_234_567, reopened.LoadSlot(reopened.ListSlots()[0]).Gil);
    }
}
