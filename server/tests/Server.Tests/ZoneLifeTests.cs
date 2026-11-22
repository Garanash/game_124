using Realmforge.Rules.Definitions;
using Realmforge.Rules.Dice;
using Realmforge.Server.World;
using Xunit;

namespace Realmforge.Server.Tests;

public class ZoneLifeTests
{
    private const string ZoneId = "life_zone";
    private const int SimulatedSeconds = 120;
    private const float Tolerance = 0.5f;
    private static readonly WorldPoint HerdSpawn = new(200, 200);
    private static readonly WorldPoint GuardPost = new(120, 120);
    private static readonly WorldPoint MerchantStall = new(80, 80);

    private static ZoneSimulation Simulation()
    {
        var catalog = new ContentCatalog { Version = "test" };
        catalog.Mobs["deer"] = new MobDefinition { Id = "deer", Name = "Deer", Hp = 4, IsGame = true };
        catalog.Mobs["dummy"] = new MobDefinition { Id = "dummy", Name = "Dummy", Hp = 4, SpeedCells = 0 };
        catalog.Npcs["guard"] = Npc("guard", "patrol_gate", GuardPost);
        catalog.Npcs["merchant"] = Npc("merchant", "trade", MerchantStall);
        catalog.Zones[ZoneId] = new ZoneDefinition
        {
            Id = ZoneId,
            SizeMeters = 512,
            Spawn = new WorldPoint(10, 10),
            Mobs =
            {
                new MobSpawnDefinition { MobId = "deer", Position = HerdSpawn, Count = 3 },
                new MobSpawnDefinition { MobId = "dummy", Position = new WorldPoint(300, 300) },
            },
        };
        return new ZoneSimulation(catalog.Zones[ZoneId], catalog, new SeededRandom(7), startUnixSeconds: 0);
    }

    private static NpcDefinition Npc(string id, string activity, WorldPoint position) => new()
    {
        Id = id,
        Name = id,
        ZoneId = ZoneId,
        Schedule = { new ScheduleEntry { FromHour = 0, ToHour = 24, Activity = activity, Position = position } },
    };

    private static Dictionary<long, float> TravelPerEntity(ZoneSimulation simulation, Action<WorldEntity> inspect)
    {
        var travelled = new Dictionary<long, float>();
        var previous = simulation.Entities.ToDictionary(entity => entity.Id, entity => (entity.X, entity.Z));
        for (int tick = 0; tick < ZoneSimulation.TicksPerSecond * SimulatedSeconds; tick++)
        {
            simulation.Tick();
            foreach (WorldEntity entity in simulation.Entities)
            {
                (float x, float z) = previous[entity.Id];
                travelled[entity.Id] = travelled.GetValueOrDefault(entity.Id) + entity.DistanceTo(x, z);
                previous[entity.Id] = (entity.X, entity.Z);
                inspect(entity);
            }
        }
        return travelled;
    }

    [Fact]
    public void CreaturesRoamButStayNearTheirSpawn()
    {
        ZoneSimulation simulation = Simulation();
        const float SpawnScatter = 6f;

        Dictionary<long, float> travelled = TravelPerEntity(simulation, entity =>
        {
            if (entity is MobEntity { Definition.Id: "deer" } deer)
            {
                Assert.True(deer.DistanceTo(HerdSpawn.X, HerdSpawn.Z) <= ZoneSimulation.MobWanderRadiusMeters + SpawnScatter * 1.5f + Tolerance);
            }
        });

        List<MobEntity> mobs = simulation.Entities.OfType<MobEntity>().ToList();
        Assert.All(mobs.Where(mob => mob.Definition.Id == "deer"), deer => Assert.True(travelled[deer.Id] > 3f));
        Assert.Equal(0f, travelled[mobs.Single(mob => mob.Definition.Id == "dummy").Id]);
    }

    [Fact]
    public void GuardsPatrolTheirPostWhileMerchantsStayAtTheStall()
    {
        ZoneSimulation simulation = Simulation();
        float guardFarthest = 0;
        float merchantFarthest = 0;

        Dictionary<long, float> travelled = TravelPerEntity(simulation, entity =>
        {
            if (entity is NpcEntity { Definition.Id: "guard" } guard && guard.HasAnchor)
            {
                guardFarthest = Math.Max(guardFarthest, guard.DistanceTo(GuardPost.X, GuardPost.Z));
            }
            if (entity is NpcEntity { Definition.Id: "merchant" } merchant && merchant.HasAnchor)
            {
                merchantFarthest = Math.Max(merchantFarthest, merchant.DistanceTo(MerchantStall.X, MerchantStall.Z));
            }
        });

        NpcEntity guardEntity = simulation.Entities.OfType<NpcEntity>().Single(npc => npc.Definition.Id == "guard");
        Assert.True(travelled[guardEntity.Id] > 10f);
        Assert.InRange(guardFarthest, 1f, ZoneSimulation.NpcPatrolRadiusMeters + Tolerance);
        Assert.InRange(merchantFarthest, 0f, ZoneSimulation.NpcFidgetRadiusMeters + Tolerance);
    }
}
