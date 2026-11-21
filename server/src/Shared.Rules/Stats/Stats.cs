using System.Collections.Generic;

namespace Realmforge.Rules.Stats
{
    public enum Stat
    {
        MaxHp,
        ArmorClass,
        AttackBonus,
        DamageBonus,
        SpellSaveDc,
        Initiative,
        SpeedCells,
        Strength,
        Dexterity,
        Constitution,
        Intelligence,
        Wisdom,
        Charisma,
        GatheringYieldPercent,
        CraftingSpeedPercent,
        RestXpPercent,
        GoldFindPercent,
    }

    public sealed class StatModifier
    {
        public StatModifier()
        {
        }

        public StatModifier(Stat stat, int value)
        {
            Stat = stat;
            Value = value;
        }

        public Stat Stat { get; set; }
        public int Value { get; set; }
    }

    /// <summary>Additive bag of modifiers aggregated from backpack, perks, housing and auras.</summary>
    public sealed class StatBlock
    {
        private readonly Dictionary<Stat, int> _values = new Dictionary<Stat, int>();

        public int this[Stat stat] => _values.TryGetValue(stat, out int value) ? value : 0;

        public IReadOnlyDictionary<Stat, int> Values => _values;

        public void Add(Stat stat, int value)
        {
            _values[stat] = this[stat] + value;
        }

        public void AddRange(IEnumerable<StatModifier> modifiers)
        {
            foreach (StatModifier modifier in modifiers)
            {
                Add(modifier.Stat, modifier.Value);
            }
        }

        public void Merge(StatBlock other)
        {
            foreach (KeyValuePair<Stat, int> pair in other._values)
            {
                Add(pair.Key, pair.Value);
            }
        }
    }
}
