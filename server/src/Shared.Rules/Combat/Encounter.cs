using System;
using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Dice;

namespace Realmforge.Rules.Combat
{
    /// <summary>
    /// A turn-based battle spawned on top of the real-time world. Players and mobs act in initiative order;
    /// players who idle past <see cref="TurnTimeoutSeconds"/> automatically Dodge so they never stall the group.
    /// </summary>
    public sealed class Encounter
    {
        public const float TurnTimeoutSeconds = 30f;
        private const int BlessDieSides = 4;
        private const int ShieldArmorBonus = 5;
        private const int BurningDamageSides = 4;
        private const int MaxAiCommandsPerTurn = 8;
        private const int MaxTurnAdvancesPerCall = 1000;

        private static readonly HashSet<Condition> HarmfulConditions = new HashSet<Condition>
        {
            Condition.Prone, Condition.Poisoned, Condition.Stunned, Condition.Restrained, Condition.Burning, Condition.Frightened,
        };

        private readonly List<Combatant> _combatants = new List<Combatant>();
        private readonly List<Combatant> _turnOrder = new List<Combatant>();
        private readonly List<CombatEvent> _events = new List<CombatEvent>();
        private readonly IRandomSource _random;
        private readonly ContentCatalog _catalog;
        private long _nextSummonId;
        private int _turnIndex = -1;
        private float _turnElapsed;

        public Encounter(Guid id, CombatGrid grid, IRandomSource random, ContentCatalog catalog, long firstSummonId)
        {
            Id = id;
            Grid = grid;
            _random = random;
            _catalog = catalog;
            _nextSummonId = firstSummonId;
        }

        public Guid Id { get; }
        public CombatGrid Grid { get; }
        public int Round { get; private set; }
        public bool IsStarted { get; private set; }
        public bool IsFinished { get; private set; }
        public int? WinningTeam { get; private set; }
        public IReadOnlyList<Combatant> Combatants => _combatants;
        public IReadOnlyList<Combatant> TurnOrder => _turnOrder;
        public Combatant? Current => _turnIndex >= 0 && _turnIndex < _turnOrder.Count ? _turnOrder[_turnIndex] : null;
        public float TurnTimeRemaining => Math.Max(0, TurnTimeoutSeconds - _turnElapsed);

        public Combatant? Find(long entityId)
        {
            return _combatants.FirstOrDefault(combatant => combatant.EntityId == entityId);
        }

        public bool IsOccupied(GridPosition cell)
        {
            return _combatants.Any(combatant => combatant.IsAlive && combatant.Position == cell);
        }

        public List<CombatEvent> DrainEvents()
        {
            var drained = new List<CombatEvent>(_events);
            _events.Clear();
            return drained;
        }

        public void Join(Combatant combatant, GridPosition desiredCell)
        {
            GridPosition cell = desiredCell;
            if (Grid.IsBlocked(cell) || IsOccupied(cell))
            {
                cell = Grid.NearestFreeCell(desiredCell, IsOccupied) ?? desiredCell;
            }
            combatant.Position = cell;
            combatant.Initiative = DiceRoller.Roll(_random, DiceRoller.D20) + combatant.InitiativeBonus;
            _combatants.Add(combatant);
            if (IsStarted)
            {
                InsertIntoTurnOrder(combatant);
            }
            Emit(CombatEventType.Joined, combatant.EntityId, 0, combatant.Initiative, cell: cell);
        }

        public void Start()
        {
            if (IsStarted)
            {
                return;
            }
            IsStarted = true;
            _turnOrder.AddRange(_combatants.OrderByDescending(c => c.Initiative)
                .ThenByDescending(c => c.Abilities.Modifier(Ability.Dexterity))
                .ThenBy(c => c.EntityId));
            Round = 1;
            Emit(CombatEventType.RoundStarted, 0, 0, Round);
            _turnIndex = -1;
            AdvanceToNextTurn();
        }

