using System.Buffers.Binary;
using System.Security.Cryptography;
using FFIX.SaveEditor.Core;
using FFIX.SaveEditor.Gui.Editing;
using FFIX.SaveEditor.Gui.Saves;
using SaveEditor.Ui.Codecs;
using SaveEditor.Ui.Editing;
using SaveEditor.Ui.Interaction;
using SaveEditor.Ui.Workflow;

namespace FFIX.SaveEditor.Gui.Tests;

public sealed class WorkspaceTests
{
    [Theory]
    [InlineData(SaveFormat.Legacy)]
    [InlineData(SaveFormat.Rr2016)]
    [InlineData(SaveFormat.Memoria)]
    public async Task CommandMatrixCommitsThroughClonesAndSupportsUndoRedo(SaveFormat format)
    {
        await using Harness harness = await Harness.OpenAsync(format);
        FfixWorkspace workspace = harness.Workspace;
        byte[] original = harness.Session.Document!.ToArray();

        Assert.True(workspace.SetGil("7654321").Success);
        Assert.Equal(7_654_321, workspace.CurrentSlot!.Gil);

        IEditableCharacter character = workspace.CurrentCharacter!;
        Dictionary<string, string> numbers = new()
        {
            ["level"] = "12", ["exp"] = "3456", ["cur_hp"] = "777", ["max_hp"] = "888",
            ["cur_mp"] = "77", ["max_mp"] = "88", ["strength"] = "31", ["speed"] = "32",
            ["magic"] = "33", ["spirit"] = "34",
        };
        Dictionary<string, string> equipment = SaveLayout.EquipmentSlots
            .Where(character.Has)
            .ToDictionary(field => field, _ => "Potion");
        Assert.True(workspace.ApplyCharacter(new CharacterDraft("Dagger", numbers, equipment)).Success);
        Assert.Equal("Dagger", workspace.CurrentCharacter!.Name);
        Assert.Equal(12, workspace.CurrentCharacter.Get("level"));
        Assert.All(equipment.Keys, field => Assert.Equal(GameData.ResolveItemId("Potion"), workspace.CurrentCharacter.Get(field)));

        Assert.True(workspace.AddItem("0xEC", 17).Success);
        Assert.Contains(workspace.CurrentSlot.Items(), item => item.ItemId == 0xec && item.Quantity == 17);
        Assert.True(workspace.AddItem("236", 21).Success);
        Assert.True(workspace.AddItem("Potion", 25).Success);
        Assert.True(workspace.GiveAllItems(3).Success);
        Assert.True(workspace.MaxSelected().Success);
        Assert.Equal(99, workspace.CurrentCharacter.Get("level"));
        Assert.True(workspace.MaxAll().Success);
        Assert.NotEmpty(workspace.CurrentSlot.Items());
        _ = workspace.Overview;
        _ = workspace.CurrentSlot.Cards();

        if (format == SaveFormat.Legacy)
        {
            Assert.True(workspace.CanEditSupportAbilities);
            Assert.True(workspace.ApplySupportAbilities([0, 63]).Success);
            Assert.Equal([0, 63], workspace.CurrentCharacter.SupportAbilities());
        }
        else
        {
            byte[] beforeUnsupported = harness.Session.Document!.ToArray();
            int count = harness.History.Count;
            Assert.False(workspace.CanEditSupportAbilities);
            Assert.False(workspace.ApplySupportAbilities([0]).Success);
            Assert.Equal(beforeUnsupported, harness.Session.Document.ToArray());
            Assert.Equal(count, harness.History.Count);
        }

        byte[] edited = harness.Session.Document!.ToArray();
        Assert.NotEqual(original, edited);
        Assert.True(harness.History.IsDirty);
        harness.Session.Undo();
        Assert.NotEqual(edited, harness.Session.Document!.ToArray());
        harness.Session.Redo();
        Assert.Equal(edited, harness.Session.Document!.ToArray());
    }

