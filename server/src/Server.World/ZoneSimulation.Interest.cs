using Realmforge.Protocol;
using Realmforge.Rules.Characters;

namespace Realmforge.Server.World;

public sealed partial class ZoneSimulation
{
    private void SendSnapshots()
    {
        foreach (PlayerEntity player in Players.Where(player => player.IsConnected).ToList())
        {
            List<WorldEntity> visible = _entities.Values.Where(entity => IsVisibleTo(player, entity)).ToList();
            var visibleIds = new HashSet<long>(visible.Select(entity => entity.Id));
            foreach (long goneId in player.KnownEntities.Where(id => !visibleIds.Contains(id)).ToList())
            {
                player.KnownEntities.Remove(goneId);
                Send(player.Id, new EntityDespawnMessage { EntityId = goneId });
            }
            foreach (WorldEntity entity in visible.Where(entity => player.KnownEntities.Add(entity.Id)))
            {
                Send(player.Id, SpawnMessage(entity));
            }
            Send(player.Id, new WorldSnapshotMessage
            {
                Tick = _tick,
                ServerTime = _startUnixSeconds + Now,
                Entities = visible.Select(entity => new EntityState
                {
                    EntityId = entity.Id,
                    X = entity.X,
                    Z = entity.Z,
                    Heading = entity.Heading,
                    Flags = entity.Flags,
                }).ToArray(),
            });
        }
    }

    private static bool IsVisibleTo(PlayerEntity viewer, WorldEntity entity)
    {
        if (entity is MobEntity { IsDead: true })
        {
            return false;
        }
        return entity == viewer || entity is PortalEntity || viewer.DistanceTo(entity) <= InterestRadiusMeters;
    }

    private static EntitySpawnMessage SpawnMessage(WorldEntity entity)
    {
        var message = new EntitySpawnMessage
        {
            EntityId = entity.Id,
            Kind = entity.Kind,
            Name = entity.Name,
            DefinitionId = entity.DefinitionId,
            X = entity.X,
            Z = entity.Z,
        };
        switch (entity)
        {
            case PlayerEntity player:
                message.Cosmetics = player.Cosmetics;
                message.Look = LookOf(player.Sheet.Look);
                message.Held = player.Held;
                message.Hp = player.Sheet.CurrentHp;
                message.MaxHp = player.MaxHp;
                message.Level = player.Sheet.Level;
                break;
            case MobEntity mob:
                message.Hp = mob.Definition.Hp;
                message.MaxHp = mob.Definition.Hp;
                message.Level = mob.Definition.Level;
                break;
        }
        return message;
    }

    private static LookDto? LookOf(CharacterLook? look)
    {
        return look == null ? null : new LookDto
        {
            Sex = look.Sex,
            Face = look.Face,
            Hair = look.Hair,
            HairColor = look.HairColor,
            EyeColor = look.EyeColor,
            Build = look.Build,
            HeightPercent = look.HeightPercent,
            Outfit = look.Outfit,
        };
    }
}