        public RuleResult Execute(long actorId, CombatCommand command)
        {
            if (IsFinished)
            {
                return RuleResult.Fail("combat.finished", "The encounter is over.");
            }
            Combatant? actor = Current;
            if (actor == null || actor.EntityId != actorId)
            {
                return RuleResult.Fail("combat.not_your_turn", "It is not your turn.");
            }
            RuleResult result = Dispatch(actor, command);
            CheckForEnd();
            return result;
        }

        public void Tick(float deltaSeconds)
        {
            if (!IsStarted || IsFinished)
            {
                return;
            }
            Combatant? current = Current;
            if (current == null)
            {
                return;
            }
            if (!current.IsPlayerControlled)
            {
                RunAiTurn(current);
                return;
            }
            _turnElapsed += deltaSeconds;
            if (_turnElapsed >= TurnTimeoutSeconds)
            {
                if (current.HasAction)
                {
                    Dispatch(current, CombatCommand.Dodge());
                }
                Dispatch(current, CombatCommand.EndTurn());
            }
        }

        /// <summary>Fallen combatants of other teams; used by the caller to award XP and loot.</summary>
        public IReadOnlyList<Combatant> DefeatedEnemiesOf(int team)
        {
            return _combatants.Where(c => c.Team != team && !c.IsAlive).ToList();
        }

        private RuleResult Dispatch(Combatant actor, CombatCommand command)
        {
            switch (command.Type)
            {
                case CombatCommandType.Move:
                    return Move(actor, command.Cell);
                case CombatCommandType.Attack:
                    return WeaponAttack(actor, command.TargetId);
                case CombatCommandType.UseAbility:
                    return UseAbility(actor, command);
                case CombatCommandType.Dodge:
                    return Dodge(actor);
                case CombatCommandType.EndTurn:
                    EndCurrentTurn();
                    return RuleResult.Ok();
                default:
                    return RuleResult.Fail("combat.unknown_command", "Unknown command.");
            }
        }

        private void RunAiTurn(Combatant mob)
        {
            for (int step = 0; step < MaxAiCommandsPerTurn && Current == mob && !IsFinished; step++)
            {
                CombatCommand command = MobBrain.NextCommand(this, mob, _catalog);
                RuleResult result = Dispatch(mob, command);
                CheckForEnd();
                if (!result.Success || command.Type == CombatCommandType.EndTurn)
                {
                    break;
                }
            }
            if (Current == mob && !IsFinished)
            {
                EndCurrentTurn();
            }
        }

        private RuleResult Move(Combatant actor, GridPosition target)
        {
            if (actor.MovementLeft <= 0)
            {
                return RuleResult.Fail("combat.no_movement", "No movement left.");
            }
            Dictionary<GridPosition, int> reachable = Grid.ReachableCells(actor.Position, actor.MovementLeft, IsOccupied);
            if (!reachable.TryGetValue(target, out int cost) || cost == 0)
            {
                return RuleResult.Fail("combat.unreachable", "Cell is out of reach.");
            }
            actor.MovementLeft -= cost;
            actor.Position = target;
            Emit(CombatEventType.Moved, actor.EntityId, 0, cost, cell: target);
            return RuleResult.Ok();
        }

        private RuleResult WeaponAttack(Combatant actor, long targetId)
        {
            Combatant? target = Find(targetId);
            if (target == null || !target.IsAlive || target.Team == actor.Team)
            {
                return RuleResult.Fail("combat.invalid_target", "Invalid target.");
            }
            if (!actor.HasAction)
            {
                return RuleResult.Fail("combat.no_action", "No action left.");
            }
            if (actor.Position.DistanceTo(target.Position) > actor.Attack.RangeCells)
            {
                return RuleResult.Fail("combat.out_of_range", "Target is out of range.");
            }
            actor.Spend(ActionCost.Action);
            bool isMelee = actor.Attack.RangeCells <= 1;
            if (!RollAttack(actor, target, actor.Attack.AttackBonus, isMelee, out bool critical))
            {
                return RuleResult.Ok();
            }
            DiceExpression damage = DiceExpression.Parse(actor.Attack.Damage);
            int amount = (critical ? damage.RollCritical(_random) : damage.Roll(_random)) + actor.Attack.DamageBonus;
            ApplyDamage(actor, target, amount, actor.Attack.DamageType);
            return RuleResult.Ok();
        }

