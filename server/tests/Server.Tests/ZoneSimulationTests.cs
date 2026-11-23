using Realmforge.Protocol;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Combat;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Dice;
using Realmforge.Rules.Inventory;
using Realmforge.Server.World;
using Xunit;

namespace Realmforge.Server.Tests;

public class ZoneSimulationTests
{
    private const string ZoneId = "test_zone";
    private const int MaxTicksPerFight = ZoneSimulation.TicksPerSecond * 600;
    private static readonly int[] SturdyAbilities = { 16, 14, 14, 10, 10, 10 };

    private static ContentCatalog Catalog()
    {
        var catalog = new ContentCatalog { Version = "test" };
        catalog.Races["dwarf"] = new RaceDefinition { Id = "dwarf", StartZoneId = ZoneId, SpeedCells = 6 };
        catalog.Mobs["rat"] = new MobDefinition
        {
            Id = "rat", Name = "Rat", Hp = 1, ArmorClass = 1, AttackBonus = 0, Damage = "1d1", AggroRadius = 5, Xp = 40, LootTableId = "loot_rat",
        };
        catalog.Mobs["deer"] = new MobDefinition { Id = "deer", Name = "Deer", Hp = 1, ArmorClass = 1, AggroRadius = 0, IsGame = true };
        catalog.LootTables["loot_rat"] = new LootTable { Id = "loot_rat", GoldMin = 3, GoldMax = 3, Entries = { new LootEntry { ItemId = "rat_tail" } } };
        catalog.Items["rat_tail"] = new ItemDefinition { Id = "rat_tail" };
        catalog.Nodes["copper_vein"] = new GatheringNodeDefinition { Id = "copper_vein", ProfessionId = "mining", MinSkill = 1, GatherSeconds = 2, RespawnSeconds = 60 };
        catalog.Zones[ZoneId] = new ZoneDefinition
        {
            Id = ZoneId,
            Spawn = new WorldPoint(100, 100),
            Mobs = { new MobSpawnDefinition { MobId = "rat", Position = new WorldPoint(300, 300), RespawnSeconds = 30 } },
            Nodes = { new NodeSpawnDefinition { NodeId = "copper_vein", Position = new WorldPoint(102, 100) } },
            Portals = { new PortalDefinition { ToZoneId = "other_zone", Position = new WorldPoint(98, 100), Arrival = new WorldPoint(5, 5) } },
        };
        return catalog;
    }

    private static ZoneSimulation Simulation(ContentCatalog catalog)
    {
        return new ZoneSimulation(catalog.Zones[ZoneId], catalog, new SeededRandom(42), startUnixSeconds: 0);
    }

    private static PlayerJoin Join(string name, Guid? partyId = null)
    {
        var sheet = new CharacterSheet { Id = Guid.NewGuid(), Name = name, RaceId = "dwarf", BaseAbilities = SturdyAbilities, ZoneId = ZoneId, PositionX = 100, PositionZ = 100 };
        return new PlayerJoin(sheet, new Equipment(), partyId, null, "faction_test", Array.Empty<string>());
    }

    private static void MovePlayerNextTo(ZoneSimulation simulation, PlayerEntity player, float x, float z)
    {
        simulation.Handle(player.Id, new MoveToRequest { X = x, Z = z });
        for (int tick = 0; tick < ZoneSimulation.TicksPerSecond * 120 && !player.InCombat && player.DistanceTo(x, z) > 0.1f; tick++)
        {
            simulation.Tick();
        }
    }

    /// <summary>Plays the player side by attacking the nearest enemy each turn until the fight resolves.</summary>
    private static void FightToTheEnd(ZoneSimulation simulation, PlayerEntity player)
    {
        for (int tick = 0; tick < MaxTicksPerFight && player.InCombat; tick++)
        {
            WorldEncounter encounter = simulation.Encounters[player.EncounterId!.Value];
            Combatant? current = encounter.Encounter.Current;
            if (current != null && current.EntityId == player.Id)
            {
                Combatant target = encounter.Encounter.Combatants.Where(c => c.Team != current.Team && c.IsAlive).OrderBy(c => c.Position.DistanceTo(current.Position)).First();
                if (target.Position.DistanceTo(current.Position) > 1 && current.MovementLeft > 0)
                {
                    GridPosition approach = encounter.Encounter.Grid.NearestFreeCell(target.Position, encounter.Encounter.IsOccupied) ?? target.Position;
                    simulation.Handle(player.Id, new CombatCommandRequest { CommandType = (byte)CombatCommandType.Move, CellX = approach.X, CellY = approach.Y });
                }
                simulation.Handle(player.Id, new CombatCommandRequest { CommandType = (byte)CombatCommandType.Attack, TargetId = target.EntityId });
                simulation.Handle(player.Id, new CombatCommandRequest { CommandType = (byte)CombatCommandType.EndTurn });
            }
            simulation.Tick();
        }
    }

