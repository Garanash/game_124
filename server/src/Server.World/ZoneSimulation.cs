using Realmforge.Protocol;
using Realmforge.Rules;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Professions;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Dice;
using Realmforge.Rules.Inventory;
using Realmforge.Rules.Quests;
using Realmforge.Rules.World;
using Realmforge.Server.Core.Social;

namespace Realmforge.Server.World;

public sealed record PlayerJoin(
    CharacterSheet Sheet,
    Backpack Backpack,
    Guid? PartyId,
    Guid? GuildId,
    string FactionId,
    string[] Cosmetics);

/// <summary>
/// Authoritative real-time simulation of one zone or instance. Single-threaded: the host calls
/// <see cref="Handle"/> and <see cref="Tick"/> from one loop and drains outbound messages and effects.
/// </summary>
public sealed partial class ZoneSimulation
{
    public const int TicksPerSecond = 20;
    public const float TickSeconds = 1f / TicksPerSecond;
    public const float PlayerSpeedMetersPerSecond = 6f;
    public const float NpcSpeedMetersPerSecond = 2.5f;
    public const float MobSpeedMetersPerSecond = 4f;
    public const float InterestRadiusMeters = 60f;
    public const float InteractionRangeMeters = 4f;
    public const int SnapshotEveryTicks = 2;
    private const float ArrivalToleranceMeters = 0.05f;
    private const int PositionSaveEveryTicks = TicksPerSecond * 30;

    private readonly ContentCatalog _catalog;
    private readonly IRandomSource _random;
    private readonly Dictionary<long, WorldEntity> _entities = new();
    private readonly Dictionary<long, List<IServerMessage>> _outbox = new();
    private readonly List<ZoneEffect> _effects = new();
    private readonly double _startUnixSeconds;
    private long _nextEntityId = 1;
    private long _tick;

    public ZoneSimulation(ZoneDefinition zone, ContentCatalog catalog, IRandomSource random, double startUnixSeconds)
    {
        Zone = zone;
        _catalog = catalog;
        _random = random;
        _startUnixSeconds = startUnixSeconds;
        SpawnStaticEntities();
    }

    public ZoneDefinition Zone { get; }
    public double Now { get; private set; }
    public long TickCount => _tick;
    public IEnumerable<WorldEntity> Entities => _entities.Values;
    public IEnumerable<PlayerEntity> Players => _entities.Values.OfType<PlayerEntity>();
    public int ConnectedPlayerCount => Players.Count(player => player.IsConnected);

    public WorldEntity? Find(long entityId) => _entities.GetValueOrDefault(entityId);

    public PlayerEntity? FindPlayer(Guid characterId) => Players.FirstOrDefault(player => player.CharacterId == characterId);

    public PlayerEntity AddPlayer(PlayerJoin join)
    {
        PlayerEntity? existing = FindPlayer(join.Sheet.Id);
        if (existing != null)
        {
            existing.IsConnected = true;
            existing.KnownEntities.Clear();
            Send(existing.Id, Welcome(existing));
            return existing;
        }
        bool hasSavedPosition = join.Sheet.ZoneId == Zone.Id;
        float x = hasSavedPosition ? join.Sheet.PositionX : Zone.Spawn.X;
        float z = hasSavedPosition ? join.Sheet.PositionZ : Zone.Spawn.Z;
        var player = new PlayerEntity(_nextEntityId++, join.Sheet, join.Backpack, x, z)
        {
            SpeedMetersPerSecond = PlayerSpeedMetersPerSecond,
            PartyId = join.PartyId,
            GuildId = join.GuildId,
            FactionId = join.FactionId,
            Cosmetics = join.Cosmetics,
        };
        RefreshDerivedStats(player);
        _entities[player.Id] = player;
        Send(player.Id, Welcome(player));
        return player;
    }

    /// <summary>
    /// A disconnecting player leaves the world unless locked in combat, where AI finishes their turns
    /// and the character is removed once the encounter ends.
    /// </summary>
    public void Disconnect(long playerEntityId)
    {
        if (_entities.GetValueOrDefault(playerEntityId) is not PlayerEntity player)
        {
            return;
        }
        player.IsConnected = false;
        _outbox.Remove(playerEntityId);
        if (player.InCombat)
        {
            HandOverToAi(player);
            return;
        }
        Emit(new SavePosition(player.CharacterId, Zone.Id, player.X, player.Z, player.Sheet.CurrentHp));
        RemoveEntity(player);
    }