        private RuleResult UseAbility(Combatant actor, CombatCommand command)
        {
            if (!_catalog.Abilities.TryGetValue(command.AbilityId, out AbilityDefinition? ability)
                || !actor.KnownAbilities.Contains(command.AbilityId))
            {
                return RuleResult.Fail("combat.unknown_ability", "Ability not known.");
            }
            if (!actor.HasResourceFor(ability.Cost))
            {
                return RuleResult.Fail("combat.no_action", "No action of that type left.");
            }
            if (actor.Cooldowns.TryGetValue(ability.Id, out int cooldown) && cooldown > 0)
            {
                return RuleResult.Fail("combat.cooldown", "Ability is on cooldown.");
            }
            int slotIndex = FindSpellSlot(actor, ability.SpellLevel);
            if (ability.SpellLevel > 0 && slotIndex < 0)
            {
                return RuleResult.Fail("combat.no_slot", "No spell slot available.");
            }
            if (!TryResolveTargets(actor, ability, command, out List<Combatant> targets, out RuleResult failure))
            {
                return failure;
            }

            actor.Spend(ability.Cost);
            if (ability.CooldownRounds > 0)
            {
                actor.Cooldowns[ability.Id] = ability.CooldownRounds;
            }
            if (slotIndex >= 0)
            {
                actor.SpellSlots[slotIndex]--;
            }
            Emit(CombatEventType.AbilityUsed, actor.EntityId, command.TargetId, ability.SpellLevel, cell: command.Cell, abilityId: ability.Id);

            foreach (EffectDefinition effect in ability.Effects.Where(e => e.Type == EffectType.Summon || e.Type == EffectType.Aura))
            {
                ApplyCasterCentredEffect(actor, ability, effect);
            }
            List<EffectDefinition> targetEffects = ability.Effects.Where(e => e.Type != EffectType.Summon && e.Type != EffectType.Aura).ToList();
            if (targetEffects.Count == 0)
            {
                return RuleResult.Ok();
            }
            foreach (Combatant target in targets)
            {
                ResolveAbilityOnTarget(actor, target, ability, targetEffects);
            }
            return RuleResult.Ok();
        }

        private void ResolveAbilityOnTarget(Combatant actor, Combatant target, AbilityDefinition ability, List<EffectDefinition> effects)
        {
            bool critical = false;
            if (ability.AttackRoll && !RollAttack(actor, target, actor.SpellAttackBonus, ability.RangeCells <= 1, out critical))
            {
                return;
            }
            bool saved = false;
            if (ability.SaveAbility.HasValue)
            {
                Ability saveAbility = ability.SaveAbility.Value;
                int modifier = target.Abilities.Modifier(saveAbility)
                    + (target.SaveProficiencies.Contains(saveAbility) ? Progression.ProficiencyBonus(target.Level) : 0);
                CheckResult save = Checks.AbilityCheck(_random, modifier, actor.SpellSaveDc);
                saved = save.Success;
                Emit(CombatEventType.SaveRolled, actor.EntityId, target.EntityId, save.Total, save.Roll.Natural, saved);
            }
            bool isAreaAbility = ability.Target == TargetKind.Area;
            foreach (EffectDefinition effect in effects)
            {
                bool harmful = IsHarmful(effect);
                if (isAreaAbility && harmful == (target.Team == actor.Team))
                {
                    continue;
                }
                ApplyTargetEffect(actor, target, effect, critical, saved);
            }
        }

