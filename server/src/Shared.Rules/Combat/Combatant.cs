using System;
using System.Collections.Generic;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;

namespace Realmforge.Rules.Combat
{
    public sealed class ActiveCondition
    {
        public ActiveCondition(Condition condition, int remainingRounds, long sourceId)
        {
            Condition = condition;
            RemainingRounds = remainingRounds;
            SourceId = sourceId;
        }

        public Condition Condition { get; }
        public int RemainingRounds { get; set; }
        public long SourceId { get; }
    }

    public sealed class AttackProfile
    {
        public int AttackBonus { get; set; }
        public string Damage { get; set; } = "1d4";
        public DamageType DamageType { get; set; } = DamageType.Bludgeoning;
        public int DamageBonus { get; set; }
        public int RangeCells { get; set; } = 1;
    }

    public sealed class Combatant
    {
        public Combatant(long entityId, string name, int team)
        {
            EntityId = entityId;
            Name = name;
            Team = team;
        }

        public long EntityId { get; }
        public string Name { get; }
        public int Team { get; }
        public bool IsPlayerControlled { get; set; }
        public Guid? CharacterId { get; set; }
        public string MobId { get; set; } = string.Empty;
        public int Level { get; set; } = 1;
        public int MaxHp { get; set; }
        public int Hp { get; set; }
        public int ArmorClass { get; set; }
        public int SpeedCells { get; set; } = 6;
        public int InitiativeBonus { get; set; }
        public AbilityScores Abilities { get; set; } = new AbilityScores();
        public HashSet<Ability> SaveProficiencies { get; set; } = new HashSet<Ability>();
        public AttackProfile Attack { get; set; } = new AttackProfile();
        public int SpellAttackBonus { get; set; }
        public int SpellSaveDc { get; set; } = Checks.BaseSpellSaveDc;
        public int[] SpellSlots { get; set; } = new int[Characters.SpellSlots.MaxSpellLevel];
        public List<string> KnownAbilities { get; set; } = new List<string>();
        public Dictionary<string, int> Cooldowns { get; } = new Dictionary<string, int>();
        public List<ActiveCondition> Conditions { get; } = new List<ActiveCondition>();
        public GridPosition Position { get; set; }
        public int Initiative { get; set; }
        public bool IsDodging { get; set; }
        public bool HasAction { get; set; }
        public bool HasBonusAction { get; set; }
        public int MovementLeft { get; set; }
        public int XpValue { get; set; }
        public string LootTableId { get; set; } = string.Empty;

        public bool IsAlive => Hp > 0;

        public bool Has(Condition condition)
        {
            return Conditions.Exists(active => active.Condition == condition);
        }

        public void ResetTurnResources()
        {
            HasAction = true;
            HasBonusAction = true;
            IsDodging = false;
            MovementLeft = Has(Condition.Restrained) ? 0 : SpeedCells;
        }

        public void AddCondition(Condition condition, int rounds, long sourceId)
        {
            Conditions.RemoveAll(active => active.Condition == condition);
            Conditions.Add(new ActiveCondition(condition, rounds, sourceId));
        }

        public bool HasResourceFor(ActionCost cost)
        {
            switch (cost)
            {
                case ActionCost.Action:
                    return HasAction;
                case ActionCost.BonusAction:
                    return HasBonusAction;
                default:
                    return true;
            }
        }

        public void Spend(ActionCost cost)
        {
            if (cost == ActionCost.Action)
            {
                HasAction = false;
            }
            else if (cost == ActionCost.BonusAction)
            {
                HasBonusAction = false;
            }
        }
    }
}
