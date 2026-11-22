using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Stats;

namespace Realmforge.Rules.Tests;

/// <summary>Small hand-built catalog so rules tests stay independent of the YAML content.</summary>
internal static class TestCatalog
{
    public static ContentCatalog Create()
    {
        var catalog = new ContentCatalog();
        catalog.Zones["start_dwarf"] = new ZoneDefinition { Id = "start_dwarf", Spawn = new WorldPoint(10, 20) };
        catalog.Races["dwarf"] = new RaceDefinition
        {
            Id = "dwarf",
            StartZoneId = "start_dwarf",
            Playable = true,
            SpeedCells = 5,
            AbilityBonuses = new Dictionary<Ability, int> { { Ability.Constitution, 2 } },
        };
        catalog.Classes["fighter"] = new ClassDefinition
        {
            Id = "fighter",
            HitDie = 10,
            PrimaryAbility = Ability.Strength,
            BaseArmorClass = 14,
            Abilities = { new ClassAbilityUnlock { Level = 1, AbilityId = "second_wind" } },
        };
        catalog.Classes["paladin"] = new ClassDefinition
        {
            Id = "paladin",
            HitDie = 10,
            PrimaryAbility = Ability.Charisma,
            Alignment = new AlignmentRequirement { Moral = { MoralAxis.Good } },
        };
        catalog.Abilities["fire_bolt"] = new AbilityDefinition
        {
            Id = "fire_bolt",
            Kind = AbilityKind.Spell,
            Target = TargetKind.Enemy,
            RangeCells = 12,
            AttackRoll = true,
            Effects = { new EffectDefinition { Type = EffectType.Damage, Dice = "1d10", DamageType = DamageType.Fire } },
        };
        catalog.Abilities["thunderwave"] = new AbilityDefinition
        {
            Id = "thunderwave",
            Kind = AbilityKind.Spell,
            Target = TargetKind.Area,
            RangeCells = 6,
            AreaRadiusCells = 1,
            SpellLevel = 1,
            SaveAbility = Ability.Constitution,
            Effects = { new EffectDefinition { Type = EffectType.Damage, Dice = "2d8", DamageType = DamageType.Force, HalfOnSave = true } },
        };
        catalog.Items["sword"] = new ItemDefinition
        {
            Id = "sword",
            Tags = { "weapon", "blade" },
            Shape = { new CellOffset(0, 0), new CellOffset(0, 1) },
            WeaponDamage = "1d8",
            WeaponDamageType = DamageType.Slashing,
        };
        catalog.Items["whetstone"] = new ItemDefinition { Id = "whetstone", Tags = { "stone" } };
        catalog.Items["great_sword"] = new ItemDefinition
        {
            Id = "great_sword",
            Rarity = ItemRarity.Rare,
            Tags = { "weapon", "blade" },
            Shape = { new CellOffset(0, 0), new CellOffset(0, 1), new CellOffset(0, 2) },
            WeaponDamage = "2d6",
        };
        catalog.Synergies["sharpened"] = new SynergyDefinition
        {
            Id = "sharpened",
            SourceTag = "blade",
            NeighborTag = "stone",
            Bonus = { new StatModifier(Stat.DamageBonus, 1) },
        };
        catalog.Fusions["forge_great_sword"] = new FusionRecipe { Id = "forge_great_sword", Inputs = { "sword", "sword" }, Output = "great_sword" };
        return catalog;
    }

    public static CreateCharacterRequest DwarfRequest(string name = "Торин")
    {
        return new CreateCharacterRequest
        {
            Name = name,
            RaceId = "dwarf",
            Alignment = AlignmentCell.LawfulGood,
            Abilities = new[] { 15, 14, 13, 12, 10, 8 },
        };
    }
}
