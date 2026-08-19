// SPDX-License-Identifier: MIT
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FFIX.SaveEditor.Core;

internal static class BinarySave
{
    public static uint ReadUInt(ReadOnlySpan<byte> data, int offset, int size) => size switch
    {
        1 => data[offset],
        2 => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]),
        3 => (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16),
        4 => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]),
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };

    public static void WriteUInt(Span<byte> data, int offset, int size, long value)
    {
        long maximum = size switch { 1 => byte.MaxValue, 2 => ushort.MaxValue, 3 => 0xFF_FFFF, 4 => uint.MaxValue, _ => throw new ArgumentOutOfRangeException(nameof(size)) };
        var bounded = (ulong)Math.Clamp(value, 0, maximum);
        switch (size)
        {
            case 1: data[offset] = (byte)bounded; break;
            case 2: BinaryPrimitives.WriteUInt16LittleEndian(data[offset..], (ushort)bounded); break;
            case 3:
                data[offset] = (byte)bounded;
                data[offset + 1] = (byte)(bounded >> 8);
                data[offset + 2] = (byte)(bounded >> 16);
                break;
            case 4: BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], (uint)bounded); break;
        }
    }

    public static int GetField(ReadOnlySpan<byte> data, int baseOffset, FieldSpec field) =>
        checked((int)ReadUInt(data, baseOffset + field.Offset, field.Size));

    public static void SetField(Span<byte> data, int baseOffset, FieldSpec field, int value) =>
        WriteUInt(data, baseOffset + field.Offset, field.Size, value);
}

public static class LegacyChecksum
{
    private static IReadOnlyList<ushort> Table { get; } = BuildTable();

    public static ushort Calculate(ReadOnlySpan<byte> block)
    {
        if (block.Length < SaveLayout.LegacyChecksumOffset + 2)
            throw new ArgumentException("Legacy block is too short.", nameof(block));
        ushort crc = 0xFFFF;
        foreach (var value in block[..SaveLayout.LegacyChecksumOffset])
            crc = (ushort)((crc >> 8) ^ Table[(crc ^ value) & 0xFF]);
        return crc;
    }

    public static void Repair(Span<byte> block) =>
        BinaryPrimitives.WriteUInt16LittleEndian(block[SaveLayout.LegacyChecksumOffset..], Calculate(block));

    private static IReadOnlyList<ushort> BuildTable()
    {
        var table = new ushort[256];
        for (var index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? (value >> 1) ^ 0x8408 : value >> 1;
            table[index] = (ushort)value;
        }
        return table;
    }
}

public sealed record RrMetadata(
    float SaveVersion,
    int DataSize,
    int LatestSlot,
    int LatestSave,
    double LatestTimestamp,
    bool IsGameFinished,
    int SelectedLanguage)
{
    public int SlotPlaintextSize => DataSize + 4;
}

public static class RrCrypto
{
    private static readonly byte[] Salt = [3, 3, 1, 4, 7, 0, 9, 7];
    private static readonly byte[] Password = "System.Security.SecureString"u8.ToArray();

    public static (byte[] Key, byte[] Iv) DeriveKeyAndIv()
    {
        var derived = Rfc2898DeriveBytes.Pbkdf2(Password, Salt, 1000, HashAlgorithmName.SHA1, 48);
        return (derived[..32], derived[32..]);
    }

    public static int CipherSize(int plaintextSize) => plaintextSize + 16 - plaintextSize % 16;

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        var (key, iv) = DeriveKeyAndIv();
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7);
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> ciphertext)
    {
        var (key, iv) = DeriveKeyAndIv();
        using var aes = Aes.Create();
        aes.Key = key;
        try
        {
            return aes.DecryptCbc(ciphertext, iv, PaddingMode.PKCS7);
        }
        catch (CryptographicException exception)
        {
            throw new SaveFormatException("Could not decrypt the rr2016 data (bad padding/key or corrupt file).", exception);
        }
    }

    public static RrMetadata ParseMetadata(ReadOnlySpan<byte> container)
    {
        var cipherLength = CipherSize(SaveLayout.RrMetadataPlaintextSize);
        if (container.Length < cipherLength)
            throw new SaveFormatException("The rr2016 container is truncated before its metadata block.");
        byte[] plaintext;
        try
        {
            plaintext = Decrypt(container[..cipherLength]);
        }
        catch (SaveFormatException exception)
        {
            throw new SaveFormatException(
                "Could not decrypt the save header. The file has the rr2016 size, but the vanilla fixed AES key did not produce valid metadata; the file may use modded encryption or be corrupt.",
                exception);
        }
        if (!plaintext.AsSpan(0, 4).SequenceEqual("SAVE"u8))
            throw new SaveFormatException("RR2016 metadata header mismatch (bad key or corrupt file).");
        var metadata = new RrMetadata(
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(plaintext.AsSpan(4))),
            BinaryPrimitives.ReadInt32LittleEndian(plaintext.AsSpan(8)),
            BinaryPrimitives.ReadInt32LittleEndian(plaintext.AsSpan(12)),
            BinaryPrimitives.ReadInt32LittleEndian(plaintext.AsSpan(16)),
            BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(plaintext.AsSpan(20))),
            BinaryPrimitives.ReadInt32LittleEndian(plaintext.AsSpan(28)) != 0,
            BinaryPrimitives.ReadInt32LittleEndian(plaintext.AsSpan(32)));
        if (metadata.SlotPlaintextSize != SaveLayout.RrSlotPlaintextSize)
            throw new SaveFormatException($"Unsupported rr2016 slot size {metadata.SlotPlaintextSize:N0}; expected {SaveLayout.RrSlotPlaintextSize:N0}.");
        return metadata;
    }

    public static int ChunkOffset(int slotId, int saveId)
    {
        if ((uint)slotId >= SaveLayout.RrMaximumSlots)
            throw new ArgumentOutOfRangeException(nameof(slotId), $"rr2016 slot must be 1-{SaveLayout.RrMaximumSlots}.");
        if ((uint)saveId >= SaveLayout.RrMaximumSaves)
            throw new ArgumentOutOfRangeException(nameof(saveId), $"rr2016 file must be 1-{SaveLayout.RrMaximumSaves}.");
        return SaveLayout.RrChunkBase + SaveLayout.RrChunkSize * (1 + slotId * SaveLayout.RrMaximumSaves + saveId);
    }
}
