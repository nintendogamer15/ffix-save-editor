// SPDX-License-Identifier: MIT
using FFIX.SaveEditor.Core;
using SaveEditor.Ui.Codecs;

namespace FFIX.SaveEditor.Gui.Saves;

public sealed class FfixSaveCodec : ISaveCodec<SaveDocument>
{
    public const int MaximumBytes = 16 * 1024 * 1024;

    public SaveFormatDescriptor Format { get; } = new(
        "ffix.save",
        "Final Fantasy IX save",
        ["dat", "sav", "mcr", "mcd", "bin", "mc", "mci", "ps", "psm", "dff", "ps1", "mcs"]);

    public bool PreservesUnknownData => true;

    public async ValueTask<SaveDocument> DecodeAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        using MemoryStream buffer = new();
        byte[] chunk = new byte[64 * 1024];
        while (true)
        {
            int read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length > MaximumBytes - read)
            {
                throw new SaveFormatException($"Save exceeds the {MaximumBytes / (1024 * 1024)} MiB input limit.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        ReadOnlySpan<byte> content = buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length));
        if (RequiresMemoriaPreflight(content))
        {
            MemoriaPreflight.Validate(content);
        }

        // Core remains the only format detector/parser. The synthetic name is used only for
        // Core's extension-specific diagnostics; every supported format has a byte signature
        // or fixed size and therefore parses independently of it.
        SaveDocument document = SaveDocument.Parse("opened-save.dat", content);
        if (document.ListSlots().Count == 0)
        {
            throw new SaveFormatException("The file contains no occupied FFIX save slots.");
        }

        return document;
    }

    public async ValueTask SerializeAsync(
        SaveDocument document,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        byte[] bytes = document.ToArray();
        if (bytes.Length > MaximumBytes)
        {
            throw new SaveFormatException($"Serialized save exceeds the {MaximumBytes / (1024 * 1024)} MiB limit.");
        }

        await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<ValidationReport> ValidateAsync(
        SaveDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (document.ListSlots().Count == 0)
        {
            return ValueTask.FromResult(new ValidationReport
            {
                Messages =
                [
                    new ValidationMessage(
                        ValidationSeverity.Error,
                        new global::SaveEditor.Ui.Interaction.UntrustedText("The document contains no occupied FFIX save slots.")),
                ],
            });
        }

        return ValueTask.FromResult(ValidationReport.Empty);
    }

    internal static bool RequiresMemoriaPreflight(ReadOnlySpan<byte> content)
    {
        // Core gives its fixed-size formats precedence over Memoria framing. Preserve that
        // boundary here, then preflight either recursive root tag before Core can traverse it.
        if (content.Length is SaveLayout.RrContainerSize or SaveLayout.LegacyCardSize or SaveLayout.LegacyBlockSize ||
            content.Length == SaveLayout.LegacyBlockHeaderSize + SaveLayout.LegacyBlockSize ||
            content.Length < sizeof(int))
        {
            return false;
        }

        int rootTag = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(content);
        return rootTag is (int)MemoriaValueKind.Dictionary or (int)MemoriaValueKind.Array;
    }
}
