using System.Buffers.Binary;
using FFIX.SaveEditor.Core;

namespace FFIX.SaveEditor.Tests;

public sealed class LegacyTests
{
    [Fact]
    public void GilUpdatesGameplayPreviewAndChecksum()
    {
        var block = TestSaveFactory.CreateLegacyBlock();
        var slot = new BinarySlot(block, SaveFormat.Legacy);
        slot.Gil = 7_654_321;
        slot.FinalizeEdits();
        Assert.Equal(7_654_321, TestSaveFactory.ReadUInt(block, SaveLayout.LegacyGil));
        Assert.Equal(7_654_321, TestSaveFactory.ReadUInt(block, SaveLayout.LegacyPreviewGil));
        Assert.Equal(LegacyChecksum.Calculate(block), BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(SaveLayout.LegacyChecksumOffset)));
    }

    [Fact]
    public void FullCardListsOnlyFfixBlocks()
    {
        var card = new byte[SaveLayout.LegacyCardSize];
        "MC"u8.CopyTo(card);
        TestSaveFactory.SetDirectoryEntry(card, 1, "BASLUS-0125100000-00"u8);
        TestSaveFactory.SetDirectoryEntry(card, 2, "BASLUS-9999900000-00"u8);
        TestSaveFactory.CreateLegacyBlock().CopyTo(card, SaveLayout.LegacyBlockSize);
        TestSaveFactory.CreateLegacyBlock(name: "Other").CopyTo(card, SaveLayout.LegacyBlockSize * 2);
        var document = SaveDocument.Parse("card.mcr", card);
        Assert.Equal(new int?[] { 1 }, document.ListSlots().Select(x => x.BlockIndex));
    }

    [Theory]
    [InlineData("BASLUS-0125100000-00", 60)]
    [InlineData("BESLES-0296500000-00", 50)]
    public void RegionalFrameRateConvertsPlaytime(string product, int frameRate)
    {
        var card = new byte[SaveLayout.LegacyCardSize];
        "MC"u8.CopyTo(card);
        TestSaveFactory.SetDirectoryEntry(card, 1, System.Text.Encoding.ASCII.GetBytes(product));
        var block = TestSaveFactory.CreateLegacyBlock();
        TestSaveFactory.WriteUInt(block, SaveLayout.LegacyPlaytime, frameRate * 60 * 60);
        block.CopyTo(card, SaveLayout.LegacyBlockSize);
        var document = SaveDocument.Parse("card.mcr", card);
        Assert.Equal(3_600, document.LoadSlot(document.ListSlots()[0]).PlaytimeSeconds);
    }

    [Fact]
    public void WrongSizedLegacyExtensionIsRejected()
    {
        Assert.Throws<SaveFormatException>(() => SaveDocument.Parse("broken.mcr", "not a save"u8));
    }

    [Fact]
    public void SimpleWrapperAndUnknownBytesArePreserved()
    {
        var header = Enumerable.Repeat((byte)0xA5, SaveLayout.LegacyBlockHeaderSize).ToArray();
        var block = TestSaveFactory.CreateLegacyBlock();
        var wrapped = header.Concat(block).ToArray();
        var document = SaveDocument.Parse("save.mcs", wrapped);
        var reference = document.ListSlots()[0];
        var slot = document.LoadSlot(reference);
        slot.Gil = 999;
        document.CommitSlot(reference, slot);
        Assert.Equal(header, document.ToArray()[..SaveLayout.LegacyBlockHeaderSize]);
    }

    [Fact]
    public void SupportAbilityBitsRoundTripAndCommitRepairsChecksum()
    {
        var document = SaveDocument.Parse("save.ps1", TestSaveFactory.CreateLegacyBlock());
        var reference = document.ListSlots()[0];
        var slot = document.LoadSlot(reference);
        var character = slot.Character(0);
        character.SetSupportAbility(0, true);
        character.SetSupportAbility(63, true);
        document.CommitSlot(reference, slot);

        var reopened = SaveDocument.Parse("edited.ps1", document.ToArray());
        var output = reopened.ToArray();
        Assert.Equal(new[] { 0, 63 }, reopened.LoadSlot(reopened.ListSlots()[0]).Character(0).SupportAbilities());
        Assert.Equal(LegacyChecksum.Calculate(output),
            BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(SaveLayout.LegacyChecksumOffset)));
    }

    [Fact]
    public void InventoryRetainsThePs1ItemIdThenCountLayout()
    {
        var bytes = TestSaveFactory.CreateLegacyBlock();
        bytes[SaveLayout.LegacyItemStart] = 236;
        bytes[SaveLayout.LegacyItemStart + 1] = 40;
        var slot = new BinarySlot(bytes, SaveFormat.Legacy);

        var item = Assert.Single(slot.Items());
        Assert.Equal((236, 40, 0), (item.ItemId, item.Quantity, item.SlotIndex));
        Assert.Equal("Potion", item.Name);

        slot.RemoveItem(236);
        Assert.Equal(GameData.EmptyItemId, bytes[SaveLayout.LegacyItemStart]);
        Assert.Equal(0, bytes[SaveLayout.LegacyItemStart + 1]);
    }
}
