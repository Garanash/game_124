using System;
using System.Collections.Generic;
using System.Linq;
using Realmforge.Protocol;
using Realmforge.Rules.Combat;

namespace Realmforge.Client.Core.World
{
    public sealed class CombatantView
    {
        public long EntityId { get; set; }
        public int Team { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int CellX { get; set; }
        public int CellY { get; set; }
        public bool IsAlive => Hp > 0 || MaxHp == 0;
    }

    /// <summary>Client mirror of one turn-based encounter, rebuilt from EncounterStarted and kept current by combat events.</summary>
    public sealed class CombatState
    {
        public const int MaxLogLines = 50;

        private readonly Dictionary<long, CombatantView> _combatants = new Dictionary<long, CombatantView>();
        private readonly List<string> _log = new List<string>();
        private readonly Func<long, string> _nameOf;

        public CombatState(EncounterStartedMessage started, Func<long, string> nameOf)
        {
            _nameOf = nameOf;
            EncounterId = started.EncounterId;
            OriginX = started.OriginX;
            OriginZ = started.OriginZ;
            Width = started.Width;
            Height = started.Height;
            Grid = new CombatGrid(started.Width, started.Height, started.OriginX, started.OriginZ);
            TurnOrder = started.TurnOrder.ToList();
            foreach (CombatantDto dto in started.Combatants)
            {
                _combatants[dto.EntityId] = new CombatantView
                {
                    EntityId = dto.EntityId, Team = dto.Team, Hp = dto.Hp, MaxHp = dto.MaxHp, CellX = dto.CellX, CellY = dto.CellY,
                };
            }
        }

        public Guid EncounterId { get; }
        public float OriginX { get; }
        public float OriginZ { get; }
        public int Width { get; }
        public int Height { get; }
        public CombatGrid Grid { get; }
        public List<long> TurnOrder { get; }
        public long CurrentTurnEntityId { get; private set; }
        public float TurnTimeRemaining { get; private set; }
        public int Round { get; private set; }
        public IReadOnlyCollection<CombatantView> Combatants => _combatants.Values;
        public IReadOnlyList<string> Log => _log;

        public CombatantView? Find(long entityId) => _combatants.TryGetValue(entityId, out CombatantView? view) ? view : null;

        public bool IsTurnOf(long entityId) => CurrentTurnEntityId == entityId;

        public void Apply(CombatEventsMessage message)
        {
            CurrentTurnEntityId = message.CurrentTurnEntityId;
            TurnTimeRemaining = message.TurnTimeRemaining;
            Round = message.Round;
            foreach (CombatEventDto combatEvent in message.Events)
            {
                ApplyEvent(combatEvent);
                string? line = CombatLogFormatter.Describe(combatEvent, _nameOf);
                if (line != null)
                {
                    AddLog(line);
                }
            }
        }

        public void CellCenter(int cellX, int cellY, out float worldX, out float worldZ)
        {
            Grid.CellToWorld(new GridPosition(cellX, cellY), out worldX, out worldZ);
        }

        /// <summary>Same rounding as the server grid, so the highlighted cell is the one the server will use.</summary>
        public bool TryWorldToCell(float worldX, float worldZ, out int cellX, out int cellY)
        {
            cellX = (int)Math.Round((worldX - OriginX) / CombatGrid.CellSizeMeters);
            cellY = (int)Math.Round((worldZ - OriginZ) / CombatGrid.CellSizeMeters);
            return Grid.InBounds(new GridPosition(cellX, cellY));
        }

