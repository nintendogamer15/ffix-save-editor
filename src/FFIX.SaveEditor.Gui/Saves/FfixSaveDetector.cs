// SPDX-License-Identifier: MIT
using SaveEditor.Ui.Codecs;

namespace FFIX.SaveEditor.Gui.Saves;

/// <summary>Performs only fixed-prefix work; encrypted/container inputs are settled by decode.</summary>
public sealed class FfixSaveDetector : ISaveCodecDetector
{
    private static readonly byte[] LegacyBlock = "SC"u8.ToArray();
    private static readonly byte[] LegacyCard = "MC"u8.ToArray();

    public SaveFormatDescriptor Format { get; } = new FfixSaveCodec().Format;
    public int HeaderBytesRequired => 4;

    public DetectionVerdict Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(LegacyBlock) || header.StartsWith(LegacyCard))
        {
            return DetectionVerdict.Confident;
        }

        // The rr2016 prefix is ciphertext, the 8,320-byte PS1 wrapper's distinguishing SC
        // bytes are outside this fixed prefix, and Memoria requires structure after its root
        // tag. Decode is the only safe discriminator for all three.
        return DetectionVerdict.RequiresDecode;
    }
}