    public void UpdateMembership(Guid characterId, Guid? partyId, Guid? guildId)
    {
        PlayerEntity? player = FindPlayer(characterId);
        if (player != null)
        {
            player.PartyId = partyId;
            player.GuildId = guildId;
        }
    }

    public void ReplaceSheet(Guid characterId, CharacterSheet sheet, Backpack backpack)
    {
        PlayerEntity? player = FindPlayer(characterId);
        if (player != null && !player.InCombat)
        {
            player.Sheet = sheet;
            player.Backpack = backpack;
            RefreshDerivedStats(player);
        }
    }

    public void Handle(long playerEntityId, IClientMessage message)
    {
        if (_entities.GetValueOrDefault(playerEntityId) is not PlayerEntity player || !player.IsConnected)
        {
            return;
        }
        switch (message)
        {
            case MoveToRequest move:
                HandleMove(player, move);
                break;
            case CombatCommandRequest command:
                HandleCombatCommand(player, command);
                break;
            case JoinEncounterRequest join:
                HandleJoinEncounter(player, join.EncounterId);
                break;
            case InteractRequest interact:
                HandleInteract(player, interact);
                break;
            case ChatSendRequest chat:
                HandleChat(player, chat);
                break;
            case QuestChoiceRequest choice:
                Emit(new QuestChoiceMade(player.CharacterId, choice.QuestId, choice.ChoiceId));
                break;
            case UsePortalRequest portal:
                HandlePortal(player, portal.PortalIndex);
                break;
            case PingRequest ping:
                Send(player.Id, new PongMessage { ClientTime = ping.ClientTime, ServerTime = _startUnixSeconds + Now });
                break;
        }
    }

    public void Tick()
    {
        _tick++;
        Now += TickSeconds;
        UpdateNpcSchedules();
        MoveEntities();
        CompleteGathering();
        RespawnMobs();
        CheckAggro();
        TickEncounters();
        if (_tick % SnapshotEveryTicks == 0)
        {
            SendSnapshots();
        }
        if (_tick % PositionSaveEveryTicks == 0)
        {
            foreach (PlayerEntity player in Players.Where(player => player.IsConnected && !player.InCombat))
            {
                Emit(new SavePosition(player.CharacterId, Zone.Id, player.X, player.Z, player.Sheet.CurrentHp));
            }
        }
    }

    public IReadOnlyList<(long PlayerEntityId, IServerMessage Message)> DrainOutbox()
    {
        var drained = new List<(long, IServerMessage)>();
        foreach (KeyValuePair<long, List<IServerMessage>> pair in _outbox)
        {
            drained.AddRange(pair.Value.Select(message => (pair.Key, message)));
            pair.Value.Clear();
        }
        return drained;
    }

    public IReadOnlyList<ZoneEffect> DrainEffects()
    {
        var drained = _effects.ToList();
        _effects.Clear();
        return drained;
    }

    public void SendTo(Guid characterId, IServerMessage message)
    {
        PlayerEntity? player = FindPlayer(characterId);
        if (player != null)
        {
            Send(player.Id, message);
        }
    }

    /// <summary>Delivers a party/guild/world message that arrived from another server via the message bus.</summary>
    public void DeliverChat(ChatScope scope, Guid? groupId, ChatMessage message)
    {
        IEnumerable<PlayerEntity> recipients = Players.Where(player => player.IsConnected);
        recipients = scope switch
        {
            ChatScope.Party => recipients.Where(player => player.PartyId == groupId),
            ChatScope.Guild => recipients.Where(player => player.GuildId == groupId),
            _ => recipients,
        };
        foreach (PlayerEntity player in recipients)
        {
            Send(player.Id, message);
        }
    }

    private void HandleMove(PlayerEntity player, MoveToRequest move)
    {
        if (player.InCombat || player.IsDead || float.IsNaN(move.X) || float.IsNaN(move.Z))
        {
            return;
        }
        player.GatheringNodeEntityId = null;
        player.TargetX = Math.Clamp(move.X, 0, Zone.SizeMeters);
        player.TargetZ = Math.Clamp(move.Z, 0, Zone.SizeMeters);
    }

