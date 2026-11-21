using Realmforge.Rules.Combat;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Dice;
using Xunit;

namespace Realmforge.Rules.Tests;

public class CombatTests
{
    private const int PlayerTeam = 1;
    private const int MobTeam = 2;

    private static Encounter NewEncounter(ContentCatalog catalog, ulong seed = 7)
    {
        return new Encounter(Guid.NewGuid(), new CombatGrid(12, 12, 0, 0), new SeededRandom(seed), catalog, firstSummonId: -1000);
    }

    private static Combatant Fighter(long id, int team, bool player)
    {
        return new Combatant(id, "c" + id, team)
        {
            IsPlayerControlled = player,
            MaxHp = 30,
            Hp = 30,
            ArmorClass = 12,
            SpeedCells = 6,
            Attack = new AttackProfile { AttackBonus = 5, Damage = "1d8", DamageBonus = 3 },
            SpellAttackBonus = 5,
            SpellSaveDc = 13,
            SpellSlots = new[] { 2, 0, 0, 0, 0, 0, 0, 0, 0 },
            KnownAbilities = new List<string> { "fire_bolt", "thunderwave" },
        };
    }

    [Fact]
    public void Only_the_current_combatant_may_act()
    {
        var catalog = TestCatalog.Create();
        Encounter encounter = NewEncounter(catalog);
        encounter.Join(Fighter(1, PlayerTeam, true), new GridPosition(1, 1));
        encounter.Join(Fighter(2, PlayerTeam, true), new GridPosition(2, 1));
        encounter.Join(Fighter(3, MobTeam, true), new GridPosition(8, 8));
        encounter.Start();

        long current = encounter.Current!.EntityId;
        long other = encounter.Combatants.First(c => c.EntityId != current).EntityId;

        Assert.Equal("combat.not_your_turn", encounter.Execute(other, CombatCommand.EndTurn()).Code);
        Assert.True(encounter.Execute(current, CombatCommand.EndTurn()).Success);
        Assert.NotEqual(current, encounter.Current!.EntityId);
    }

    [Fact]
    public void Movement_is_limited_by_speed()
    {
        var catalog = TestCatalog.Create();
        Encounter encounter = NewEncounter(catalog);
        Combatant hero = Fighter(1, PlayerTeam, true);
        encounter.Join(hero, new GridPosition(0, 0));
        encounter.Join(Fighter(2, MobTeam, true), new GridPosition(11, 11));
        encounter.Start();
        if (encounter.Current != hero)
        {
            encounter.Execute(encounter.Current!.EntityId, CombatCommand.EndTurn());
        }

        Assert.Equal("combat.unreachable", encounter.Execute(1, CombatCommand.Move(new GridPosition(7, 0))).Code);
        Assert.True(encounter.Execute(1, CombatCommand.Move(new GridPosition(4, 4))).Success);
        Assert.Equal(2, hero.MovementLeft);
    }

    [Fact]
    public void Ai_mobs_fight_until_one_team_remains()
    {
        var catalog = TestCatalog.Create();
        Encounter encounter = NewEncounter(catalog, seed: 99);
        encounter.Join(Fighter(1, PlayerTeam, false), new GridPosition(0, 0));
        encounter.Join(Fighter(2, MobTeam, false), new GridPosition(10, 10));
        encounter.Start();

        for (int tick = 0; tick < 500 && !encounter.IsFinished; tick++)
        {
            encounter.Tick(0.05f);
        }

        Assert.True(encounter.IsFinished);
        Assert.NotNull(encounter.WinningTeam);
        Assert.Contains(encounter.DrainEvents(), e => e.Type == CombatEventType.Died);
    }

    [Fact]
    public void Idle_player_dodges_and_passes_turn_after_timeout()
    {
        var catalog = TestCatalog.Create();
        Encounter encounter = NewEncounter(catalog);
        encounter.Join(Fighter(1, PlayerTeam, true), new GridPosition(0, 0));
        encounter.Join(Fighter(2, MobTeam, true), new GridPosition(10, 10));
        encounter.Start();
        Combatant idle = encounter.Current!;

        encounter.Tick(Encounter.TurnTimeoutSeconds + 1);

        Assert.True(idle.IsDodging);
        Assert.NotEqual(idle, encounter.Current);
    }

