using Realmforge.Rules.Definitions;

namespace Realmforge.Rules.Combat
{
    public enum CombatCommandType
    {
        Move,
        Attack,
        UseAbility,
        Dodge,
        EndTurn,
    }

    public sealed class CombatCommand
    {
        public CombatCommandType Type { get; set; }
        public GridPosition Cell { get; set; }
        public long TargetId { get; set; }
        public string AbilityId { get; set; } = string.Empty;

        public static CombatCommand Move(GridPosition cell)
        {
            return new CombatCommand { Type = CombatCommandType.Move, Cell = cell };
        }

        public static CombatCommand AttackTarget(long targetId)
        {
            return new CombatCommand { Type = CombatCommandType.Attack, TargetId = targetId };
        }

        public static CombatCommand Use(string abilityId, long targetId, GridPosition cell)
        {
            return new CombatCommand { Type = CombatCommandType.UseAbility, AbilityId = abilityId, TargetId = targetId, Cell = cell };
        }

        public static CombatCommand Dodge()
        {
            return new CombatCommand { Type = CombatCommandType.Dodge };
        }

        public static CombatCommand EndTurn()
        {
            return new CombatCommand { Type = CombatCommandType.EndTurn };
        }
    }

    public enum CombatEventType
    {
        Joined,
        RoundStarted,
        TurnStarted,
        Moved,
        AttackRolled,
        SaveRolled,
        Damaged,
        Healed,
        ConditionApplied,
        ConditionExpired,
        AbilityUsed,
        Summoned,
        Dodged,
        Died,
        TurnEnded,
        EncounterEnded,
    }

    /// <summary>Flat event record so the network layer can relay combat logs without per-type mapping.</summary>
    public sealed class CombatEvent
    {
        public CombatEventType Type { get; set; }
        public long ActorId { get; set; }
        public long TargetId { get; set; }
        public int Value { get; set; }
        public int Roll { get; set; }
        public bool Success { get; set; }
        public GridPosition Cell { get; set; }
        public string AbilityId { get; set; } = string.Empty;
        public Condition Condition { get; set; }
        public DamageType DamageType { get; set; }
    }
}
