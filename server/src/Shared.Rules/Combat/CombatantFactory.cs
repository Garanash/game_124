using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Inventory;
using Realmforge.Rules.Stats;

namespace Realmforge.Rules.Combat
{
    public static class CombatantFactory
    {
        public const int RangedWeaponRangeCells = 16;
        private const int DefaultSpeedCells = 6;
        private const string UnarmedDamage = "1d4";

        public static StatBlock CharacterBonuses(CharacterSheet sheet, Backpack backpack, ContentCatalog catalog)
        {
            StatBlock bonuses = BackpackRules.ComputeBonuses(backpack, catalog);
            bonuses.Merge(PerkRules.Bonuses(sheet, catalog));
            return bonuses;
        }

        public static Combatant FromCharacter(long entityId, CharacterSheet sheet, Backpack backpack, ContentCatalog catalog, int team)
        {
            StatBlock bonuses = CharacterBonuses(sheet, backpack, catalog);
            AbilityScores abilities = ApplyAbilityBonuses(sheet.Abilities(catalog), bonuses);
            int level = sheet.Level;
            int proficiency = Progression.ProficiencyBonus(level);
            ItemDefinition? weapon = BackpackRules.BestWeapon(backpack, catalog);
            bool finesse = weapon != null && weapon.Tags.Contains("finesse");
            bool ranged = weapon != null && weapon.Tags.Contains("ranged");
            Ability attackAbility = finesse || ranged ? Ability.Dexterity : Ability.Strength;
            int attackModifier = abilities.Modifier(attackAbility);
            List<ClassDefinition> classes = sheet.ClassLevels.Keys
                .Where(catalog.Classes.ContainsKey)
                .Select(id => catalog.Classes[id])
                .ToList();
            ClassDefinition? caster = classes.FirstOrDefault(definition => definition.SpellcastingAbility.HasValue);
            int castingModifier = caster?.SpellcastingAbility != null ? abilities.Modifier(caster.SpellcastingAbility.Value) : 0;
            int raceSpeed = RaceSpeedCells(sheet, catalog);

            var combatant = new Combatant(entityId, sheet.Name, team)
            {
                IsPlayerControlled = true,
                CharacterId = sheet.Id,
                Level = level,
                Abilities = abilities,
                MaxHp = CharacterRules.MaxHp(sheet, catalog, bonuses),
                ArmorClass = CharacterRules.ArmorClass(sheet, catalog, bonuses),
                SpeedCells = raceSpeed + bonuses[Stat.SpeedCells],
                InitiativeBonus = abilities.Modifier(Ability.Dexterity) + bonuses[Stat.Initiative],
                SaveProficiencies = new HashSet<Ability>(classes.FirstOrDefault()?.SavingThrows ?? new List<Ability>()),
                Attack = new AttackProfile
                {
                    AttackBonus = attackModifier + proficiency + bonuses[Stat.AttackBonus],
                    Damage = weapon?.WeaponDamage ?? UnarmedDamage,
                    DamageType = weapon?.WeaponDamageType ?? DamageType.Bludgeoning,
                    DamageBonus = attackModifier + bonuses[Stat.DamageBonus],
                    RangeCells = ranged ? RangedWeaponRangeCells : 1,
                },
                SpellAttackBonus = proficiency + castingModifier + bonuses[Stat.AttackBonus],
                SpellSaveDc = Checks.BaseSpellSaveDc + proficiency + castingModifier + bonuses[Stat.SpellSaveDc],
                SpellSlots = SpellSlotsFor(sheet, classes),
                KnownAbilities = CharacterRules.KnownAbilities(sheet, catalog).ToList(),
            };
            combatant.Hp = sheet.CurrentHp > 0 ? System.Math.Min(sheet.CurrentHp, combatant.MaxHp) : combatant.MaxHp;
            return combatant;
        }

        public static Combatant FromMob(long entityId, MobDefinition mob, int team)
        {
            return new Combatant(entityId, mob.Name, team)
            {
                MobId = mob.Id,
                Level = mob.Level,
                MaxHp = mob.Hp,
                Hp = mob.Hp,
                ArmorClass = mob.ArmorClass,
                SpeedCells = mob.SpeedCells,
                Abilities = new AbilityScores().With(Ability.Dexterity, mob.Dexterity),
                InitiativeBonus = AbilityScores.ModifierOf(mob.Dexterity),
                Attack = new AttackProfile { AttackBonus = mob.AttackBonus, Damage = mob.Damage, DamageType = mob.DamageType },
                SpellAttackBonus = mob.AttackBonus,
                SpellSaveDc = Checks.BaseSpellSaveDc + mob.AttackBonus,
                KnownAbilities = mob.Abilities.ToList(),
                XpValue = mob.Xp,
                LootTableId = mob.LootTableId,
            };
        }

        private static int RaceSpeedCells(CharacterSheet sheet, ContentCatalog catalog)
        {
            return catalog.Races.TryGetValue(sheet.RaceId, out RaceDefinition? race) ? race.SpeedCells : DefaultSpeedCells;
        }

        private static int[] SpellSlotsFor(CharacterSheet sheet, List<ClassDefinition> classes)
        {
            int casterLevel = classes.Sum(definition => SpellSlots.CasterLevel(definition.Caster, sheet.ClassLevels[definition.Id]));
            return SpellSlots.ForCasterLevel(casterLevel);
        }

        private static AbilityScores ApplyAbilityBonuses(AbilityScores scores, StatBlock bonuses)
        {
            var mapping = new Dictionary<Ability, int>
            {
                { Ability.Strength, bonuses[Stat.Strength] },
                { Ability.Dexterity, bonuses[Stat.Dexterity] },
                { Ability.Constitution, bonuses[Stat.Constitution] },
                { Ability.Intelligence, bonuses[Stat.Intelligence] },
                { Ability.Wisdom, bonuses[Stat.Wisdom] },
                { Ability.Charisma, bonuses[Stat.Charisma] },
            };
            return scores.WithBonuses(mapping);
        }
    }
}
