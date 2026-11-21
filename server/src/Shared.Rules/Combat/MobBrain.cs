using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Definitions;

namespace Realmforge.Rules.Combat
{
    /// <summary>
    /// Greedy tactical AI used for mobs and summons: heal a hurt ally, cast what is in range,
    /// otherwise strike or close the distance to the nearest enemy.
    /// </summary>
    public static class MobBrain
    {
        private const double HealThreshold = 0.5;

        public static CombatCommand NextCommand(Encounter encounter, Combatant self, ContentCatalog catalog)
        {
            List<Combatant> enemies = encounter.Combatants.Where(c => c.IsAlive && c.Team != self.Team).ToList();
            if (enemies.Count == 0)
            {
                return CombatCommand.EndTurn();
            }
            Combatant target = enemies
                .OrderBy(enemy => enemy.Position.DistanceTo(self.Position))
                .ThenBy(enemy => enemy.Hp)
                .First();

            CombatCommand? abilityCommand = ChooseAbility(encounter, self, target, catalog);
            if (abilityCommand != null)
            {
                return abilityCommand;
            }
            int distance = self.Position.DistanceTo(target.Position);
            if (self.HasAction && distance <= self.Attack.RangeCells)
            {
                return CombatCommand.AttackTarget(target.EntityId);
            }
            if (self.MovementLeft > 0 && distance > self.Attack.RangeCells)
            {
                GridPosition? approach = BestApproachCell(encounter, self, target);
                if (approach.HasValue)
                {
                    return CombatCommand.Move(approach.Value);
                }
            }
            return CombatCommand.EndTurn();
        }

        private static CombatCommand? ChooseAbility(Encounter encounter, Combatant self, Combatant target, ContentCatalog catalog)
        {
            foreach (string abilityId in self.KnownAbilities)
            {
                if (!catalog.Abilities.TryGetValue(abilityId, out AbilityDefinition? ability)
                    || !self.HasResourceFor(ability.Cost)
                    || (self.Cooldowns.TryGetValue(abilityId, out int cooldown) && cooldown > 0)
                    || (ability.SpellLevel > 0 && !self.SpellSlots.Skip(ability.SpellLevel - 1).Any(slots => slots > 0)))
                {
                    continue;
                }
                bool heals = ability.Effects.Any(effect => effect.Type == EffectType.Heal);
                if (heals && ability.Target == TargetKind.Ally)
                {
                    Combatant? hurtAlly = encounter.Combatants.FirstOrDefault(c => c.IsAlive && c.Team == self.Team
                        && c.Hp < c.MaxHp * HealThreshold && c.Position.DistanceTo(self.Position) <= ability.RangeCells);
                    if (hurtAlly != null)
                    {
                        return CombatCommand.Use(abilityId, hurtAlly.EntityId, hurtAlly.Position);
                    }
                    continue;
                }
                bool inRange = self.Position.DistanceTo(target.Position) <= ability.RangeCells;
                if ((ability.Target == TargetKind.Enemy || ability.Target == TargetKind.Area) && inRange)
                {
                    return CombatCommand.Use(abilityId, target.EntityId, target.Position);
                }
            }
            return null;
        }

        private static GridPosition? BestApproachCell(Encounter encounter, Combatant self, Combatant target)
        {
            Dictionary<GridPosition, int> reachable = encounter.Grid.ReachableCells(self.Position, self.MovementLeft, encounter.IsOccupied);
            int currentDistance = self.Position.DistanceTo(target.Position);
            GridPosition? best = null;
            int bestDistance = currentDistance;
            int bestCost = int.MaxValue;
            foreach (KeyValuePair<GridPosition, int> cell in reachable)
            {
                int distance = cell.Key.DistanceTo(target.Position);
                if (distance < self.Attack.RangeCells && self.Attack.RangeCells > 1)
                {
                    continue;
                }
                if (distance < bestDistance || (distance == bestDistance && best.HasValue && cell.Value < bestCost))
                {
                    best = cell.Key;
                    bestDistance = distance;
                    bestCost = cell.Value;
                }
            }
            return best;
        }
    }
}
