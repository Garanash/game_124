using System;
using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Inventory;
using Realmforge.Rules.Stats;

namespace Realmforge.Rules.Combat
{
    public sealed class AbilityScoreSummary
    {
        public string Ability { get; set; } = string.Empty;
        /// <summary>Point-buy score chosen at creation, before race and item bonuses.</summary>
        public int Base { get; set; }
        public int Total { get; set; }
        public int Modifier { get; set; }
    }

    /// <summary>What the character sheet screen shows: the exact numbers an encounter would use.</summary>
    public sealed class CharacterStats
    {
        public int Level { get; set; }
        public int ProficiencyBonus { get; set; }
        public int MaxHp { get; set; }
        public int ArmorClass { get; set; }
        public int SpeedCells { get; set; }
        public int Initiative { get; set; }
        public int AttackBonus { get; set; }
        public string WeaponId { get; set; } = string.Empty;
        public string Damage { get; set; } = string.Empty;
        public string DamageType { get; set; } = string.Empty;
        public int DamageBonus { get; set; }
        public int RangeCells { get; set; }
        public int SpellAttackBonus { get; set; }
        public int SpellSaveDc { get; set; }
        public int[] SpellSlots { get; set; } = Array.Empty<int>();
        public List<AbilityScoreSummary> Abilities { get; set; } = new List<AbilityScoreSummary>();
        /// <summary>Backpack, synergy and perk bonuses by stat name; zero entries are left out.</summary>
        public Dictionary<string, int> Bonuses { get; set; } = new Dictionary<string, int>();
        public List<string> AvailableFusions { get; set; } = new List<string>();

        public static CharacterStats Build(CharacterSheet sheet, Backpack backpack, ContentCatalog catalog)
        {
            Combatant combatant = CombatantFactory.FromCharacter(0, sheet, backpack, catalog, 0);
            StatBlock bonuses = CombatantFactory.CharacterBonuses(sheet, backpack, catalog);
            return new CharacterStats
            {
                Level = sheet.Level,
                ProficiencyBonus = Progression.ProficiencyBonus(sheet.Level),
                MaxHp = combatant.MaxHp,
                ArmorClass = combatant.ArmorClass,
                SpeedCells = combatant.SpeedCells,
                Initiative = combatant.InitiativeBonus,
                AttackBonus = combatant.Attack.AttackBonus,
                WeaponId = BackpackRules.BestWeapon(backpack, catalog)?.Id ?? string.Empty,
                Damage = combatant.Attack.Damage,
                DamageType = combatant.Attack.DamageType.ToString(),
                DamageBonus = combatant.Attack.DamageBonus,
                RangeCells = combatant.Attack.RangeCells,
                SpellAttackBonus = combatant.SpellAttackBonus,
                SpellSaveDc = combatant.SpellSaveDc,
                SpellSlots = combatant.SpellSlots.ToArray(),
                Abilities = Enum.GetValues(typeof(Ability)).Cast<Ability>().Select(ability => new AbilityScoreSummary
                {
                    Ability = ability.ToString(),
                    Base = sheet.BaseAbilities[(int)ability],
                    Total = combatant.Abilities[ability],
                    Modifier = combatant.Abilities.Modifier(ability),
                }).ToList(),
                Bonuses = bonuses.Values.Where(pair => pair.Value != 0).ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
                AvailableFusions = BackpackRules.FindFusions(backpack, catalog).Select(candidate => candidate.Recipe.Id).ToList(),
            };
        }
    }
}
