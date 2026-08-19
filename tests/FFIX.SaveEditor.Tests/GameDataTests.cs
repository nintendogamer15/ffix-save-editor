using FFIX.SaveEditor.Core;

namespace FFIX.SaveEditor.Tests;

public sealed class GameDataTests
{
    [Fact]
    public void AllReferenceTablesAndLegacyCodecArePreserved()
    {
        Assert.Equal(256, GameData.ItemNames.Count);
        Assert.Equal(64, GameData.SupportAbilityNames.Count);
        Assert.Equal(100, GameData.CardTypeNames.Count);
        Assert.Equal(29, GameData.ResolveItemId("Ragnarok"));
        Assert.Equal(29, GameData.ResolveItemId("0x1D"));
        var text = "Zidane Àß♥";
        Assert.Equal(text, LegacyTextCodec.Decode(LegacyTextCodec.Encode(text, text.Length)));
    }
}
