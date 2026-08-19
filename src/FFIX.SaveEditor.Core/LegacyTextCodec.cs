// SPDX-License-Identifier: MIT
namespace FFIX.SaveEditor.Core;

public static class LegacyTextCodec
{
    private const byte Terminator = 0xFF;

    public static byte[] Encode(string text, int length)
    {
        var result = Enumerable.Repeat(Terminator, length).ToArray();
        for (var index = 0; index < Math.Min(text.Length, length); index++)
            result[index] = CharacterToByte.TryGetValue(text[index], out var value) ? value : CharacterToByte['?'];
        return result;
    }

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var result = new System.Text.StringBuilder();
        foreach (var value in bytes)
        {
            if (value == Terminator)
                break;
            if (ByteToCharacter.TryGetValue(value, out var character))
                result.Append(character);
        }
        return result.ToString();
    }

    private static IReadOnlyDictionary<char, byte> BuildTable()
    {
        var result = new Dictionary<char, byte>();
        for (var index = 0; index < 10; index++) result[(char)('0' + index)] = (byte)index;
        for (var index = 0; index < 26; index++) result[(char)('A' + index)] = (byte)(0x10 + index);
        for (var index = 0; index < 26; index++) result[(char)('a' + index)] = (byte)(0x30 + index);
        foreach (var pair in Specials)
            result[pair.Key] = pair.Value;
        return result;
    }

    private static IReadOnlyDictionary<char, byte> Specials { get; } = new Dictionary<char, byte>
    {
        ['+'] = 0x0A, ['-'] = 0x0B, ['*'] = 0x0C, ['='] = 0x0D, ['%'] = 0x0E, [' '] = 0x0F,
        ['('] = 0x2A, ['!'] = 0x2B, ['?'] = 0x2C, ['“'] = 0x2D, [':'] = 0x2E, ['.'] = 0x2F,
        [')'] = 0x4A, [','] = 0x4B, ['/'] = 0x4C, ['•'] = 0x4D, ['~'] = 0x4E, ['&'] = 0x4F,
        ['Á'] = 0x50, ['À'] = 0x51, ['Â'] = 0x52, ['Ä'] = 0x53, ['É'] = 0x54, ['È'] = 0x55,
        ['Ê'] = 0x56, ['Ë'] = 0x57, ['Í'] = 0x58, ['Ì'] = 0x59, ['Î'] = 0x5A, ['Ï'] = 0x5B,
        ['Ó'] = 0x5C, ['Ò'] = 0x5D, ['Ô'] = 0x5E, ['Ö'] = 0x5F, ['Ú'] = 0x60, ['Ù'] = 0x61,
        ['Û'] = 0x62, ['Ü'] = 0x63, ['á'] = 0x64, ['à'] = 0x65, ['â'] = 0x66, ['ä'] = 0x67,
        ['é'] = 0x68, ['è'] = 0x69, ['ê'] = 0x6A, ['ë'] = 0x6B, ['í'] = 0x6C, ['ì'] = 0x6D,
        ['î'] = 0x6E, ['ï'] = 0x6F, ['ó'] = 0x70, ['ò'] = 0x71, ['ô'] = 0x72, ['ö'] = 0x73,
        ['ú'] = 0x74, ['ù'] = 0x75, ['û'] = 0x76, ['ü'] = 0x77, ['Ç'] = 0x78, ['Ñ'] = 0x79,
        ['ç'] = 0x7A, ['ñ'] = 0x7B, ['Œ'] = 0x7C, ['ß'] = 0x7D, ['’'] = 0x7E, ['”'] = 0x7F,
        ['_'] = 0x80, ['】'] = 0x81, ['【'] = 0x82, ['∴'] = 0x83, ['∵'] = 0x84, ['♪'] = 0x85,
        ['→'] = 0x86, ['∈'] = 0x87, ['ⅹ'] = 0x88, ['♦'] = 0x89, ['§'] = 0x8A, ['‹'] = 0x8B,
        ['›'] = 0x8C, ['←'] = 0x8D, ['∋'] = 0x8E, ['↑'] = 0x8F, ['△'] = 0x90, ['□'] = 0x91,
        ['∞'] = 0x92, ['♥'] = 0x93, ['≪'] = 0xA1, ['≫'] = 0xA2, ['↓'] = 0xA3, ['─'] = 0xA4,
        ['°'] = 0xA5, ['★'] = 0xA6, ['♂'] = 0xA7, ['♀'] = 0xA8, ['☺'] = 0xA9, ['„'] = 0xAB,
        ['‘'] = 0xAC, ['#'] = 0xAD, ['※'] = 0xAE, [';'] = 0xAF, ['¡'] = 0xB0, ['¿'] = 0xB1,
    };

    private static IReadOnlyDictionary<char, byte> CharacterToByte { get; } = BuildTable();
    private static IReadOnlyDictionary<byte, char> ByteToCharacter { get; } = CharacterToByte.ToDictionary(x => x.Value, x => x.Key);
}
