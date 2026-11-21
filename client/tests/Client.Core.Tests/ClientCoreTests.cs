using System.Numerics;
using Realmforge.Client.Core.Api;
using Realmforge.Client.Core.Camera;
using Realmforge.Client.Core.CharacterCreation;
using Realmforge.Client.Core.Session;
using Realmforge.Client.Core.World;
using Realmforge.Protocol;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Combat;
using Xunit;

namespace Realmforge.Client.Core.Tests;

public class CharacterCreationTests
{
    [Fact]
    public void Race_grid_lists_every_race_under_its_start_zone()
    {
        var model = new CharacterCreationModel(ShippedContent.Value);

        int listed = model.RaceGroups.Sum(group => group.Races.Count);

        Assert.Equal(ShippedContent.Value.Races.Count, listed);
        Assert.All(model.RaceGroups, group => Assert.All(group.Races, race => Assert.Equal(group.StartZone.Id, race.StartZoneId)));
    }

    [Fact]
    public void Point_buy_stops_at_budget()
    {
        var model = new CharacterCreationModel(ShippedContent.Value);

        for (int attempt = 0; attempt < 100; attempt++)
        {
            foreach (Ability ability in Enum.GetValues<Ability>())
            {
                model.Increase(ability);
            }
        }

        Assert.True(model.PointsRemaining >= 0);
        Assert.Equal(PointBuy.Budget, model.PointsSpent + model.PointsRemaining);
        Assert.All(Enum.GetValues<Ability>(), ability => Assert.False(model.CanIncrease(ability) && model.PointsRemaining == 0));
    }

    [Fact]
    public void Named_character_with_spent_points_passes_server_validation()
    {
        var model = new CharacterCreationModel(ShippedContent.Value) { Name = "Арвен" };
        model.SelectRace("elf");
        model.SelectAlignment(AlignmentCell.ChaoticGood);

        Assert.True(model.Validate().Success, model.Validate().Message);
        Assert.Equal("start_silverleaf", model.StartZone?.Id);
        Assert.Equal(AlignmentCell.ChaoticGood, model.ToRequest().Alignment);
    }
}

public class InterpolationTests
{
    [Fact]
    public void Samples_blend_linearly_between_snapshots()
    {
        var buffer = new SnapshotBuffer();
        buffer.Add(1.0, new EntityPose(0, 0, 0));
        buffer.Add(2.0, new EntityPose(10, 20, 0));

        EntityPose middle = buffer.Sample(1.5);

        Assert.Equal(5, middle.X, 3);
        Assert.Equal(10, middle.Z, 3);
        Assert.Equal(10, buffer.Sample(5).X, 3);
    }

    [Fact]
    public void Out_of_order_snapshots_are_ignored()
    {
        var buffer = new SnapshotBuffer();
        buffer.Add(2.0, new EntityPose(1, 1, 0));
        buffer.Add(1.0, new EntityPose(9, 9, 0));

        Assert.Equal(1, buffer.Count);
    }
}

public class WorldStateTests
{
    private const long LocalId = 1;
    private const long MobId = 2;

    [Fact]
    public void Spawn_snapshot_and_despawn_raise_events()
    {
        var world = new WorldState();
        var spawned = new List<long>();
        var despawned = new List<long>();
        world.EntitySpawned += entity => spawned.Add(entity.Id);
        world.EntityDespawned += despawned.Add;

        world.Apply(new WelcomeMessage { EntityId = LocalId, ZoneId = "start_silverleaf", ServerTime = 100 });
        world.Apply(new EntitySpawnMessage { EntityId = MobId, Kind = EntityKind.Mob, X = 1, Z = 1 });
        world.Apply(new WorldSnapshotMessage { ServerTime = 100.05, Entities = new[] { new EntityState { EntityId = MobId, X = 3, Z = 4, Flags = EntityStateFlags.InCombat } } });
        world.Apply(new EntityDespawnMessage { EntityId = MobId });

        Assert.Equal("start_silverleaf", world.ZoneId);
        Assert.Equal(new[] { MobId }, spawned);
        Assert.Equal(new[] { MobId }, despawned);
        Assert.Null(world.Find(MobId));
    }

    [Fact]
    public void Encounter_without_local_player_is_offered_to_join()
    {
        var world = new WorldState();
        world.Apply(new WelcomeMessage { EntityId = LocalId });
        EncounterStartedMessage? offered = null;
        world.EncounterJoinOffered += started => offered = started;

        world.Apply(Encounter(new CombatantDto { EntityId = MobId, Team = 2, Hp = 10, MaxHp = 10 }));

        Assert.NotNull(offered);
        Assert.Null(world.Combat);
    }