    [Fact]
    public void WelcomeIsSentOnJoin()
    {
        ZoneSimulation simulation = Simulation(Catalog());

        PlayerEntity player = simulation.AddPlayer(Join("Brom"));

        var welcome = Assert.IsType<WelcomeMessage>(simulation.DrainOutbox().Single(entry => entry.PlayerEntityId == player.Id).Message);
        Assert.Equal(player.Id, welcome.EntityId);
        Assert.True(player.MaxHp > 0);
    }

    [Fact]
    public void InterestManagementSpawnsOnlyNearbyEntities()
    {
        ZoneSimulation simulation = Simulation(Catalog());
        PlayerEntity player = simulation.AddPlayer(Join("Brom"));

        simulation.Tick();
        simulation.Tick();

        List<EntitySpawnMessage> spawns = simulation.DrainOutbox().Select(entry => entry.Message).OfType<EntitySpawnMessage>().ToList();
        Assert.Contains(spawns, spawn => spawn.Kind == EntityKind.GatheringNode);
        Assert.DoesNotContain(spawns, spawn => spawn.Kind == EntityKind.Mob);
    }

    [Fact]
    public void AggroStartsEncounterAndVictoryGrantsRewardsAndQuestKill()
    {
        ZoneSimulation simulation = Simulation(Catalog());
        PlayerEntity player = simulation.AddPlayer(Join("Brom"));

        MovePlayerNextTo(simulation, player, 300, 300);
        Assert.True(player.InCombat);
        FightToTheEnd(simulation, player);

        IReadOnlyList<ZoneEffect> effects = simulation.DrainEffects();
        GrantEncounterRewards rewards = effects.OfType<GrantEncounterRewards>().Single();
        Assert.Equal(40, rewards.Xp);
        Assert.Equal(3, rewards.Loot.Gold);
        Assert.Contains(effects.OfType<ReportQuestEvent>(), quest => quest.Event.TargetId == "rat");
        Assert.True(simulation.Entities.OfType<MobEntity>().Single().IsDead);
    }

    [Fact]
    public void NearbyPartyMembersJoinTheFight()
    {
        ZoneSimulation simulation = Simulation(Catalog());
        Guid party = Guid.NewGuid();
        PlayerEntity leader = simulation.AddPlayer(Join("Brom", party));
        PlayerEntity friend = simulation.AddPlayer(Join("Hilda", party));
        friend.X = leader.X;
        friend.Z = leader.Z;

        simulation.Handle(friend.Id, new MoveToRequest { X = 296, Z = 296 });
        MovePlayerNextTo(simulation, leader, 300, 300);

        Assert.True(leader.InCombat);
        Assert.Equal(leader.EncounterId, friend.EncounterId);
    }

    [Fact]
    public void HuntingPassiveGameRequiresEngaging()
    {
        ContentCatalog catalog = Catalog();
        catalog.Zones[ZoneId].Mobs[0].MobId = "deer";
        ZoneSimulation simulation = Simulation(catalog);
        PlayerEntity player = simulation.AddPlayer(Join("Brom"));
        MobEntity deer = simulation.Entities.OfType<MobEntity>().Single();

        MovePlayerNextTo(simulation, player, deer.X + 1, deer.Z);
        Assert.False(player.InCombat);

        simulation.Handle(player.Id, new InteractRequest { TargetEntityId = deer.Id });
        Assert.True(player.InCombat);
    }

