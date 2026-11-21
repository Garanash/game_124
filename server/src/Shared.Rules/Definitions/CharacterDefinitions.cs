using System.Collections.Generic;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Stats;

namespace Realmforge.Rules.Definitions
{
    /// <summary>
    /// Licensing status of a race name and concept. Only <see cref="Srd"/> content is safe to ship
    /// commercially as-is; the rest must be renamed and redesigned before release.
    /// </summary>
    public enum IpStatus
    {
        Srd,
        NonSrd,
        ThirdParty,
        Original,
    }

    public enum CreatureSize
    {
        Small,
        Medium,
        Large,
    }

    public sealed class RaceDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public IpStatus IpStatus { get; set; } = IpStatus.Original;
        public string ContinentId { get; set; } = string.Empty;
        public string StartZoneId { get; set; } = string.Empty;
        public CreatureSize Size { get; set; } = CreatureSize.Medium;
        public int SpeedCells { get; set; } = 6;
        public Dictionary<Ability, int> AbilityBonuses { get; set; } = new Dictionary<Ability, int>();
        public List<string> Traits { get; set; } = new List<string>();
        public string Portrait { get; set; } = string.Empty;
    }

    public sealed class ClassAbilityUnlock
    {
        public int Level { get; set; }
        public string AbilityId { get; set; } = string.Empty;
    }

    public sealed class ClassDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int HitDie { get; set; } = 8;
        public Ability PrimaryAbility { get; set; }
        public Ability? SpellcastingAbility { get; set; }
        public CasterProgression Caster { get; set; } = CasterProgression.None;
        public List<Ability> SavingThrows { get; set; } = new List<Ability>();
        public int BaseArmorClass { get; set; } = 10;
        public string MentorNpcId { get; set; } = string.Empty;
        public string TrialQuestId { get; set; } = string.Empty;
        public AlignmentRequirement Alignment { get; set; } = new AlignmentRequirement();
        public List<ClassAbilityUnlock> Abilities { get; set; } = new List<ClassAbilityUnlock>();
    }

    public enum PerkSource
    {
        Level,
        Achievement,
    }

    public sealed class PerkDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public PerkSource Source { get; set; } = PerkSource.Level;
        public int MinLevel { get; set; } = 1;
        public List<string> Requires { get; set; } = new List<string>();
        public string AchievementId { get; set; } = string.Empty;
        public List<StatModifier> Modifiers { get; set; } = new List<StatModifier>();
        public List<string> GrantsAbilities { get; set; } = new List<string>();
    }
}