    [Fact]
    public void Area_spell_consumes_slot_and_spares_allies()
    {
        var catalog = TestCatalog.Create();
        Encounter encounter = NewEncounter(catalog);
        Combatant caster = Fighter(1, PlayerTeam, true);
        caster.InitiativeBonus = 100;
        Combatant ally = Fighter(2, PlayerTeam, true);
        Combatant enemy = Fighter(3, MobTeam, true);
        encounter.Join(caster, new GridPosition(0, 0));
        encounter.Join(ally, new GridPosition(5, 4));
        encounter.Join(enemy, new GridPosition(5, 5));
        encounter.Start();

        var result = encounter.Execute(1, CombatCommand.Use("thunderwave", 0, new GridPosition(5, 5)));

        Assert.True(result.Success, result.ToString());
        Assert.Equal(1, caster.SpellSlots[0]);
        Assert.Equal(ally.MaxHp, ally.Hp);
        Assert.True(enemy.Hp < enemy.MaxHp);
    }

    private static (Encounter Encounter, Combatant Mob) MobNextTo(ContentCatalog catalog, string abilityId, GridPosition heroCell)
    {
        Encounter encounter = NewEncounter(catalog);
        Combatant mob = Fighter(2, MobTeam, false);
        mob.KnownAbilities = new List<string> { abilityId };
        mob.HasAction = true;
        mob.HasBonusAction = true;
        encounter.Join(Fighter(1, PlayerTeam, true), heroCell);
        encounter.Join(mob, new GridPosition(5, 5));
        return (encounter, mob);
    }

    [Fact]
    public void Mob_uses_caster_centred_area_only_when_enemies_are_close()
    {
        var catalog = TestCatalog.Create();
        catalog.Abilities["tail_sweep"] = new AbilityDefinition
        {
            Id = "tail_sweep",
            Target = TargetKind.Area,
            RangeCells = 0,
            AreaRadiusCells = 2,
            Effects = { new EffectDefinition { Type = EffectType.Damage, Dice = "2d8" } },
        };

        (Encounter close, Combatant closeMob) = MobNextTo(catalog, "tail_sweep", new GridPosition(6, 6));
        CombatCommand sweep = MobBrain.NextCommand(close, closeMob, catalog);
        (Encounter far, Combatant farMob) = MobNextTo(catalog, "tail_sweep", new GridPosition(10, 10));

        Assert.Equal(CombatCommandType.UseAbility, sweep.Type);
        Assert.Equal(closeMob.Position, sweep.Cell);
        Assert.NotEqual(CombatCommandType.UseAbility, MobBrain.NextCommand(far, farMob, catalog).Type);
    }

    [Fact]
    public void Mob_raises_self_buff_once_when_engaged()
    {
        var catalog = TestCatalog.Create();
        catalog.Abilities["bone_shield"] = new AbilityDefinition
        {
            Id = "bone_shield",
            Target = TargetKind.Self,
            Cost = ActionCost.BonusAction,
            Effects = { new EffectDefinition { Type = EffectType.Condition, Condition = Condition.Shielded, DurationRounds = 2 } },
        };
        (Encounter encounter, Combatant mob) = MobNextTo(catalog, "bone_shield", new GridPosition(6, 6));

        CombatCommand first = MobBrain.NextCommand(encounter, mob, catalog);
        mob.AddCondition(Condition.Shielded, 2, mob.EntityId);
        CombatCommand second = MobBrain.NextCommand(encounter, mob, catalog);

        Assert.Equal(CombatCommandType.UseAbility, first.Type);
        Assert.Equal(mob.EntityId, first.TargetId);
        Assert.NotEqual(CombatCommandType.UseAbility, second.Type);
    }

    [Fact]
    public void Late_joiner_enters_turn_order()
    {
        var catalog = TestCatalog.Create();
        Encounter encounter = NewEncounter(catalog);
        encounter.Join(Fighter(1, PlayerTeam, true), new GridPosition(0, 0));
        encounter.Join(Fighter(2, MobTeam, true), new GridPosition(10, 10));
        encounter.Start();
        long current = encounter.Current!.EntityId;

        encounter.Join(Fighter(3, PlayerTeam, true), new GridPosition(0, 0));

        Assert.Equal(3, encounter.TurnOrder.Count);
        Assert.Equal(current, encounter.Current!.EntityId);
        Assert.NotEqual(new GridPosition(0, 0), encounter.Find(3)!.Position);
    }
}