    [Fact]
    public void GatheringRequiresProfessionSkillAndPutsNodeOnCooldown()
    {
        ZoneSimulation simulation = Simulation(Catalog());
        PlayerEntity player = simulation.AddPlayer(Join("Brom"));
        NodeEntity node = simulation.Entities.OfType<NodeEntity>().Single();

        simulation.Handle(player.Id, new InteractRequest { TargetEntityId = node.Id });
        for (int tick = 0; tick < ZoneSimulation.TicksPerSecond * 3; tick++)
        {
            simulation.Tick();
        }

        Assert.Equal("copper_vein", simulation.DrainEffects().OfType<NodeGathered>().Single().NodeId);
        Assert.False(node.IsAvailable(simulation.Now));
    }

    [Fact]
    public void PortalRequestsZoneTransfer()
    {
        ZoneSimulation simulation = Simulation(Catalog());
        PlayerEntity player = simulation.AddPlayer(Join("Brom"));

        simulation.Handle(player.Id, new UsePortalRequest { PortalIndex = 0 });

        IReadOnlyList<ZoneEffect> effects = simulation.DrainEffects();
        TransferToZone transfer = effects.OfType<TransferToZone>().Single();
        Assert.Equal("other_zone", transfer.ZoneId);
        Assert.Empty(effects.OfType<SavePosition>());
        Assert.Null(simulation.Find(player.Id));
    }

    [Fact]
    public void DisconnectDuringCombatLetsAiFinishAndThenRemovesCharacter()
    {
        ZoneSimulation simulation = Simulation(Catalog());
        PlayerEntity player = simulation.AddPlayer(Join("Brom"));
        MovePlayerNextTo(simulation, player, 300, 300);
        Assert.True(player.InCombat);

        simulation.Disconnect(player.Id);
        Assert.NotNull(simulation.Find(player.Id));
        for (int tick = 0; tick < MaxTicksPerFight && player.InCombat; tick++)
        {
            simulation.Tick();
        }

        Assert.Null(simulation.Find(player.Id));
        Assert.Contains(simulation.DrainEffects().OfType<SavePosition>(), save => save.CharacterId == player.CharacterId);
    }

    [Fact]
    public void ArenaBoutReportsRatedResultWithoutAlignmentPenalty()
    {
        ContentCatalog catalog = Catalog();
        catalog.Zones[ZoneId].Pvp = PvpRule.Arena;
        ZoneSimulation simulation = Simulation(catalog);
        PlayerEntity challenger = simulation.AddPlayer(Join("Brom"));
        PlayerEntity rival = simulation.AddPlayer(Join("Hilda"));

        simulation.Handle(challenger.Id, new InteractRequest { TargetEntityId = rival.Id });
        Assert.True(challenger.InCombat && rival.InCombat);
        for (int tick = 0; tick < MaxTicksPerFight && challenger.InCombat; tick++)
        {
            WorldEncounter encounter = simulation.Encounters[challenger.EncounterId!.Value];
            long? current = encounter.Encounter.Current?.EntityId;
            PlayerEntity? actor = current == challenger.Id ? challenger : current == rival.Id ? rival : null;
            if (actor != null)
            {
                PlayerEntity target = actor == challenger ? rival : challenger;
                Combatant self = encounter.Encounter.Find(actor.Id)!;
                GridPosition targetCell = encounter.Encounter.Find(target.Id)!.Position;
                GridPosition approach = encounter.Encounter.Grid.NearestFreeCell(targetCell, encounter.Encounter.IsOccupied) ?? targetCell;
                if (self.Position.DistanceTo(targetCell) > 1)
                {
                    simulation.Handle(actor.Id, new CombatCommandRequest { CommandType = (byte)CombatCommandType.Move, CellX = approach.X, CellY = approach.Y });
                }
                simulation.Handle(actor.Id, new CombatCommandRequest { CommandType = (byte)CombatCommandType.Attack, TargetId = target.Id });
                simulation.Handle(actor.Id, new CombatCommandRequest { CommandType = (byte)CombatCommandType.EndTurn });
            }
            simulation.Tick();
        }

        IReadOnlyList<ZoneEffect> effects = simulation.DrainEffects();
        ArenaMatchFinished result = effects.OfType<ArenaMatchFinished>().Single();
        Assert.NotEqual(result.CharacterId, result.LoserCharacterId);
        Assert.All(effects.OfType<PvpKill>(), kill => Assert.Equal((0, 0, 0), (kill.KillerGoodShift, kill.KillerLawShift, kill.VictimGoldLossPercent)));
    }
}