    [Theory]
    [InlineData(SaveFormat.Legacy)]
    [InlineData(SaveFormat.Rr2016)]
    [InlineData(SaveFormat.Memoria)]
    public async Task ApplyInventoryAndCardsCommitThroughClonesAndSupportUndo(SaveFormat format)
    {
        await using Harness harness = await Harness.OpenAsync(format);
        FfixWorkspace workspace = harness.Workspace;
        byte[] original = harness.Session.Document!.ToArray();

        Assert.True(workspace.AddItem("Potion", 40).Success);
        InventoryItem potion = Assert.Single(workspace.CurrentSlot!.Items(), item => item.Name == "Potion");
        Assert.Equal(40, potion.Quantity);

        Assert.True(workspace.ApplyInventory([new InventoryRowDraft(potion.SlotIndex, "Potion", 10)]).Success);
        InventoryItem lowered = Assert.Single(workspace.CurrentSlot.Items(), item => item.SlotIndex == potion.SlotIndex);
        Assert.Equal((GameData.ResolveItemId("Potion"), 10), (lowered.ItemId, lowered.Quantity));

        Assert.True(workspace.ApplyInventory([new InventoryRowDraft(lowered.SlotIndex, "Ragnarok", 5)]).Success);
        InventoryItem swapped = Assert.Single(workspace.CurrentSlot.Items(), item => item.SlotIndex == lowered.SlotIndex);
        Assert.Equal((29, 5, "Ragnarok"), (swapped.ItemId, swapped.Quantity, swapped.Name));

        Assert.True(workspace.ApplyInventory([new InventoryRowDraft(swapped.SlotIndex, "Ragnarok", 0)]).Success);
        Assert.DoesNotContain(workspace.CurrentSlot.Items(), item => item.SlotIndex == swapped.SlotIndex);
        Assert.DoesNotContain(workspace.CurrentSlot.Items(), item => item.ItemId == 29);

        Assert.False(workspace.ApplyInventory([new InventoryRowDraft(0, "   ", 3)]).Success);

        Assert.True(workspace.ApplyCard(0, 1, 0xFF, 42, 3, 11, 22).Success);
        CardInfo card = workspace.CurrentSlot.Cards().Single(entry => entry.Index == 0);
        Assert.Equal(1, card.TypeId);
        Assert.Equal("Fang", card.TypeName);
        Assert.Equal(0xFF, card.Arrows);
        Assert.Equal(42, card.Attack);
        Assert.Equal(3, card.AttackType);
        Assert.Equal('A', card.AttackTypeName);
        Assert.Equal(11, card.PhysicalDefense);
        Assert.Equal(22, card.MagicDefense);

        Assert.True(workspace.SetCardRecord(12, 34, 56).Success);
        Assert.Equal((12, 34, 56), workspace.CurrentSlot.CardRecord);

        byte[] edited = harness.Session.Document!.ToArray();
        Assert.NotEqual(original, edited);
        Assert.True(harness.History.IsDirty);
        harness.Session.Undo();
        Assert.NotEqual(edited, harness.Session.Document!.ToArray());
        harness.Session.Redo();
        Assert.Equal(edited, harness.Session.Document!.ToArray());
    }

    [Fact]
    public async Task InvalidEditLeavesAllBytesAndHistoryUnchangedAndPendingIsReported()
    {
        await using Harness harness = await Harness.OpenAsync(SaveFormat.Legacy);
        byte[] before = harness.Session.Document!.ToArray();
        int historyBefore = harness.History.Count;

        Assert.False(harness.Workspace.SetGil("10000000").Success);
        Assert.False(harness.Workspace.ApplyCharacter(new CharacterDraft(
            "Bad", new Dictionary<string, string> { ["level"] = "-1" }, new Dictionary<string, string>())).Success);
        Assert.Equal(before, harness.Session.Document.ToArray());
        Assert.Equal(historyBefore, harness.History.Count);

        harness.Workspace.SetPendingEdits(true);
        Assert.True(harness.Session.HasPendingEdits);
    }