        private bool TryResolveTargets(Combatant actor, AbilityDefinition ability, CombatCommand command, out List<Combatant> targets, out RuleResult failure)
        {
            targets = new List<Combatant>();
            failure = RuleResult.Ok();
            switch (ability.Target)
            {
                case TargetKind.Self:
                    targets.Add(actor);
                    return true;
                case TargetKind.Enemy:
                case TargetKind.Ally:
                    Combatant? target = Find(command.TargetId);
                    bool wantsEnemy = ability.Target == TargetKind.Enemy;
                    if (target == null || !target.IsAlive || (target.Team != actor.Team) != wantsEnemy)
                    {
                        failure = RuleResult.Fail("combat.invalid_target", "Invalid target.");
                        return false;
                    }
                    if (actor.Position.DistanceTo(target.Position) > ability.RangeCells)
                    {
                        failure = RuleResult.Fail("combat.out_of_range", "Target is out of range.");
                        return false;
                    }
                    targets.Add(target);
                    return true;
                case TargetKind.Area:
                    if (actor.Position.DistanceTo(command.Cell) > ability.RangeCells || !Grid.InBounds(command.Cell))
                    {
                        failure = RuleResult.Fail("combat.out_of_range", "Area is out of range.");
                        return false;
                    }
                    targets.AddRange(_combatants.Where(c => c.IsAlive && c.Position.DistanceTo(command.Cell) <= ability.AreaRadiusCells));
                    return true;
                default:
                    failure = RuleResult.Fail("combat.invalid_target", "Unsupported target kind.");
                    return false;
            }
        }

        private void ApplyTargetEffect(Combatant actor, Combatant target, EffectDefinition effect, bool critical, bool saved)
        {
            switch (effect.Type)
            {
                case EffectType.Damage:
                    if (saved && !effect.HalfOnSave)
                    {
                        return;
                    }
                    DiceExpression dice = DiceExpression.Parse(effect.Dice);
                    int amount = critical ? dice.RollCritical(_random) : dice.Roll(_random);
                    ApplyDamage(actor, target, saved ? amount / 2 : amount, effect.DamageType);
                    return;
                case EffectType.Heal:
                    if (!target.IsAlive)
                    {
                        return;
                    }
                    int healed = Math.Min(target.MaxHp - target.Hp, DiceExpression.Parse(effect.Dice).Roll(_random));
                    target.Hp += healed;
                    Emit(CombatEventType.Healed, actor.EntityId, target.EntityId, healed);
                    return;
                case EffectType.Condition:
                    if (saved || !target.IsAlive)
                    {
                        return;
                    }
                    target.AddCondition(effect.Condition, effect.DurationRounds, actor.EntityId);
                    Emit(CombatEventType.ConditionApplied, actor.EntityId, target.EntityId, effect.DurationRounds, condition: effect.Condition);
                    return;
                case EffectType.Move:
                    if (!saved && target.IsAlive)
                    {
                        Push(actor, target, effect.DistanceCells);
                    }
                    return;
            }
        }

        private void ApplyCasterCentredEffect(Combatant actor, AbilityDefinition ability, EffectDefinition effect)
        {
            if (effect.Type == EffectType.Summon)
            {
                var summon = new Combatant(_nextSummonId++, effect.SummonName, actor.Team)
                {
                    MaxHp = effect.SummonHp,
                    Hp = effect.SummonHp,
                    ArmorClass = effect.SummonArmorClass,
                    Level = actor.Level,
                    Attack = new AttackProfile { AttackBonus = actor.SpellAttackBonus, Damage = effect.SummonDamage, DamageType = effect.DamageType },
                };
                Join(summon, actor.Position);
                Emit(CombatEventType.Summoned, actor.EntityId, summon.EntityId, summon.MaxHp, cell: summon.Position);
                return;
            }
            foreach (Combatant ally in _combatants.Where(c => c.IsAlive && c.Team == actor.Team
                                                              && c.Position.DistanceTo(actor.Position) <= ability.AreaRadiusCells))
            {
                ally.AddCondition(effect.Condition, effect.DurationRounds, actor.EntityId);
                Emit(CombatEventType.ConditionApplied, actor.EntityId, ally.EntityId, effect.DurationRounds, condition: effect.Condition);
            }
        }