        private void ApplyEvent(CombatEventDto combatEvent)
        {
            switch ((CombatEventType)combatEvent.Type)
            {
                case CombatEventType.Joined:
                    if (!_combatants.ContainsKey(combatEvent.ActorId))
                    {
                        _combatants[combatEvent.ActorId] = new CombatantView { EntityId = combatEvent.ActorId, CellX = combatEvent.CellX, CellY = combatEvent.CellY };
                        TurnOrder.Add(combatEvent.ActorId);
                    }
                    break;
                case CombatEventType.Moved when Find(combatEvent.ActorId) is CombatantView mover:
                    mover.CellX = combatEvent.CellX;
                    mover.CellY = combatEvent.CellY;
                    break;
                case CombatEventType.Damaged when Find(combatEvent.TargetId) is CombatantView hurt:
                    hurt.Hp = Math.Max(0, hurt.Hp - combatEvent.Value);
                    break;
                case CombatEventType.Healed when Find(combatEvent.TargetId) is CombatantView healed:
                    healed.Hp = healed.MaxHp > 0 ? Math.Min(healed.MaxHp, healed.Hp + combatEvent.Value) : healed.Hp + combatEvent.Value;
                    break;
                case CombatEventType.Died when Find(combatEvent.TargetId) is CombatantView dead:
                    dead.Hp = 0;
                    break;
            }
        }

        private void AddLog(string line)
        {
            if (_log.Count == MaxLogLines)
            {
                _log.RemoveAt(0);
            }
            _log.Add(line);
        }
    }

    public static class CombatCommands
    {
        public static CombatCommandRequest Move(int cellX, int cellY) => Command(CombatCommandType.Move, cellX, cellY, 0, string.Empty);

        public static CombatCommandRequest Attack(long targetId) => Command(CombatCommandType.Attack, 0, 0, targetId, string.Empty);

        public static CombatCommandRequest UseAbility(string abilityId, long targetId, int cellX, int cellY) =>
            Command(CombatCommandType.UseAbility, cellX, cellY, targetId, abilityId);

        public static CombatCommandRequest Dodge() => Command(CombatCommandType.Dodge, 0, 0, 0, string.Empty);

        public static CombatCommandRequest EndTurn() => Command(CombatCommandType.EndTurn, 0, 0, 0, string.Empty);

        private static CombatCommandRequest Command(CombatCommandType type, int cellX, int cellY, long targetId, string abilityId)
        {
            return new CombatCommandRequest { CommandType = (byte)type, CellX = cellX, CellY = cellY, TargetId = targetId, AbilityId = abilityId };
        }
    }

    public static class CombatLogFormatter
    {
        /// <summary>Russian combat log line, or null for bookkeeping events that would only clutter the log.</summary>
        public static string? Describe(CombatEventDto combatEvent, Func<long, string> nameOf)
        {
            string actor = nameOf(combatEvent.ActorId);
            string target = nameOf(combatEvent.TargetId);
            switch ((CombatEventType)combatEvent.Type)
            {
                case CombatEventType.RoundStarted:
                    return $"— Раунд {combatEvent.Value} —";
                case CombatEventType.TurnStarted:
                    return $"Ход: {actor}";
                case CombatEventType.AttackRolled:
                    return $"{actor} атакует {target}: {combatEvent.Value} (d20 = {combatEvent.Roll}) — {(combatEvent.Success ? "попадание" : "промах")}";
                case CombatEventType.SaveRolled:
                    return $"{target} спасбросок: {combatEvent.Value} — {(combatEvent.Success ? "успех" : "провал")}";
                case CombatEventType.Damaged:
                    return $"{target} получает {combatEvent.Value} урона";
                case CombatEventType.Healed:
                    return $"{target} восстанавливает {combatEvent.Value} здоровья";
                case CombatEventType.ConditionApplied:
                    return $"{target}: состояние {(Rules.Definitions.Condition)combatEvent.Condition}";
                case CombatEventType.AbilityUsed:
                    return $"{actor} применяет {combatEvent.AbilityId}";
                case CombatEventType.Summoned:
                    return $"{actor} призывает союзника";
                case CombatEventType.Dodged:
                    return $"{actor} уклоняется";
                case CombatEventType.Died:
                    return $"{target} повержен";
                case CombatEventType.EncounterEnded:
                    return "Бой окончен";
                default:
                    return null;
            }
        }
    }
}
