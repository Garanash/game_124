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
                if (!catalog.Abilities.TryGetValue(abilityId, out AbilityDefinition? ability) || !IsReady(self, ability))
                {
                    continue;
                }
                CombatCommand? command = ability.Target switch
                {
                    TargetKind.Ally => ChooseAllyTarget(encounter, self, ability),
                    TargetKind.Self => ChooseSelfBuff(self, target, ability),
                    TargetKind.Area => ChooseAreaCell(encounter, self, target, ability),
                    _ => self.Position.DistanceTo(target.Position) <= ability.RangeCells
                        ? CombatCommand.Use(ability.Id, target.EntityId, target.Position)
                        : null,
                };
                if (command != null)
                {
                    return command;
                }
            }
            return null;
        }

        private static bool IsReady(Combatant self, AbilityDefinition ability)
        {
            bool onCooldown = self.Cooldowns.TryGetValue(ability.Id, out int cooldown) && cooldown > 0;
            bool hasSlot = ability.SpellLevel == 0 || self.SpellSlots.Skip(ability.SpellLevel - 1).Any(slots => slots > 0);
            return self.HasResourceFor(ability.Cost) && !onCooldown && hasSlot;
        }

        private static CombatCommand? ChooseAllyTarget(Encounter encounter, Combatant self, AbilityDefinition ability)
        {
            IEnumerable<Combatant> alliesInRange = encounter.Combatants
                .Where(c => c.IsAlive && c.Team == self.Team && c.Position.DistanceTo(self.Position) <= ability.RangeCells);
            bool heals = ability.Effects.Any(effect => effect.Type == EffectType.Heal);
            Combatant? ally = heals
                ? alliesInRange.FirstOrDefault(c => c.Hp < c.MaxHp * HealThreshold)
                : alliesInRange.FirstOrDefault(c => !HasAllConditions(c, ability));
            return ally != null ? CombatCommand.Use(ability.Id, ally.EntityId, ally.Position) : null;
        }

        /// <summary>Buffs are worth a turn only once the fight is about to reach the mob, and never while still active.</summary>
        private static CombatCommand? ChooseSelfBuff(Combatant self, Combatant nearestEnemy, AbilityDefinition ability)
        {
            int threatRange = self.SpeedCells + self.Attack.RangeCells;
            bool engaged = self.Position.DistanceTo(nearestEnemy.Position) <= threatRange;
            return engaged && !HasAllConditions(self, ability) ? CombatCommand.Use(ability.Id, self.EntityId, self.Position) : null;
        }

        /// <summary>Range 0 means the area is centred on the caster (howls, tail sweeps), so it needs enemies close by.</summary>
        private static CombatCommand? ChooseAreaCell(Encounter encounter, Combatant self, Combatant target, AbilityDefinition ability)
        {
            if (ability.RangeCells == 0)
            {
                bool enemyNearby = encounter.Combatants.Any(c => c.IsAlive && c.Team != self.Team
                    && c.Position.DistanceTo(self.Position) <= ability.AreaRadiusCells);
                return enemyNearby ? CombatCommand.Use(ability.Id, self.EntityId, self.Position) : null;
            }
            return self.Position.DistanceTo(target.Position) <= ability.RangeCells
                ? CombatCommand.Use(ability.Id, target.EntityId, target.Position)
                : null;
        }

        private static bool HasAllConditions(Combatant combatant, AbilityDefinition ability)
        {
            List<EffectDefinition> conditions = ability.Effects.Where(effect => effect.Type == EffectType.Condition).ToList();
            return conditions.Count > 0 && conditions.All(effect => combatant.Has(effect.Condition));
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