        private RuleResult Dodge(Combatant actor)
        {
            if (!actor.HasAction)
            {
                return RuleResult.Fail("combat.no_action", "No action left.");
            }
            actor.Spend(ActionCost.Action);
            actor.IsDodging = true;
            Emit(CombatEventType.Dodged, actor.EntityId, 0, 0);
            return RuleResult.Ok();
        }

        private bool RollAttack(Combatant attacker, Combatant target, int attackBonus, bool isMelee, out bool critical)
        {
            bool advantage = attacker.Has(Condition.Invisible) || target.Has(Condition.Restrained)
                             || target.Has(Condition.Stunned) || (isMelee && target.Has(Condition.Prone));
            bool disadvantage = attacker.Has(Condition.Poisoned) || attacker.Has(Condition.Frightened) || target.IsDodging
                                || target.Has(Condition.Invisible) || (!isMelee && target.Has(Condition.Prone));
            D20Result roll = DiceRoller.RollD20(_random, DiceRoller.Combine(advantage, disadvantage));
            int blessBonus = attacker.Has(Condition.Blessed) ? DiceRoller.Roll(_random, BlessDieSides) : 0;
            int total = roll.Natural + attackBonus + blessBonus;
            int armorClass = target.ArmorClass + (target.Has(Condition.Shielded) ? ShieldArmorBonus : 0);
            critical = roll.IsCriticalHit;
            bool hit = !roll.IsCriticalMiss && (critical || total >= armorClass);
            Emit(CombatEventType.AttackRolled, attacker.EntityId, target.EntityId, total, roll.Natural, hit);
            return hit;
        }

        private void ApplyDamage(Combatant source, Combatant target, int amount, DamageType type)
        {
            if (!target.IsAlive)
            {
                return;
            }
            int dealt = Math.Max(0, Math.Min(target.Hp, amount));
            target.Hp -= dealt;
            Emit(CombatEventType.Damaged, source.EntityId, target.EntityId, dealt, damageType: type);
            if (!target.IsAlive)
            {
                target.Conditions.Clear();
                Emit(CombatEventType.Died, source.EntityId, target.EntityId, 0);
            }
        }

        private void Push(Combatant source, Combatant target, int distanceCells)
        {
            int directionX = Math.Sign(target.Position.X - source.Position.X);
            int directionY = Math.Sign(target.Position.Y - source.Position.Y);
            if (distanceCells < 0)
            {
                directionX = -directionX;
                directionY = -directionY;
            }
            if (directionX == 0 && directionY == 0)
            {
                return;
            }
            GridPosition position = target.Position;
            for (int step = 0; step < Math.Abs(distanceCells); step++)
            {
                var next = new GridPosition(position.X + directionX, position.Y + directionY);
                if (Grid.IsBlocked(next) || IsOccupied(next))
                {
                    break;
                }
                position = next;
            }
            if (position != target.Position)
            {
                target.Position = position;
                Emit(CombatEventType.Moved, target.EntityId, source.EntityId, 0, cell: position);
            }
        }

        private void EndCurrentTurn()
        {
            Combatant? current = Current;
            if (current != null)
            {
                FinishTurnFor(current);
            }
            AdvanceToNextTurn();
        }

        private void FinishTurnFor(Combatant combatant)
        {
            foreach (ActiveCondition condition in combatant.Conditions.ToList())
            {
                condition.RemainingRounds--;
                if (condition.RemainingRounds <= 0)
                {
                    combatant.Conditions.Remove(condition);
                    Emit(CombatEventType.ConditionExpired, combatant.EntityId, 0, 0, condition: condition.Condition);
                }
            }
            Emit(CombatEventType.TurnEnded, combatant.EntityId, 0, 0);
        }

