using System.Collections.Generic;
using Realmforge.Rules.Characters;

namespace Realmforge.Rules.Definitions
{
    public enum AbilityKind
    {
        Spell,
        Skill,
    }

    public enum ActionCost
    {
        Action,
        BonusAction,
        Free,
    }

    public enum TargetKind
    {
        Self,
        Enemy,
        Ally,
        Area,
    }

    public enum EffectType
    {
        Damage,
        Heal,
        Condition,
        Move,
        Summon,
        Aura,
    }

    public enum DamageType
    {
        Slashing,
        Piercing,
        Bludgeoning,
        Fire,
        Cold,
        Lightning,
        Poison,
        Necrotic,
        Radiant,
        Force,
        Psychic,
    }

    public enum Condition
    {
        Prone,
        Poisoned,
        Stunned,
        Restrained,
        Blessed,
        Shielded,
        Burning,
        Frightened,
        Invisible,
    }

    public sealed class EffectDefinition
    {
        public EffectType Type { get; set; }
        public string Dice { get; set; } = string.Empty;
        public DamageType DamageType { get; set; } = DamageType.Force;
        public Condition Condition { get; set; }
        public int DurationRounds { get; set; } = 1;
        /// <summary>Positive pushes away from the caster, negative pulls; for Move effects.</summary>
        public int DistanceCells { get; set; }
        public string SummonName { get; set; } = string.Empty;
        public int SummonHp { get; set; }
        public int SummonArmorClass { get; set; }
        public string SummonDamage { get; set; } = string.Empty;
        /// <summary>Saving throw success halves damage instead of negating it.</summary>
        public bool HalfOnSave { get; set; }
    }

    public sealed class AbilityDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public AbilityKind Kind { get; set; } = AbilityKind.Skill;
        public ActionCost Cost { get; set; } = ActionCost.Action;
        public TargetKind Target { get; set; } = TargetKind.Enemy;
        public int RangeCells { get; set; } = 1;
        public int AreaRadiusCells { get; set; }
        /// <summary>0 = cantrip/at-will; otherwise consumes a spell slot of this level.</summary>
        public int SpellLevel { get; set; }
        public int CooldownRounds { get; set; }
        public bool AttackRoll { get; set; }
        public Ability? SaveAbility { get; set; }
        public List<EffectDefinition> Effects { get; set; } = new List<EffectDefinition>();
    }
}
