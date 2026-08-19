// SPDX-License-Identifier: MIT
namespace FFIX.SaveEditor.Core;

public static class GameData
{
    public const byte EmptyItemId = 0xFF;
    public const byte NoCardType = 0xFF;

    public static IReadOnlyList<string> ItemNames { get; } = Lines(ItemData);
    public static IReadOnlyList<string> SupportAbilityNames { get; } = Lines(SupportAbilityData);
    public static IReadOnlyList<string> CardTypeNames { get; } = Lines(CardTypeData);

    public static string ItemName(int itemId) => itemId >= 0 && itemId < ItemNames.Count ? ItemNames[itemId] : $"0x{itemId:X2}";
    public static string CardTypeName(int typeId) => typeId >= 0 && typeId < CardTypeNames.Count ? CardTypeNames[typeId] : "(none)";

    public static byte ResolveItemId(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var value = token.Trim();
        if (TryParseId(value, out var numeric))
        {
            if (numeric is < 0 or > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(token), $"Item ID out of range 0-255: {numeric}");
            return (byte)numeric;
        }
        var exact = Enumerable.Range(0, ItemNames.Count)
            .FirstOrDefault(index => string.Equals(ItemNames[index], value, StringComparison.OrdinalIgnoreCase), -1);
        if (exact >= 0)
            return (byte)exact;
        var matches = Enumerable.Range(0, ItemNames.Count)
            .Where(index => ItemNames[index].Contains(value, StringComparison.OrdinalIgnoreCase)).Take(9).ToArray();
        return matches.Length switch
        {
            0 => throw new ArgumentException($"Unknown item name/ID: '{token}'.", nameof(token)),
            1 => (byte)matches[0],
            _ => throw new ArgumentException($"Ambiguous item name '{token}': matches " +
                                             string.Join(", ", matches.Take(8).Select(index => ItemNames[index])), nameof(token)),
        };
    }