    private void HandleInteract(PlayerEntity player, InteractRequest interact)
    {
        WorldEntity? target = Find(interact.TargetEntityId);
        if (target == null || player.InCombat || player.IsDead)
        {
            return;
        }
        if (target is PlayerEntity other)
        {
            TryStartPvp(player, other);
            return;
        }
        if (target is MobEntity mob)
        {
            EngageMob(player, mob);
            return;
        }
        if (player.DistanceTo(target) > InteractionRangeMeters)
        {
            SendError(player, "interact.too_far", "Подойдите ближе.");
            return;
        }
        switch (target)
        {
            case NpcEntity npc:
                Emit(new NpcInteracted(player.CharacterId, npc.Id, npc.Definition.Id, interact.OptionId));
                Emit(new ReportQuestEvent(player.CharacterId, new QuestEvent(QuestStepType.Talk, npc.Definition.Id)));
                break;
            case NodeEntity node when node.IsAvailable(Now):
                RuleResult skill = ProfessionRules.CanGather(player.Sheet, node.Definition);
                if (!skill.Success)
                {
                    SendError(player, skill.Code, skill.Message);
                    return;
                }
                player.TargetX = player.X;
                player.TargetZ = player.Z;
                player.GatheringNodeEntityId = node.Id;
                player.GatheringCompletesAt = Now + node.Definition.GatherSeconds;
                break;
            case NodeEntity:
                SendError(player, "gather.depleted", "Ресурс ещё не восстановился.");
                break;
        }
    }

    private void HandlePortal(PlayerEntity player, int portalIndex)
    {
        if (portalIndex < 0 || portalIndex >= Zone.Portals.Count || player.InCombat)
        {
            return;
        }
        PortalDefinition portal = Zone.Portals[portalIndex];
        if (player.DistanceTo(portal.Position.X, portal.Position.Z) > InteractionRangeMeters)
        {
            SendError(player, "portal.too_far", "Подойдите к порталу.");
            return;
        }
        Emit(new TransferToZone(player.CharacterId, portal.ToZoneId, portal.Arrival.X, portal.Arrival.Z));
        Emit(new ReportQuestEvent(player.CharacterId, new QuestEvent(QuestStepType.Reach, portal.ToZoneId)));
    }

    private void HandleChat(PlayerEntity player, ChatSendRequest chat)
    {
        string? text = ChatText.Sanitize(chat.Text);
        if (text == null || !ChatChannel.TryParse(chat.Channel, out ChatChannel channel))
        {
            return;
        }
        var message = new ChatMessage { Channel = channel.Scope.ToString().ToLowerInvariant(), SenderName = player.Name, Text = text, ServerTime = _startUnixSeconds + Now };
        string? subject = channel.Subject(Zone.Id, player.PartyId, player.GuildId);
        if (subject != null)
        {
            Emit(new PublishChat(player.CharacterId, subject, message.Channel, player.Name, text));
            return;
        }
        IEnumerable<PlayerEntity> listeners = Players.Where(other => other.IsConnected);
        if (channel.Scope == ChatScope.Say)
        {
            listeners = listeners.Where(other => other.DistanceTo(player) <= ChatChannel.SayRadiusMeters);
        }
        foreach (PlayerEntity listener in listeners)
        {
            Send(listener.Id, message);
        }
    }

    private void UpdateNpcSchedules()
    {
        if (_tick % TicksPerSecond != 1)
        {
            return;
        }
        double hour = GameClock.HourOfDay(_startUnixSeconds + Now);
        foreach (NpcEntity npc in _entities.Values.OfType<NpcEntity>())
        {
            ScheduleEntry? entry = NpcSchedule.ActiveEntry(npc.Definition, hour);
            if (entry == null)
            {
                continue;
            }
            npc.Activity = entry.Activity;
            npc.TargetX = entry.Position.X;
            npc.TargetZ = entry.Position.Z;
        }
    }

    private void MoveEntities()
    {
        float step = TickSeconds;
        foreach (WorldEntity entity in _entities.Values)
        {
            if (entity.InCombat || entity.IsDead || entity.SpeedMetersPerSecond <= 0)
            {
                continue;
            }
            float deltaX = entity.TargetX - entity.X;
            float deltaZ = entity.TargetZ - entity.Z;
            float distance = MathF.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            if (distance <= ArrivalToleranceMeters)
            {
                continue;
            }
            float travel = MathF.Min(distance, entity.SpeedMetersPerSecond * step);
            entity.X += deltaX / distance * travel;
            entity.Z += deltaZ / distance * travel;
            entity.Heading = MathF.Atan2(deltaX, deltaZ);
        }
    }

