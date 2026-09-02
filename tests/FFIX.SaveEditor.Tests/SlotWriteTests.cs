using FFIX.SaveEditor.Core;

namespace FFIX.SaveEditor.Tests;

public sealed class SlotWriteTests
{
    [Theory]
    [InlineData(SaveFormat.Legacy)]
    [InlineData(SaveFormat.Rr2016)]
    [InlineData(SaveFormat.Memoria)]
    public void ReplaceItemAtWritesExactCountAndClearsWhileSetItemStillRaises(SaveFormat format)
    {
        var slot = CreateSlot(format);
        Assert.True(slot.SetItem(236, 40));
        Assert.True(slot.SetItem(236, 10));
        var raised = Assert.Single(slot.Items(), item => item.ItemId == 236);
        Assert.Equal(40, raised.Quantity);

        Assert.True(slot.ReplaceItemAt(raised.SlotIndex, 236, 10));
        var lowered = Assert.Single(slot.Items(), item => item.ItemId == 236);
        Assert.Equal(10, lowered.Quantity);
        Assert.Equal(raised.SlotIndex, lowered.SlotIndex);

        Assert.True(slot.ReplaceItemAt(lowered.SlotIndex, 29, 5));
        var swapped = Assert.Single(slot.Items(), item => item.SlotIndex == lowered.SlotIndex);
        Assert.Equal((29, 5), (swapped.ItemId, swapped.Quantity));
        Assert.Equal("Ragnarok", swapped.Name);

        Assert.True(slot.ReplaceItemAt(swapped.SlotIndex, 29, 0));
        Assert.DoesNotContain(slot.Items(), item => item.SlotIndex == swapped.SlotIndex);
        Assert.DoesNotContain(slot.Items(), item => item.ItemId == 29);
    }

    [Theory]
    [InlineData(SaveFormat.Legacy)]
    [InlineData(SaveFormat.Rr2016)]
    [InlineData(SaveFormat.Memoria)]
    public void SetCardRoundTripsSpeciesArrowsAttackAndClassA(SaveFormat format)
    {
        var slot = CreateSlot(format);
        slot.SetCard(0, 1, 0xFF, 42, 3, 11, 22);
        var card = slot.Cards().Single(entry => entry.Index == 0);
        Assert.Equal(1, card.TypeId);
        Assert.Equal("Fang", card.TypeName);
        Assert.Equal(0xFF, card.Arrows);
        Assert.Equal(42, card.Attack);
        Assert.Equal(3, card.AttackType);
        Assert.Equal('A', card.AttackTypeName);
        Assert.Equal(11, card.PhysicalDefense);
        Assert.Equal(22, card.MagicDefense);
    }

    [Fact]
    public void MemoriaFixtureCardsAreGoblinFangAndSkeleton()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "SavedData_ww_Memoria_0_0.dat");
        var slot = new MemoriaSlot(MemoriaCodec.Parse(File.ReadAllBytes(path)));
        Assert.Equal(["Goblin", "Fang", "Skeleton"], slot.Cards().Select(card => card.TypeName));
        Assert.Equal(new byte[] { 0, 1, 2 }, slot.Cards().Select(card => card.TypeId));
        Assert.Equal(new[] { 0, 1, 2 }, slot.Cards().Select(card => card.Index));
        Assert.All(slot.Cards(), card => Assert.Equal('P', card.AttackTypeName));
    }

    [Theory]
    [InlineData(SaveFormat.Legacy)]
    [InlineData(SaveFormat.Rr2016)]
    [InlineData(SaveFormat.Memoria)]
    public void SetCardRecordRoundTripsWinsLossesAndDraws(SaveFormat format)
    {
        var slot = CreateSlot(format);
        slot.SetCardRecord(12, 34, 56);
        Assert.Equal((12, 34, 56), slot.CardRecord);
    }

    [Theory]
    [InlineData(SaveFormat.Legacy)]
    [InlineData(SaveFormat.Rr2016)]
    [InlineData(SaveFormat.Memoria)]
    public void SetCardFillsTheFirstEmptySlotOrAppends(SaveFormat format)
    {
        var slot = CreateSlot(format);
        if (format is SaveFormat.Legacy or SaveFormat.Rr2016)
        {
            slot.SetCard(0, GameData.NoCardType, 0, 0, 0, 0, 0);
            Assert.DoesNotContain(slot.Cards(), card => card.Index == 0);
            slot.SetCard(0, 2, 0xAA, 7, 1, 8, 9);
            var filled = slot.Cards().Single(card => card.Index == 0);
            Assert.Equal(("Skeleton", 'M', 0xAA, 7, 8, 9),
                (filled.TypeName, filled.AttackTypeName, filled.Arrows, filled.Attack, filled.PhysicalDefense, filled.MagicDefense));
            return;
        }

        var index = slot.CardSlotCount;
        slot.SetCard(index, 2, 0xAA, 7, 1, 8, 9);
        Assert.Equal(index + 1, slot.CardSlotCount);
        var appended = slot.Cards().Single(card => card.Index == index);
        Assert.Equal(("Skeleton", 'M'), (appended.TypeName, appended.AttackTypeName));
    }

    [Fact]
    public void MemoriaSetCardPreservesSideAndCpoint()
    {
        var root = TestSaveFactory.CreateMemoriaTree();
        var cards = root.Require("30000_MiniGame").Require("MiniGameCard");
        cards.ArrayItems.Add(MemoriaValue.Dictionary([
            new("id", MemoriaValue.Int32(0)),
            new("type", MemoriaValue.Int32(0)),
            new("atk", MemoriaValue.Int32(1)),
            new("pdef", MemoriaValue.Int32(2)),
            new("mdef", MemoriaValue.Int32(3)),
            new("arrow", MemoriaValue.Int32(4)),
            new("side", MemoriaValue.Int32(7)),
            new("cpoint", MemoriaValue.Int32(20)),
        ]));
        var slot = new MemoriaSlot(root);
        slot.SetCard(0, 1, 0xFF, 9, 3, 5, 6);
        var entry = cards.ArrayItems[0];
        Assert.Equal(1, entry.Require("id").AsInt());
        Assert.Equal(3, entry.Require("type").AsInt());
        Assert.Equal(7, entry.Require("side").AsInt());
        Assert.Equal(20, entry.Require("cpoint").AsInt());
        Assert.Equal('A', slot.Cards()[0].AttackTypeName);
        Assert.Equal("Fang", slot.Cards()[0].TypeName);
    }

    private static IEditableSlot CreateSlot(SaveFormat format) => format switch
    {
        SaveFormat.Legacy => new BinarySlot(TestSaveFactory.CreateLegacyBlock(), SaveFormat.Legacy),
        SaveFormat.Rr2016 => CreateRrSlot(),
        SaveFormat.Memoria => new MemoriaSlot(TestSaveFactory.CreateMemoriaTree()),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static BinarySlot CreateRrSlot()
    {
        var bytes = new byte[SaveLayout.RrSlotPlaintextSize];
        SaveLayout.RrOccupiedHeader.CopyTo(bytes);
        return new BinarySlot(bytes, SaveFormat.Rr2016);
    }
}
