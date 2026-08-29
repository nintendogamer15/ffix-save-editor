using System.Buffers.Binary;
using FFIX.SaveEditor.Core;
using FFIX.SaveEditor.Gui.Saves;
using SaveEditor.Ui.Codecs;
using SaveEditor.Ui.Editing;
using SaveEditor.Ui.Interaction;
using SaveEditor.Ui.Settings;
using SaveEditor.Ui.Shell;
using SaveEditor.Ui.Workflow;

namespace FFIX.SaveEditor.Gui.Tests;

public sealed class OpenFailureTests
{
    [Fact]
    public async Task ArrayRootWithExtremeNestingFailsBeforeCoreAndProcessRemainsResponsive()
    {
        byte[] hostile = ArrayRootNestedArrays(200_000);
        Assert.Equal(1_600_008, hostile.Length);

        SaveFormatException exception = await Assert.ThrowsAsync<SaveFormatException>(async () =>
            await new FfixSaveCodec().DecodeAsync(new MemoryStream(hostile)));

        Assert.Contains("depth", exception.Message, StringComparison.OrdinalIgnoreCase);

        using MemoryStream harmless = new([42]);
        Assert.Equal(42, harmless.ReadByte());
    }

    [Fact]
    public async Task InvalidOversizedDeepZeroSlotAndSymlinkInputsFailThroughShellWithoutAdoption()
    {
        string root = Path.Combine(Path.GetTempPath(), "ffix-open-failures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string invalid = Path.Combine(root, "invalid.dat");
            string oversized = Path.Combine(root, "oversized.dat");
            string deep = Path.Combine(root, "deep.dat");
            string zero = Path.Combine(root, "empty.mcr");
            await File.WriteAllBytesAsync(invalid, "not a save"u8.ToArray());
            await File.WriteAllBytesAsync(oversized, new byte[FfixSaveCodec.MaximumBytes + 1]);
            await File.WriteAllBytesAsync(deep, NestedArrays(129));
            byte[] emptyCard = new byte[SaveLayout.LegacyCardSize];
            "MC"u8.CopyTo(emptyCard);
            await File.WriteAllBytesAsync(zero, emptyCard);

            using TestComposition composition = new(root);
            await composition.Shell.InitializeAsync();
            foreach (string path in new[] { invalid, oversized, deep, zero })
            {
                await composition.Shell.OpenPathAsync(path);
                Assert.False(composition.Session.HasDocument, $"Unexpectedly adopted {path}: {composition.Shell.StatusMessage}");
                Assert.Null(composition.Session.Document);
            }

            string link = Path.Combine(root, "linked.mcr");
            File.CreateSymbolicLink(link, zero);
            await composition.Shell.OpenPathAsync(link);
            Assert.False(composition.Session.HasDocument);
            Assert.Equal(SaveFailureReason.PathRefused, composition.Session.LastOutcome?.Reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestComposition : IDisposable
    {
        public TestComposition(string root)
        {
            FfixSaveCodec codec = new();
            Interaction = new NoInteraction();
            SaveCodecRegistry<SaveDocument> registry = new(
            [new CodecRegistration<SaveDocument>(new FfixSaveDetector(), codec)]);
            SafeFileWorkflow<SaveDocument> workflow = new(new SafeFileWorkflowOptions<SaveDocument>
            {
                Registry = registry,
                Interaction = Interaction,
                DocumentComparer = FfixDocumentComparer.Instance,
                MaxBytes = FfixSaveCodec.MaximumBytes,
                ConfirmAboveBytes = FfixSaveCodec.MaximumBytes,
                MaxSerializedBytes = FfixSaveCodec.MaximumBytes,
            });
            Session = new DocumentSession<SaveDocument>(workflow, new EditHistory(), codec);
            EditorSettingsStore settings = new(
                EditorApplicationId.Parse("FFIXOpenFailureTests"),
                new EditorSettingsStoreOptions { BaseDirectory = Path.Combine(root, "settings") });
            Shell = new EditorShellViewModel(Session, Interaction, settings);
        }

        public NoInteraction Interaction { get; }
        public DocumentSession<SaveDocument> Session { get; }
        public EditorShellViewModel Shell { get; }
        public void Dispose() { Shell.Dispose(); Session.Dispose(); }
    }

    private sealed class NoInteraction : IUserInteraction
    {
        public ValueTask<string?> PickOpenFileAsync(FilePickerRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
        public ValueTask<SaveFilePickResult?> PickSaveFileAsync(FilePickerRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult<SaveFilePickResult?>(null);
        public ValueTask<string?> PickFolderAsync(string title, string? suggestedDirectory = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
        public ValueTask<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask ShowMessageAsync(MessageRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<string?> ChooseAsync(ChoicePrompt prompt, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
        public ValueTask ShowDocumentAsync(DocumentRequest request, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
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

    private static byte[] ArrayRootNestedArrays(int depth)
    {
        using MemoryStream stream = new();
        for (int index = 0; index < depth; index++)
        {
            WriteInt(stream, (int)MemoriaValueKind.Array);
            WriteInt(stream, 1);
        }

        WriteInt(stream, (int)MemoriaValueKind.Int32);
        WriteInt(stream, 0);
        return stream.ToArray();
    }

    private static void WriteInt(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