    private static bool TryParseId(string value, out int itemId)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value.AsSpan(2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out itemId);
        return int.TryParse(value, out itemId);
    }

    private static IReadOnlyList<string> Lines(string source) =>
        source.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private const string ItemData = """
        Hammer
        Dagger
        Mage Masher
        Mythril Dagger
        Gladius
        Zorlin Shape
        Orichalcon
        Butterfly Sword
        The Ogre
        Exploda
        Rune Thoot
        Angel Bless
        Sargatanas
        Masamune
        The Tower
        Ultima Weapon
        Broadsword
        Iron Sword
        Mythril Sword
        Blood Sword
        Ice Brand
        Coral Sword
        Diamond Sword
        Flame Saber
        Rune Blade
        Defender
        Save The Queen
        UltimaSword
        Excalibur
        Ragnarok
        Excalibur II
        Javelin
        Mythril Spear
        Partisan
        Ice Lance
        Trident
        Heavy Lance
        Obelisk
        Holy Lance
        Kain's Lance
        Dragon's Hair
        Cat's Claws
        Poison Knuckles
        Mythril Knuckles
        Scissor Fangs
        Dragon's Claws
        Tiger Fangs
        Avenger
        Kaiser Knuckles
        Duel Claws
        Rune Claws
        Air Racket
        Multina Racket
        Magic Racket
        Mythril Racket
        Priest's Racket
        Tiger Racket
        Rod
        Mythril Rod
        Stardust Rod
        Healing Rod
        Asura's Rod
        Wizard Rod
        Whale Whisker
        Golem's Flute
        Lamia's Flute
        Fairy Flute
        Hamelin
        Siren's Flute
        Angel Flute
        Mage Staff
        Flame Staff
        Ice Staff
        Lightning Staff
        Oak Staff
        Cypress Pile
        Octagon Rod
        High Mage Staff
        Mace of Zeus
        Fork
        Needle Fork
        Mythril Fork
        Silver Fork
        Bistro Fork
        Gastro Fork
        Pinwheel
        Rising Sun
        Wing Edge
        Wrist
        Leather Wrist
        Glass Armlet
        Bone Wrist
        Mythril Armlet
        Magic Armlet
        Chimera Armlet
        Egoist's Armlet
        N-Kai Armlet
        Jade Armlet
        Thief Gloves
        Dragon Wrist
        Power Wrist
        Bracer
        Bronze Gloves
        Silver Gloves
        Mythril Gloves
        Thunder Gloves
        Diamond Gloves
        Venitia Shield
        Defense Gloves
        Genji Gloves
        Aegis Gloves
        Gauntlets
        Leather Hat
        Straw Hat
        Feather Hat
        Steepled Hat
        Headgear
        Magus Hat
        Bandana
        Mage's Hat
        Lamia's Tiara
        Ritual Hat
        Twist Headband
        Mantra Band
        Dark Hat
        Green Beret
        Black Hood
        Red Hat
        Golden Heirpin
        Coronet
        Flash Hat
        Adaman Hat
        Theif Hat
        Holy Miter
        Golden Skullcap
        Circlet
        Rubber Helm
        Bronze Helm
        Iron Helm
        Barbut
        Mythril Helm
        Gold Helm
        Cross Helm
        Diamond Helm
        Platinum Helm
        Kaiser Helm
        Genji Helm
        Grand Helm
        Aloha T-shirt
        Leather Shirt
        Silk Shirt
        Leather Plate
        Bronze Vest
        Chain Plate
        Mythril Vest
        Adaman Vest
        Magician Cloak
        Survival Vest
        Brigandine
        Judo Uniform
        Power Vest
        Gaia Gear
        Demon's Vest
        Minerva's Plate
        Ninja Gear
        Dark Gear
        Rubber Suit
        Brave Suit
        Cotton Robe
        Silk Robe
        Magician Robe
        Glutton's Robe
        White Robe
        Black Robe
        Light Robe
        Robe of Lords
        Tin Armor
        Bronze Armor
        Linen Cuirass
        Chain Mail
        Mythril Armor
        Plate Mail
        Gold Armor
        Shield Armor
        Demon's Mail
        Diamon Armor
        Platina Armor
        Carabini Mail
        Dragon Mail
        Genji Armor
        Maximilian
        Grand Armor
        Desert Boots
        Magician Shoes
        Germinas Boots
        Sandals
        Feather Boots
        Battle Boots
        Running Shoes
        Anklet
        Power Belt
        Black Belt
        Glass Buckle
        Madain's Ring
        Rosetta Ring
        Reflact Ring
        Coral Ring
        Promist Ring
        Rebirth Ring
        Protect Ring
        Pumice Piece
        Pumice
        Yellow Scarf
        Gold Choker
        Fairy Earrings
        Angel Earrings
        Pearl Rouge
        Pearl Armlet
        Cachusha
        Barette
        Extension
        Ribbon
        Maiden Prayer
        Ancient Aroma
        Garnet
        Amethyst
        Aquamarine
        Diamond
        Emerald
        Moonstone
        Ruby
        Peridot
        Sapphire
        Opal
        Topaz
        Lapiz Lazuli
        Potion
        Hi-Potion
        Ether
        Elixir
        Phoenix Down
        Echo Screen
        Soft
        Antidote
        Eye Drops
        Magic Tag
        Vaccine
        Remedy
        Annoyntment
        Phoenix Pinion
        Dark Matter
        Gysahl Greens
        Dead Peper
        Tent
        Ore
        Nothing
        """;

    private const string SupportAbilityData = """
        Auto-Reflect
        Auto-Float
        Auto-Haste
        Auto-Regen
        Auto-Life
        HP+10%
        HP+20%
        MP+10%
        MP+20%
        Accuracy+
        Distract
        Long Reach
        MP Attack
        Bird Killer
        Bug Killer
        Stone Killer
        Undead Killer
        Dragon Killer
        Devil Killer
        Beast killer
        Man Eater
        High Jump
        Master Thief
        Steal Gil
        Healer
        Add Status
        Gamble Defence
        Chemist
        Power Throw
        Power Up
        Reflect-Null
        Reflectx2
        Mag Elem Null
        Concentrate
        Half MP
        High Tide
        Counter
        Cover
        Protect Girls
        Eye 4 Eye
        Body Temp
        Alert
        Initiative
        Level Up
        Ability Up
        Millionaire
        Flee-Gil
        Guardian Mog
        Insomniac
        Antibody
        Bright Eyes
        Loudmouth
        Restore HP
        Jelly
        Return Magic
        Absorb MP
        Auto-Potion
        Locomotion
        Clear Headed
        Boost
        Odin's Sword
        Mug
        Bandit
        Void
        """;

    private const string CardTypeData = """
        Goblin
        Fang
        Skeleton
        Flan
        Zaghnol
        Lizard Man
        Zombie
        Bomb
        Ironite
        Sahagin
        Yeti
        Mimic
        Wyerd
        Mandragora
        Crawler
        Sand Scorpion
        Nymph
        Sand Golem
        Zuu
        Dragonfly
        Carrion Worm
        Cerberus
        Antlion
        Cactuar
        Gimme Cat
        Ragtimer
        Hedgehog Pie
        Ralvuimahgo
        Ochu
        Troll
        Blazer Beetle
        Abomination
        Zemzelett
        Stroper
        Tantarian
        Grand Dragon
        Feather Circle
        Hecteyes
        Ogre
        Armstrong
        Ash
        Wraith
        Gargoyle
        Vepal
        Grimlock
        Tonberry
        Veteran
        Garuda
        Malboro
        Mover
        Abadon
        Behemoth
        Iron Man
        Nova Dragon
        Ozma
        Hades
        Holy
        Meteor
        Flare
        Shiva
        Ifrit
        Ramuh
        Atomos
        Odin
        Leviathan
        Bahamut
        Ark
        Fenrir
        Madeen
        Alexander
        Excalibur 2
        Ultima Weapon
        Masamune
        Elixir
        Dark Matter
        Ribbon
        Tiger Racket
        Save The Queen
        Genji
        Mythril Sword
        Blue Narciss
        Hilda Garde 3
        Invincible
        Cargo Ship
        Hilda Garde 1
        Red Rose
        Theater Ship
        Viltgance
        Chocobo
        Fat Chocobo
        Mog
        Frog
        Oglop
        Alexandria
        Lindblum
        Two Moons
        Gargant
        Namingway
        Boco
        Airship
        """;
}