    [Fact]
    public void Combat_events_update_hit_points_and_log()
    {
        var world = new WorldState();
        world.Apply(new WelcomeMessage { EntityId = LocalId });
        EncounterStartedMessage started = Encounter(
            new CombatantDto { EntityId = LocalId, Team = 1, Hp = 12, MaxHp = 12 },
            new CombatantDto { EntityId = MobId, Team = 2, Hp = 7, MaxHp = 7 });
        world.Apply(started);

        world.Apply(new CombatEventsMessage
        {
            EncounterId = started.EncounterId,
            CurrentTurnEntityId = MobId,
            Round = 1,
            Events = new[]
            {
                new CombatEventDto { Type = (byte)CombatEventType.Damaged, ActorId = LocalId, TargetId = MobId, Value = 7, AbilityId = string.Empty },
                new CombatEventDto { Type = (byte)CombatEventType.Died, ActorId = LocalId, TargetId = MobId, AbilityId = string.Empty },
            },
        });

        Assert.False(world.Combat!.Find(MobId)!.IsAlive);
        Assert.True(world.Combat.IsTurnOf(MobId));
        Assert.Equal(2, world.Combat.Log.Count);

        world.Apply(new EncounterEndedMessage { EncounterId = started.EncounterId, Victory = true });
        Assert.Null(world.Combat);
    }

    private static EncounterStartedMessage Encounter(params CombatantDto[] combatants)
    {
        return new EncounterStartedMessage
        {
            EncounterId = Guid.NewGuid(),
            Width = 17,
            Height = 17,
            Combatants = combatants,
            TurnOrder = combatants.Select(combatant => combatant.EntityId).ToArray(),
        };
    }
}

public class GatewayAndSessionTests
{
    private const string BaseUrl = "http://gateway";

    [Fact]
    public async Task Login_stores_token_and_sends_it_on_later_requests()
    {
        var http = new FakeHttp()
            .Reply(200, "{\"accountId\":\"6f1c0f5e-9f45-4d3b-8a25-1c0b1b1b1b1b\",\"token\":\"jwt\"}")
            .Reply(200, "[]");
        var gateway = new GatewayClient(http, BaseUrl + "/");

        await gateway.Login("a@b.c", "password1");
        await gateway.ListCharacters();

        Assert.True(gateway.IsLoggedIn);
        Assert.Equal(BaseUrl + "/characters", http.Requests[1].Url);
        Assert.Equal("jwt", http.Requests[1].Token);
    }

    [Fact]
    public async Task Server_error_code_is_surfaced()
    {
        var http = new FakeHttp().Reply(409, "{\"code\":\"auth.email_taken\",\"message\":\"Почта занята\"}");
        var gateway = new GatewayClient(http, BaseUrl);

        ApiException error = await Assert.ThrowsAsync<ApiException>(() => gateway.Register("a@b.c", "password1"));

        Assert.Equal("auth.email_taken", error.Code);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public async Task Entering_world_connects_and_sends_ticket()
    {
        var http = new FakeHttp()
            .Reply(200, "{\"zoneId\":\"start_silverleaf\",\"host\":\"127.0.0.1\",\"udpPort\":7777,\"webSocketPort\":7778,\"ticket\":\"zone-ticket\"}");
        var transport = new FakeTransport();
        var session = new GameSession(new GatewayClient(http, BaseUrl), transport);

        await session.EnterWorld(new CharacterSummaryDto { CharacterId = Guid.NewGuid(), Name = "Арвен" });
        transport.Deliver(ProtocolCodec.Encode(new WelcomeMessage { EntityId = 5, ZoneId = "start_silverleaf" }));

        var hello = Assert.IsType<HelloRequest>(ProtocolCodec.DecodeClient(transport.Sent.Single()));
        Assert.Equal("zone-ticket", hello.Ticket);
        Assert.Equal(("127.0.0.1", 7777, 7778), transport.LastConnect);
        Assert.Equal(SessionPhase.InWorld, session.Phase);
        Assert.Equal(5, session.World.LocalEntityId);
    }

    [Fact]
    public async Task Lost_connection_returns_to_character_select()
    {
        var http = new FakeHttp().Reply(200, "{\"host\":\"h\",\"udpPort\":1,\"webSocketPort\":2,\"ticket\":\"t\"}");
        var transport = new FakeTransport();
        var session = new GameSession(new GatewayClient(http, BaseUrl), transport);
        string? reason = null;
        session.ConnectionLost += text => reason = text;

        await session.EnterWorld(new CharacterSummaryDto { CharacterId = Guid.NewGuid() });
        transport.Drop("Timeout");

        Assert.Equal(SessionPhase.SelectingCharacter, session.Phase);
        Assert.Equal("Timeout", reason);
    }
}

public class CameraTests
{
    [Fact]
    public void Ground_hit_lands_on_plane_and_sky_rays_miss()
    {
        Vector3? hit = IsometricCameraModel.GroundHit(new Vector3(0, 10, 0), Vector3.Normalize(new Vector3(1, -1, 0)));

        Assert.NotNull(hit);
        Assert.Equal(10, hit!.Value.X, 3);
        Assert.Equal(0, hit.Value.Y, 3);
        Assert.Null(IsometricCameraModel.GroundHit(Vector3.Zero, Vector3.UnitY));
    }

    [Fact]
    public void Combat_framing_pulls_camera_back_and_restores()
    {
        var camera = new IsometricCameraModel();
        float normal = camera.Distance;

        camera.FrameCombat(true);
        float combat = camera.Distance;
        camera.FrameCombat(false);

        Assert.True(combat > normal);
        Assert.Equal(normal, camera.Distance);
    }
}