    [Fact]
    public async Task SaveAsLeavesOriginalHashAndOverwriteProducesExactBackup()
    {
        await using Harness harness = await Harness.OpenAsync(SaveFormat.Legacy);
        byte[] original = await File.ReadAllBytesAsync(harness.Path);
        string originalHash = Convert.ToHexString(SHA256.HashData(original));
        Assert.True(harness.Workspace.SetGil("222222").Success);

        string copy = Path.Combine(harness.Directory, "edited.ps1");
        harness.Interaction.SavePath = copy;
        await harness.Session.SaveAsAsync();
        Assert.True(File.Exists(copy));
        Assert.Equal(originalHash, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(harness.Path))));

        byte[] beforeOverwrite = await File.ReadAllBytesAsync(copy);
        Assert.True(harness.Workspace.SetGil("333333").Success);
        await harness.Session.OverwriteWithBackupAsync();
        Assert.True(harness.Session.LastOutcome!.IsSuccess);
        Assert.NotNull(harness.Session.LastBackupPath);
        Assert.Equal(beforeOverwrite, await File.ReadAllBytesAsync(harness.Session.LastBackupPath!));
    }

    [Fact]
    public async Task OverwriteRefusesExternallyChangedRetainedFile()
    {
        await using Harness harness = await Harness.OpenAsync(SaveFormat.Legacy);
        Assert.True(harness.Workspace.SetGil("222222").Success);

        byte[] externallyChanged = await File.ReadAllBytesAsync(harness.Path);
        externallyChanged[500] ^= 0x5a;
        await File.WriteAllBytesAsync(harness.Path, externallyChanged);

        await harness.Session.OverwriteWithBackupAsync();

        Assert.False(harness.Session.LastOutcome!.IsSuccess);
        Assert.Equal(SaveFailureReason.ExternalChange, harness.Session.LastOutcome.Reason);
        Assert.Equal(externallyChanged, await File.ReadAllBytesAsync(harness.Path));
    }

    [Fact]
    public async Task LegacyMultiSlotSelectionEditsOnlyTheChosenBlock()
    {
        byte[] card = new byte[SaveLayout.LegacyCardSize];
        "MC"u8.CopyTo(card);
        SetDirectoryEntry(card, 1, "BASLUS-0125100000-00"u8);
        SetDirectoryEntry(card, 2, "BASLUS-0125100000-01"u8);
        byte[] first = CreateLegacyBlock();
        byte[] second = CreateLegacyBlock();
        new BinarySlot(second, SaveFormat.Legacy).Gil = 444;
        LegacyChecksum.Repair(second);
        first.CopyTo(card, SaveLayout.LegacyBlockSize);
        second.CopyTo(card, SaveLayout.LegacyBlockSize * 2);

        await using Harness harness = await Harness.OpenBytesAsync(SaveFormat.Legacy, card);
        Assert.Equal(2, harness.Workspace.Slots.Count);
        int firstGil = harness.Session.Document!.LoadSlot(harness.Workspace.Slots[0]).Gil;
        harness.Workspace.SelectSlot(1);
        Assert.True(harness.Workspace.SetGil("888888").Success);

        Assert.Equal(firstGil, harness.Session.Document.LoadSlot(harness.Workspace.Slots[0]).Gil);
        Assert.Equal(888_888, harness.Session.Document.LoadSlot(harness.Workspace.Slots[1]).Gil);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(string directory, string path, TestInteraction interaction,
            EditHistory history, DocumentSession<SaveDocument> session, FfixWorkspace workspace)
        {
            Directory = directory;
            Path = path;
            Interaction = interaction;
            History = history;
            Session = session;
            Workspace = workspace;
        }

        public string Directory { get; }
        public string Path { get; }
        public TestInteraction Interaction { get; }
        public EditHistory History { get; }
        public DocumentSession<SaveDocument> Session { get; }
        public FfixWorkspace Workspace { get; }

        public static async Task<Harness> OpenAsync(SaveFormat format)
        {
            return await OpenBytesAsync(format, format switch
            {
                SaveFormat.Legacy => CreateLegacyBlock(),
                SaveFormat.Rr2016 => CreateRrContainerAt(2, 3),
                _ => CreateMemoria(),
            });
        }

        public static async Task<Harness> OpenBytesAsync(SaveFormat format, byte[] contents)
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ffix-gui-tests-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            string path = System.IO.Path.Combine(directory, format switch
            {
                SaveFormat.Legacy => "save.ps1",
                SaveFormat.Rr2016 => "SavedData_ww.dat",
                _ => "SavedData_ww_Memoria_0_0.dat",
            });
            await File.WriteAllBytesAsync(path, contents);

            TestInteraction interaction = new();
            FfixSaveCodec codec = new();
            SaveCodecRegistry<SaveDocument> registry = new(
            [new CodecRegistration<SaveDocument>(new FfixSaveDetector(), codec)]);
            SafeFileWorkflow<SaveDocument> workflow = new(new SafeFileWorkflowOptions<SaveDocument>
            {
                Registry = registry,
                Interaction = interaction,
                DocumentComparer = FfixDocumentComparer.Instance,
                MaxBytes = FfixSaveCodec.MaximumBytes,
                ConfirmAboveBytes = FfixSaveCodec.MaximumBytes,
                MaxSerializedBytes = FfixSaveCodec.MaximumBytes,
            });
            EditHistory history = new();
            DocumentSession<SaveDocument> session = new(workflow, history, codec);
            FfixWorkspace workspace = new(session, history);
            session.PendingEditProbe = () => workspace.HasPendingEdits;
            session.DocumentChanged += (_, _) => workspace.BindDocument();
            await session.OpenAsync(path);
            Assert.True(session.HasDocument, session.LastStatusMessage);
            return new Harness(directory, path, interaction, history, session, workspace);
        }

        public ValueTask DisposeAsync()
        {
            Session.Dispose();
            System.IO.Directory.Delete(Directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestInteraction : IUserInteraction
    {
        public string? SavePath { get; set; }
        public ValueTask<string?> PickOpenFileAsync(FilePickerRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
        public ValueTask<SaveFilePickResult?> PickSaveFileAsync(FilePickerRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<SaveFilePickResult?>(SavePath is null ? null : new(SavePath, false));
        public ValueTask<string?> PickFolderAsync(string title, string? suggestedDirectory = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
        public ValueTask<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask ShowMessageAsync(MessageRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<string?> ChooseAsync(ChoicePrompt prompt, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(prompt.Options.FirstOrDefault()?.Key);
        public ValueTask ShowDocumentAsync(DocumentRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
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

    private static void SetDirectoryEntry(byte[] card, int blockIndex, ReadOnlySpan<byte> product)
    {
        byte[] header = new byte[SaveLayout.LegacyBlockHeaderSize];
        header[0] = 0x51;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(5), SaveLayout.LegacyBlockSize);
        header[8] = header[9] = 0xff;
        product.CopyTo(header.AsSpan(SaveLayout.LegacyRegionCodeOffset));
        byte checksum = 0;
        foreach (byte value in header.AsSpan(0, header.Length - 1)) checksum ^= value;
        header[^1] = checksum;
        header.CopyTo(card, blockIndex * SaveLayout.LegacyBlockHeaderSize);
    }

    private static byte[] CreateRrContainerAt(int slotId, int saveId)
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
        RrCrypto.Encrypt(bytes).CopyTo(container, RrCrypto.ChunkOffset(slotId, saveId));
        return container;
    }

    private static byte[] CreateMemoria()
    {
        MemoriaValue player = MemoriaValue.Dictionary([
            new("name", MemoriaValue.String("Zidane")), new("level", MemoriaValue.Int32(1)), new("exp", MemoriaValue.Int32(0)),
            new("cur", MemoriaValue.Dictionary([new("hp", MemoriaValue.Int32(105)), new("mp", MemoriaValue.Int32(36))])),
            new("max", MemoriaValue.Dictionary([new("hp", MemoriaValue.Int32(105)), new("mp", MemoriaValue.Int32(36))])),
            new("basis", MemoriaValue.Dictionary([new("max_hp", MemoriaValue.Int32(105)), new("max_mp", MemoriaValue.Int32(36)), new("dex", MemoriaValue.Int32(23)), new("str", MemoriaValue.Int32(21)), new("mgc", MemoriaValue.Int32(18)), new("wpr", MemoriaValue.Int32(23))])),
            new("elem", MemoriaValue.Dictionary([new("dex", MemoriaValue.Int32(23)), new("str", MemoriaValue.Int32(21)), new("mgc", MemoriaValue.Int32(18)), new("wpr", MemoriaValue.Int32(23))])),
            new("trance", MemoriaValue.Int32(0)), new("equip", MemoriaValue.Array([MemoriaValue.Int32(1), MemoriaValue.Int32(112), MemoriaValue.Int32(88), MemoriaValue.Int32(149), MemoriaValue.Int32(255)])),
        ]);
        return MemoriaCodec.Serialize(MemoriaValue.Dictionary([
            new("40000_Common", MemoriaValue.Dictionary([new("gil", MemoriaValue.Int32(500)), new("items", MemoriaValue.Array()), new("players", MemoriaValue.Array([player]))])),
            new("30000_MiniGame", MemoriaValue.Dictionary([new("MiniGameCard", MemoriaValue.Array()), new("sWin", MemoriaValue.Int32(0)), new("sLose", MemoriaValue.Int32(0)), new("sDraw", MemoriaValue.Int32(0))])),
        ]));
    }
}