        private void AdvanceToNextTurn()
        {
            if (_turnOrder.Count == 0)
            {
                return;
            }
            for (int guard = 0; guard < MaxTurnAdvancesPerCall && !IsFinished; guard++)
            {
                _turnIndex++;
                if (_turnIndex >= _turnOrder.Count)
                {
                    _turnIndex = 0;
                    Round++;
                    Emit(CombatEventType.RoundStarted, 0, 0, Round);
                }
                Combatant combatant = _turnOrder[_turnIndex];
                if (!combatant.IsAlive)
                {
                    continue;
                }
                StartTurnFor(combatant);
                CheckForEnd();
                if (!combatant.IsAlive)
                {
                    continue;
                }
                if (combatant.Has(Condition.Stunned))
                {
                    FinishTurnFor(combatant);
                    continue;
                }
                return;
            }
        }

        private void StartTurnFor(Combatant combatant)
        {
            _turnElapsed = 0;
            combatant.ResetTurnResources();
            foreach (string abilityId in combatant.Cooldowns.Keys.ToList())
            {
                combatant.Cooldowns[abilityId] = Math.Max(0, combatant.Cooldowns[abilityId] - 1);
            }
            Emit(CombatEventType.TurnStarted, combatant.EntityId, 0, Round);
            ActiveCondition? burning = combatant.Conditions.FirstOrDefault(c => c.Condition == Condition.Burning);
            if (burning != null)
            {
                Combatant source = Find(burning.SourceId) ?? combatant;
                ApplyDamage(source, combatant, DiceRoller.Roll(_random, BurningDamageSides), DamageType.Fire);
            }
        }

        private void InsertIntoTurnOrder(Combatant combatant)
        {
            int index = _turnOrder.FindIndex(existing => existing.Initiative < combatant.Initiative);
            if (index < 0)
            {
                index = _turnOrder.Count;
            }
            _turnOrder.Insert(index, combatant);
            if (index <= _turnIndex)
            {
                _turnIndex++;
            }
        }

        private void CheckForEnd()
        {
            if (IsFinished || !IsStarted)
            {
                return;
            }
            List<int> teamsAlive = _combatants.Where(c => c.IsAlive).Select(c => c.Team).Distinct().ToList();
            if (teamsAlive.Count > 1)
            {
                return;
            }
            IsFinished = true;
            WinningTeam = teamsAlive.Count == 1 ? teamsAlive[0] : (int?)null;
            Emit(CombatEventType.EncounterEnded, 0, 0, WinningTeam ?? -1);
        }

        private static int FindSpellSlot(Combatant actor, int spellLevel)
        {
            if (spellLevel <= 0)
            {
                return -1;
            }
            for (int index = spellLevel - 1; index < actor.SpellSlots.Length; index++)
            {
                if (actor.SpellSlots[index] > 0)
                {
                    return index;
                }
            }
            return -1;
        }

        private static bool IsHarmful(EffectDefinition effect)
        {
            switch (effect.Type)
            {
                case EffectType.Damage:
                case EffectType.Move:
                    return true;
                case EffectType.Condition:
                    return HarmfulConditions.Contains(effect.Condition);
                default:
                    return false;
            }
        }

        private void Emit(
            CombatEventType type,
            long actorId,
            long targetId,
            int value,
            int roll = 0,
            bool success = false,
            GridPosition cell = default,
            string abilityId = "",
            Condition condition = Condition.Prone,
            DamageType damageType = DamageType.Force)
        {
            _events.Add(new CombatEvent
            {
                Type = type,
                ActorId = actorId,
                TargetId = targetId,
                Value = value,
                Roll = roll,
                Success = success,
                Cell = cell,
                AbilityId = abilityId,
                Condition = condition,
                DamageType = damageType,
            });
        }
    }
}
