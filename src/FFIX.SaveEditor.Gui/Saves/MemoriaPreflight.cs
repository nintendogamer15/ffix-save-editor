// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.Text;
using FFIX.SaveEditor.Core;

namespace FFIX.SaveEditor.Gui.Saves;

/// <summary>
/// Iteratively checks the untrusted Memoria framing before Core's recursive reader sees it.
/// It deliberately validates only tags, counts, string framing and traversal bounds; Core
/// remains the sole authority for the save's meaning.
/// </summary>
internal static class MemoriaPreflight
{
    internal const int MaximumDepth = 128;
    internal const int MaximumValues = 250_000;
    internal const int MaximumCollectionEntries = 100_000;
    internal const int MaximumStringBytes = 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void Validate(ReadOnlySpan<byte> data)
    {
        Reader reader = new(data);
        List<Frame> stack = [];
        int values = 0;

        ReadValue(ref reader, 1, stack, ref values);
        while (stack.Count > 0)
        {
            int last = stack.Count - 1;
            Frame frame = stack[last];
            if (frame.Remaining == 0)
            {
                stack.RemoveAt(last);
                continue;
            }

            if (frame.Dictionary)
            {
                reader.ReadString();
            }

            stack[last] = frame with { Remaining = frame.Remaining - 1 };
            ReadValue(ref reader, checked(frame.Depth + 1), stack, ref values);
        }
    }

    private static void ReadValue(ref Reader reader, int depth, List<Frame> stack, ref int values)
    {
        if (depth > MaximumDepth)
        {
            throw new SaveFormatException($"Memoria nesting exceeds the supported depth of {MaximumDepth}.");
        }

        values = checked(values + 1);
        if (values > MaximumValues)
        {
            throw new SaveFormatException($"Memoria value count exceeds the supported limit of {MaximumValues:N0}.");
        }

        int tagOffset = reader.Position;
        int tag = reader.ReadInt32();
        switch (tag)
        {
            case (int)MemoriaValueKind.Array:
                PushCollection(ref reader, stack, depth, dictionary: false);
                break;
            case (int)MemoriaValueKind.Dictionary:
                PushCollection(ref reader, stack, depth, dictionary: true);
                break;
            case (int)MemoriaValueKind.String:
                reader.ReadString();
                break;
            case (int)MemoriaValueKind.Int32:
                reader.Take(4);
                break;
            case (int)MemoriaValueKind.Double:
                reader.Take(8);
                break;
            default:
                throw new SaveFormatException($"Unknown Memoria type tag {tag} at offset {tagOffset}.");
        }
    }

    private static void PushCollection(ref Reader reader, List<Frame> stack, int depth, bool dictionary)
    {
        int countOffset = reader.Position;
        int count = reader.ReadInt32();
        if (count < 0 || count > MaximumCollectionEntries)
        {
            throw new SaveFormatException(
                $"Invalid Memoria {(dictionary ? "dictionary" : "array")} count {count} at offset {countOffset}; " +
                $"the supported limit is {MaximumCollectionEntries:N0}.");
        }

        int minimumPerEntry = dictionary ? 5 : 4;
        int minimumBytes;
        try
        {
            minimumBytes = checked(count * minimumPerEntry);
        }
        catch (OverflowException exception)
        {
            throw new SaveFormatException("Memoria collection framing overflows its byte count.", exception);
        }

        if (minimumBytes > reader.Remaining)
        {
            throw new SaveFormatException($"Truncated Memoria collection at offset {countOffset}.");
        }

        if (count > 0)
        {
            stack.Add(new Frame(count, dictionary, depth));
        }
    }

    private readonly record struct Frame(int Remaining, bool Dictionary, int Depth);

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> content = data;
        public int Position { get; private set; }
        public int Remaining => content.Length - Position;

        public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

        public void ReadString()
        {
            int length = Read7BitLength();
            if (length > MaximumStringBytes)
            {
                throw new SaveFormatException(
                    $"Memoria UTF-8 string payload exceeds the supported limit of {MaximumStringBytes:N0} bytes.");
            }

            try
            {
                _ = StrictUtf8.GetCharCount(Take(length));
            }
            catch (DecoderFallbackException exception)
            {
                throw new SaveFormatException($"Invalid UTF-8 in Memoria string at offset {Position - length}.", exception);
            }
        }

        public ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || Position > content.Length - count)
            {
                throw new SaveFormatException($"Unexpected end of Memoria data at offset {Position}.");
            }

            ReadOnlySpan<byte> result = content.Slice(Position, count);
            Position = checked(Position + count);
            return result;
        }

        private int Read7BitLength()
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                byte value = Take(1)[0];
                result |= (uint)(value & 0x7f) << shift;
                if ((value & 0x80) == 0)
                {
                    if (result > int.MaxValue)
                    {
                        throw new SaveFormatException($"Memoria 7-bit length is too large at offset {Position}.");
                    }

                    return (int)result;
                }
            }

            throw new SaveFormatException($"Invalid Memoria 7-bit length at offset {Position}.");
        }
    }
}