    private void CompleteGathering()
    {
        foreach (PlayerEntity player in Players.Where(player => player.GatheringNodeEntityId.HasValue && Now >= player.GatheringCompletesAt).ToList())
        {
            long nodeId = player.GatheringNodeEntityId!.Value;
            player.GatheringNodeEntityId = null;
            if (Find(nodeId) is not NodeEntity node || !node.IsAvailable(Now))
            {
                continue;
            }
            node.AvailableAt = Now + node.Definition.RespawnSeconds;
            Emit(new NodeGathered(player.CharacterId, node.Definition.Id));
        }
    }

    private void RespawnMobs()
    {
        foreach (MobEntity mob in _entities.Values.OfType<MobEntity>().Where(mob => mob.IsDead && mob.RespawnSeconds > 0 && Now >= mob.RespawnAt))
        {
            mob.IsDead = false;
            mob.X = mob.SpawnX;
            mob.Z = mob.SpawnZ;
            mob.TargetX = mob.SpawnX;
            mob.TargetZ = mob.SpawnZ;
        }
    }

    private void SpawnStaticEntities()
    {
        const float SpawnScatterMeters = 6f;
        foreach (MobSpawnDefinition spawn in Zone.Mobs)
        {
            MobDefinition definition = _catalog.Mobs[spawn.MobId];
            for (int index = 0; index < spawn.Count; index++)
            {
                float offsetX = (_random.NextInt(0, 1000) / 1000f - 0.5f) * 2 * SpawnScatterMeters;
                float offsetZ = (_random.NextInt(0, 1000) / 1000f - 0.5f) * 2 * SpawnScatterMeters;
                var mob = new MobEntity(_nextEntityId++, definition, spawn.Position.X + offsetX, spawn.Position.Z + offsetZ, spawn.RespawnSeconds)
                {
                    SpeedMetersPerSecond = MobSpeedMetersPerSecond,
                };
                _entities[mob.Id] = mob;
            }
        }
        foreach (NodeSpawnDefinition spawn in Zone.Nodes)
        {
            var node = new NodeEntity(_nextEntityId++, _catalog.Nodes[spawn.NodeId], spawn.Position.X, spawn.Position.Z);
            _entities[node.Id] = node;
        }
        foreach (NpcDefinition definition in _catalog.Npcs.Values.Where(npc => npc.ZoneId == Zone.Id))
        {
            WorldPoint start = definition.Schedule.FirstOrDefault()?.Position ?? Zone.Spawn;
            var npc = new NpcEntity(_nextEntityId++, definition, start.X, start.Z) { SpeedMetersPerSecond = NpcSpeedMetersPerSecond };
            _entities[npc.Id] = npc;
        }
        foreach (PortalDefinition definition in Zone.Portals)
        {
            string name = _catalog.Zones.TryGetValue(definition.ToZoneId, out ZoneDefinition? target) ? target.Name : definition.ToZoneId;
            var portal = new PortalEntity(_nextEntityId++, definition, name);
            _entities[portal.Id] = portal;
        }
    }

    private WelcomeMessage Welcome(PlayerEntity player)
    {
        return new WelcomeMessage
        {
            EntityId = player.Id,
            ZoneId = Zone.Id,
            SceneAddress = Zone.SceneAddress,
            ContentVersion = _catalog.Version,
            ServerTime = _startUnixSeconds + Now,
        };
    }

    private void RemoveEntity(WorldEntity entity)
    {
        _entities.Remove(entity.Id);
        foreach (PlayerEntity player in Players.Where(player => player.KnownEntities.Remove(entity.Id)))
        {
            Send(player.Id, new EntityDespawnMessage { EntityId = entity.Id });
        }
    }

    private void Send(long playerEntityId, IServerMessage message)
    {
        if (_entities.GetValueOrDefault(playerEntityId) is not PlayerEntity { IsConnected: true })
        {
            return;
        }
        if (!_outbox.TryGetValue(playerEntityId, out List<IServerMessage>? queue))
        {
            queue = new List<IServerMessage>();
            _outbox[playerEntityId] = queue;
        }
        queue.Add(message);
    }

    private void SendError(PlayerEntity player, string code, string text)
    {
        Send(player.Id, new ErrorMessage { Code = code, Text = text });
    }

    private void Emit(ZoneEffect effect)
    {
        _effects.Add(effect);
    }
}
