using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Inventory;
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
            Slot = ItemSlotKind.Weapon,
        };
        catalog.Items["whetstone"] = new ItemDefinition { Id = "whetstone", Tags = { "stone" } };
        catalog.Items["great_sword"] = new ItemDefinition
        {
            Id = "great_sword",
            Rarity = ItemRarity.Rare,
            Tags = { "weapon", "blade" },
            Shape = { new CellOffset(0, 0), new CellOffset(0, 1), new CellOffset(0, 2) },
            WeaponDamage = "2d6",
            Slot = ItemSlotKind.TwoHanded,
        };
        catalog.Items["shield"] = new ItemDefinition
        {
            Id = "shield",
            Shape = { new CellOffset(0, 0), new CellOffset(1, 0), new CellOffset(0, 1), new CellOffset(1, 1) },
            Modifiers = { new StatModifier(Stat.ArmorClass, 2) },
            Slot = ItemSlotKind.OffHand,
        };
        catalog.Items["ring"] = new ItemDefinition
        {
            Id = "ring",
            Modifiers = { new StatModifier(Stat.DamageBonus, 1) },
            Slot = ItemSlotKind.Ring,
        };
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
