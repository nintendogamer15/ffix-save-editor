// SPDX-License-Identifier: MIT
namespace FFIX.SaveEditor.Core;

public static class SaveLayout
{
    public const int MaximumGil = 9_999_999;
    public static IReadOnlyList<string> EquipmentSlots { get; } = ["weapon", "head", "arm", "armor", "accessory"];

    public const int LegacyBlockHeaderSize = 0x80;
    public const int LegacyBlockSize = 0x2000;
    public const int LegacyCardSize = 0x20000;
    public const int LegacyRegionCodeOffset = 0xA;
    public const int LegacyMaximumBlocks = 16;
    public const int LegacyChecksumOffset = 0x13FE;
    public static FieldSpec LegacyPreviewGil { get; } = new(0x130, 3);
    public static FieldSpec LegacyGil { get; } = new(0xEE8, 4);
    public static FieldSpec LegacyPlaytime { get; } = new(0x12C, 4);
    public const int LegacyLocationOffset = 0x110;
    public const int LegacyLocationLength = 28;
    public const int LegacyLeaderNameOffset = 0x106;
    public const int LegacyLeaderNameLength = 8;
    public const int LegacyLeaderLevelOffset = 0x105;
    public const int LegacyCharacterStart = 0x9D0;
    public const int LegacyCharacterSize = 144;
    public const int LegacyCharacterCount = 9;
    public const int LegacyCharacterNameLength = 8;
    public const int LegacySupportBitmapOffset = 0x088;
    public const int LegacySupportBitmapLength = 8;
    public const int LegacyItemStart = 0xF20;
    public const int LegacyItemCount = 256;
    public static FieldSpec LegacyCardWins { get; } = new(0x1178, 2);
    public static FieldSpec LegacyCardLosses { get; } = new(0x117A, 2);
    public static FieldSpec LegacyCardDraws { get; } = new(0x117C, 2);
    public const int LegacyCardStart = 0x117E;
    public const int LegacyCardRecordSize = 6;
    public const int LegacyCardCount = 105;
    public static IReadOnlyList<int> LegacyCardLayout { get; } = [0, 1, 2, 3, 4, 5];

    public static IReadOnlyDictionary<string, FieldSpec> LegacyCharacterFields { get; } =
        new Dictionary<string, FieldSpec>
        {
            ["level"] = new(0x00B, 1), ["exp"] = new(0x00C, 4), ["cur_hp"] = new(0x010, 2),
            ["cur_mp"] = new(0x012, 2), ["cur_magic_stones"] = new(0x017, 1), ["max_hp"] = new(0x018, 2),
            ["max_mp"] = new(0x01A, 2), ["max_magic_stones"] = new(0x01F, 1), ["trance"] = new(0x020, 1),
            ["speed"] = new(0x024, 1), ["strength"] = new(0x025, 1), ["magic"] = new(0x026, 1),
            ["spirit"] = new(0x027, 1), ["defence"] = new(0x028, 1), ["evade"] = new(0x029, 1),
            ["magic_defence"] = new(0x02A, 1), ["magic_evade"] = new(0x02B, 1),
            ["max_hp_bonus"] = new(0x02C, 2), ["max_mp_bonus"] = new(0x02E, 2),
            ["speed_base"] = new(0x030, 1), ["strength_base"] = new(0x031, 1),
            ["magic_base"] = new(0x032, 1), ["spirit_base"] = new(0x033, 1), ["status"] = new(0x038, 1),
            ["weapon"] = new(0x039, 1), ["head"] = new(0x03A, 1), ["arm"] = new(0x03B, 1),
            ["armor"] = new(0x03C, 1), ["accessory"] = new(0x03D, 1),
        };

    public const int RrContainerSize = 0x2CD140;
    public const int RrChunkSize = 0x4800;
    public const int RrChunkBase = 153_920;
    public const int RrMetadataPlaintextSize = 288;
    public const int RrMaximumSlots = 9;
    public const int RrMaximumSaves = 15;
    public const int RrSlotPlaintextSize = 0x4632;
    public static ReadOnlySpan<byte> RrOccupiedHeader => "SAVE"u8;
    public static ReadOnlySpan<byte> RrEmptyHeader => "NONE"u8;
    public static FieldSpec RrGil { get; } = new(0x1473, 4);
    public const int RrItemStart = 0x1477;
    public const int RrItemCount = 256;
    public const int RrCardStart = 0x101B;
    public const int RrCardRecordSize = 11;
    public const int RrCardCount = 100;
    public static IReadOnlyList<int> RrCardLayout { get; } = [3, 0, 1, 7, 5, 4];
    public static FieldSpec RrCardDraws { get; } = new(0x1467, 2);
    public static FieldSpec RrCardLosses { get; } = new(0x1469, 2);
    public static FieldSpec RrCardWins { get; } = new(0x146B, 2);
    public const int RrCharacterStart = 0x1677;
    public const int RrCharacterSize = 0xF4;
    public const int RrCharacterCount = 9;
    public const int RrCharacterNameOffset = 0x39;
    public const int RrCharacterNameLength = 8;
    public const int RrPartyOffset = 0x1F4B;
    public const int RrPartySlots = 4;
    public const int RrPlaytimeOffset = 0x3832;

    public static IReadOnlyDictionary<string, FieldSpec> RrCharacterFields { get; } =
        new Dictionary<string, FieldSpec>
        {
            ["speed_base"] = new(0x00, 1), ["max_hp_base"] = new(0x01, 2), ["max_mp_base"] = new(0x03, 2),
            ["magic_base"] = new(0x05, 1), ["strength_base"] = new(0x06, 1), ["spirit_base"] = new(0x07, 1),
            ["cur_hp"] = new(0x15, 2), ["cur_mp"] = new(0x17, 2), ["magic_defence"] = new(0x19, 1),
            ["magic_evade"] = new(0x1A, 1), ["defence"] = new(0x1B, 1), ["evade"] = new(0x1C, 1),
            ["speed_bonus"] = new(0x1D, 1), ["magic_bonus"] = new(0x1E, 1),
            ["strength_bonus"] = new(0x1F, 1), ["spirit_bonus"] = new(0x20, 1),
            ["weapon"] = new(0x21, 1), ["head"] = new(0x22, 1), ["arm"] = new(0x23, 1),
            ["armor"] = new(0x24, 1), ["accessory"] = new(0x25, 1), ["exp"] = new(0x26, 4),
            ["magic_stones"] = new(0x34, 1), ["max_hp"] = new(0x35, 2), ["max_mp"] = new(0x37, 2),
            ["level"] = new(0x30, 1),
        };
}
