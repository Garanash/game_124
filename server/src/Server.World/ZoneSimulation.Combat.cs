using Realmforge.Protocol;
using Realmforge.Rules;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Combat;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Pvp;
using Realmforge.Rules.Quests;

namespace Realmforge.Server.World;

public enum EncounterKind
{
    Pve,
    Pvp,
}

public sealed class WorldEncounter
{
    public WorldEncounter(Encounter encounter, EncounterKind kind, float centerX, float centerZ)
    {
        Encounter = encounter;
        Kind = kind;
        CenterX = centerX;
        CenterZ = centerZ;
    }

    public Encounter Encounter { get; }
    public EncounterKind Kind { get; }
    public float CenterX { get; }
    public float CenterZ { get; }
    public Dictionary<long, WorldEntity> Participants { get; } = new();
    /// <summary>The player who started the fight on each team; credited for PvP kills.</summary>
    public Dictionary<int, PlayerEntity> Initiators { get; } = new();

    public IEnumerable<PlayerEntity> Players => Participants.Values.OfType<PlayerEntity>();
}

public sealed partial class ZoneSimulation
{
    public const int PlayerTeam = 1;
    public const int MobTeam = 2;
    public const int PvpAttackerTeam = 1;
    public const int PvpDefenderTeam = 2;
    public const int ArenaRadiusCells = 10;
    public const float EngageRangeMeters = 20f;
    public const float EncounterJoinRadiusMeters = 25f;
    public const float MobGroupRadiusMeters = 8f;
    private const float RegenFractionPerSecond = 0.02f;
    private const long SummonIdRangeStart = 1_000_000_000;
    private const long SummonIdsPerEncounter = 1_000;

    private readonly Dictionary<Guid, WorldEncounter> _encounters = new();
    private long _encounterSequence;

    public IReadOnlyDictionary<Guid, WorldEncounter> Encounters => _encounters;

    /// <summary>The strongest mob of an instance zone; killing it completes the run.</summary>
    public string? BossMobId => Zone.Kind != ZoneKind.Instance
        ? null
        : Zone.Mobs.Select(spawn => _catalog.Mobs[spawn.MobId]).OrderByDescending(mob => mob.Level).ThenByDescending(mob => mob.Hp).FirstOrDefault()?.Id;

    private void CheckAggro()
    {
        foreach (MobEntity mob in _entities.Values.OfType<MobEntity>().Where(IsAggressiveAndFree).ToList())
        {
            if (mob.InCombat)
            {
                continue;
            }
            PlayerEntity? victim = Players
                .Where(player => IsFreeToFight(player) && player.IsConnected && player.DistanceTo(mob) <= mob.Definition.AggroRadius)
                .OrderBy(player => player.DistanceTo(mob))
                .FirstOrDefault();
            if (victim != null)
            {
                StartPve(victim, mob);
            }
        }
    }

    private static bool IsAggressiveAndFree(MobEntity mob)
    {
        return !mob.IsDead && !mob.InCombat && !mob.Definition.IsGame && mob.Definition.AggroRadius > 0;
    }

    private static bool IsFreeToFight(WorldEntity entity) => !entity.IsDead && !entity.InCombat;

    private void EngageMob(PlayerEntity player, MobEntity mob)
    {
        if (!IsFreeToFight(mob) || player.DistanceTo(mob) > EngageRangeMeters)
        {
            SendError(player, "combat.cannot_engage", "Цель недоступна для атаки.");
            return;
        }
        StartPve(player, mob);
    }

    private void StartPve(PlayerEntity initiator, MobEntity mob)
    {
        WorldEncounter encounter = CreateEncounter(EncounterKind.Pve, (initiator.X + mob.X) / 2, (initiator.Z + mob.Z) / 2);
        encounter.Initiators[PlayerTeam] = initiator;
        foreach (PlayerEntity player in AlliesNear(initiator))
        {
            AddPlayerCombatant(encounter, player, PlayerTeam);
        }
        IEnumerable<MobEntity> pack = _entities.Values.OfType<MobEntity>()
            .Where(other => IsFreeToFight(other) && other.DistanceTo(mob) <= MobGroupRadiusMeters && other.Definition.IsGame == mob.Definition.IsGame);
        foreach (MobEntity member in pack)
        {
            AddCombatant(encounter, member, CombatantFactory.FromMob(member.Id, member.Definition, MobTeam));
        }
        BeginEncounter(encounter);
    }

    private void TryStartPvp(PlayerEntity attacker, PlayerEntity defender)
    {
        bool allowed = Zone.Pvp != PvpRule.None
            && IsFreeToFight(defender)
            && attacker.DistanceTo(defender) <= EngageRangeMeters
            && PvpRules.CanAttack(Zone.Pvp, Participant(attacker), Participant(defender), _catalog);
        if (!allowed)
        {
            SendError(attacker, "pvp.not_allowed", "Здесь нельзя атаковать этого игрока.");
            return;
        }
        WorldEncounter encounter = CreateEncounter(EncounterKind.Pvp, (attacker.X + defender.X) / 2, (attacker.Z + defender.Z) / 2);
        encounter.Initiators[PvpAttackerTeam] = attacker;
        encounter.Initiators[PvpDefenderTeam] = defender;
        foreach (PlayerEntity player in AlliesNear(attacker))
        {
            AddPlayerCombatant(encounter, player, PvpAttackerTeam);
        }
        foreach (PlayerEntity player in AlliesNear(defender).Where(player => !player.InCombat))
        {
            AddPlayerCombatant(encounter, player, PvpDefenderTeam);
        }
        BeginEncounter(encounter);
    }

    private IEnumerable<PlayerEntity> AlliesNear(PlayerEntity leader)
    {
        yield return leader;
        if (!leader.PartyId.HasValue)
        {
            yield break;
        }
        foreach (PlayerEntity member in Players.Where(player => player != leader
                     && player.PartyId == leader.PartyId
                     && player.IsConnected
                     && IsFreeToFight(player)
                     && player.DistanceTo(leader) <= EncounterJoinRadiusMeters).ToList())
        {
            yield return member;
        }
    }

    private void HandleJoinEncounter(PlayerEntity player, Guid encounterId)
    {
        if (!IsFreeToFight(player) || !_encounters.TryGetValue(encounterId, out WorldEncounter? encounter))
        {
            return;
        }
        bool partyInside = player.PartyId.HasValue && encounter.Players.Any(member => member.PartyId == player.PartyId);
        bool closeEnough = player.DistanceTo(encounter.CenterX, encounter.CenterZ) <= EncounterJoinRadiusMeters;
        if (encounter.Kind != EncounterKind.Pve || !partyInside || !closeEnough || encounter.Encounter.IsFinished)
        {
            SendError(player, "combat.cannot_join", "К этому бою нельзя присоединиться.");
            return;
        }
        AddPlayerCombatant(encounter, player, PlayerTeam);
        Send(player.Id, StartedMessage(encounter));
    }

    private void HandleCombatCommand(PlayerEntity player, CombatCommandRequest request)
    {
        if (!player.EncounterId.HasValue || !_encounters.TryGetValue(player.EncounterId.Value, out WorldEncounter? encounter))
        {
            return;
        }
        var command = new CombatCommand
        {
            Type = (CombatCommandType)request.CommandType,
            Cell = new GridPosition(request.CellX, request.CellY),
            TargetId = request.TargetId,
            AbilityId = request.AbilityId,
        };
        RuleResult result = encounter.Encounter.Execute(player.Id, command);
        if (!result.Success)
        {
            SendError(player, result.Code, result.Message);
        }
    }

    private void HandOverToAi(PlayerEntity player)
    {
        if (player.EncounterId.HasValue && _encounters.TryGetValue(player.EncounterId.Value, out WorldEncounter? encounter))
        {
            Combatant? combatant = encounter.Encounter.Find(player.Id);
            if (combatant != null)
            {
                combatant.IsPlayerControlled = false;
            }
        }
    }

    private WorldEncounter CreateEncounter(EncounterKind kind, float centerX, float centerZ)
    {
        long sequence = ++_encounterSequence;
        CombatGrid grid = CombatGrid.AroundWorldPoint(centerX, centerZ, ArenaRadiusCells);
        var encounter = new Encounter(Guid.NewGuid(), grid, _random, _catalog, SummonIdRangeStart + sequence * SummonIdsPerEncounter);
        var worldEncounter = new WorldEncounter(encounter, kind, centerX, centerZ);
        _encounters[encounter.Id] = worldEncounter;
        return worldEncounter;
    }

    private void AddPlayerCombatant(WorldEncounter encounter, PlayerEntity player, int team)
    {
        player.GatheringNodeEntityId = null;
        AddCombatant(encounter, player, CombatantFactory.FromCharacter(player.Id, player.Sheet, player.Backpack, _catalog, team));
    }

    private void AddCombatant(WorldEncounter encounter, WorldEntity entity, Combatant combatant)
    {
        CombatGrid grid = encounter.Encounter.Grid;
        GridPosition cell = grid.WorldToCell(entity.X, entity.Z);
        var clamped = new GridPosition(Math.Clamp(cell.X, 0, grid.Width - 1), Math.Clamp(cell.Y, 0, grid.Height - 1));
        encounter.Encounter.Join(combatant, clamped);
        encounter.Participants[entity.Id] = entity;
        entity.EncounterId = encounter.Encounter.Id;
        entity.TargetX = entity.X;
        entity.TargetZ = entity.Z;
    }

    private void BeginEncounter(WorldEncounter encounter)
    {
        encounter.Encounter.Start();
        EncounterStartedMessage started = StartedMessage(encounter);
        foreach (PlayerEntity player in encounter.Players)
        {
            Send(player.Id, started);
        }
    }

    private void TickEncounters()
    {
        foreach (WorldEncounter encounter in _encounters.Values.ToList())
        {
            encounter.Encounter.Tick(TickSeconds);
            BroadcastCombatEvents(encounter);
            SyncCombatPositions(encounter);
            if (encounter.Encounter.IsFinished)
            {
                FinishEncounter(encounter);
            }
        }
        RegenerateOutOfCombat();
    }

    private void BroadcastCombatEvents(WorldEncounter encounter)
    {
        List<CombatEvent> events = encounter.Encounter.DrainEvents();
        if (events.Count == 0)
        {
            return;
        }
        var message = new CombatEventsMessage
        {
            EncounterId = encounter.Encounter.Id,
            CurrentTurnEntityId = encounter.Encounter.Current?.EntityId ?? 0,
            TurnTimeRemaining = encounter.Encounter.TurnTimeRemaining,
            Round = encounter.Encounter.Round,
            Events = events.Select(ToDto).ToArray(),
        };
        IEnumerable<PlayerEntity> audience = Players.Where(player =>
            player.EncounterId == encounter.Encounter.Id
            || player.DistanceTo(encounter.CenterX, encounter.CenterZ) <= InterestRadiusMeters);
        foreach (PlayerEntity player in audience)
        {
            Send(player.Id, message);
        }
    }

    private static void SyncCombatPositions(WorldEncounter encounter)
    {
        foreach (Combatant combatant in encounter.Encounter.Combatants)
        {
            if (!encounter.Participants.TryGetValue(combatant.EntityId, out WorldEntity? entity))
            {
                continue;
            }
            encounter.Encounter.Grid.CellToWorld(combatant.Position, out float x, out float z);
            entity.X = x;
            entity.Z = z;
            entity.TargetX = x;
            entity.TargetZ = z;
        }
    }

    private void FinishEncounter(WorldEncounter encounter)
    {
        _encounters.Remove(encounter.Encounter.Id);
        int? winningTeam = encounter.Encounter.WinningTeam;
        if (encounter.Kind == EncounterKind.Pvp && winningTeam.HasValue)
        {
            CreditPvpKills(encounter, winningTeam.Value);
            if (Zone.Pvp == PvpRule.Arena)
            {
                ReportArenaResults(encounter, winningTeam.Value);
            }
        }
        foreach (Combatant combatant in encounter.Encounter.Combatants)
        {
            if (!encounter.Participants.TryGetValue(combatant.EntityId, out WorldEntity? entity))
            {
                continue;
            }
            entity.EncounterId = null;
            switch (entity)
            {
                case PlayerEntity player:
                    SettlePlayer(encounter, player, combatant, combatant.Team == winningTeam);
                    break;
                case MobEntity mob:
                    SettleMob(mob, combatant);
                    break;
            }
        }
    }

    private void SettlePlayer(WorldEncounter encounter, PlayerEntity player, Combatant combatant, bool victory)
    {
        long xp = 0;
        var loot = new LootResult();
        if (encounter.Kind == EncounterKind.Pve && victory)
        {
            (xp, loot) = RewardsFor(encounter, combatant.Team);
            List<string> killedMobs = encounter.Encounter.DefeatedEnemiesOf(combatant.Team)
                .Where(enemy => enemy.MobId.Length > 0).Select(enemy => enemy.MobId).ToList();
            Emit(new GrantEncounterRewards(player.CharacterId, xp, loot, killedMobs));
            foreach (string mobId in killedMobs)
            {
                Emit(new ReportQuestEvent(player.CharacterId, new QuestEvent(QuestStepType.Kill, mobId)));
                if (mobId == BossMobId)
                {
                    Emit(new InstanceBossDefeated(player.CharacterId, mobId));
                }
            }
        }
        Send(player.Id, new EncounterEndedMessage
        {
            EncounterId = encounter.Encounter.Id,
            Victory = victory,
            Xp = xp,
            Gold = loot.Gold,
            Items = loot.Items.Select(item => new ItemStackDto { ItemId = item.ItemId, Quantity = item.Quantity }).ToArray(),
        });
        player.Sheet.CurrentHp = combatant.Hp;
        if (!combatant.IsAlive)
        {
            Respawn(player);
        }
        if (!player.IsConnected)
        {
            Emit(new SavePosition(player.CharacterId, Zone.Id, player.X, player.Z, player.Sheet.CurrentHp));
            RemoveEntity(player);
        }
    }

    /// <summary>XP is split evenly across the winning team; loot is personal so nobody fights over drops.</summary>
    private (long Xp, LootResult Loot) RewardsFor(WorldEncounter encounter, int team)
    {
        IReadOnlyList<Combatant> defeated = encounter.Encounter.DefeatedEnemiesOf(team);
        int winners = Math.Max(1, encounter.Encounter.Combatants.Count(combatant => combatant.Team == team && combatant.CharacterId.HasValue));
        long xp = defeated.Sum(enemy => (long)enemy.XpValue) / winners;
        var loot = new LootResult();
        foreach (Combatant enemy in defeated.Where(enemy => _catalog.LootTables.ContainsKey(enemy.LootTableId)))
        {
            loot.Merge(LootRoller.Roll(_catalog.LootTables[enemy.LootTableId], _random));
        }
        return (xp, loot);
    }

    private void CreditPvpKills(WorldEncounter encounter, int winningTeam)
    {
        if (!encounter.Initiators.TryGetValue(winningTeam, out PlayerEntity? killer))
        {
            return;
        }
        foreach (Combatant victim in encounter.Encounter.DefeatedEnemiesOf(winningTeam).Where(victim => victim.CharacterId.HasValue))
        {
            if (encounter.Participants.GetValueOrDefault(victim.EntityId) is not PlayerEntity victimPlayer)
            {
                continue;
            }
            PvpKillConsequence consequence = PvpRules.OnKill(Zone.Pvp, Participant(killer), Participant(victimPlayer));
            Emit(new PvpKill(killer.CharacterId, victimPlayer.CharacterId, consequence.KillerGoodShift, consequence.KillerLawShift, consequence.VictimGoldLossPercent));
        }
    }

    /// <summary>
    /// Team bouts are rated as paired duels (first winner against first loser, and so on), so a 3v3 moves
    /// each rating as much as a 1v1 instead of multiplying the Elo swing by the team size.
    /// </summary>
    private void ReportArenaResults(WorldEncounter encounter, int winningTeam)
    {
        List<Guid> winners = TeamCharacters(encounter, team => team == winningTeam);
        List<Guid> losers = TeamCharacters(encounter, team => team != winningTeam);
        if (winners.Count == 0 || losers.Count == 0)
        {
            return;
        }
        int bouts = Math.Max(winners.Count, losers.Count);
        for (int bout = 0; bout < bouts; bout++)
        {
            Emit(new ArenaMatchFinished(winners[bout % winners.Count], losers[bout % losers.Count]));
        }
    }

    private static List<Guid> TeamCharacters(WorldEncounter encounter, Func<int, bool> teamFilter) =>
        encounter.Encounter.Combatants
            .Where(combatant => combatant.CharacterId.HasValue && teamFilter(combatant.Team))
            .OrderBy(combatant => combatant.EntityId)
            .Select(combatant => combatant.CharacterId!.Value)
            .ToList();

    private void SettleMob(MobEntity mob, Combatant combatant)
    {
        if (combatant.IsAlive)
        {
            mob.TargetX = mob.SpawnX;
            mob.TargetZ = mob.SpawnZ;
            return;
        }
        mob.IsDead = true;
        mob.RespawnAt = Now + mob.RespawnSeconds;
    }

    private void Respawn(PlayerEntity player)
    {
        Emit(new CharacterDied(player.CharacterId));
        player.X = Zone.Spawn.X;
        player.Z = Zone.Spawn.Z;
        player.TargetX = player.X;
        player.TargetZ = player.Z;
        player.Sheet.CurrentHp = Math.Max(1, player.MaxHp / 2);
    }

    private void RegenerateOutOfCombat()
    {
        if (_tick % TicksPerSecond != 0)
        {
            return;
        }
        foreach (PlayerEntity player in Players.Where(player => !player.InCombat && player.Sheet.CurrentHp < player.MaxHp))
        {
            int regen = Math.Max(1, (int)(player.MaxHp * RegenFractionPerSecond));
            player.Sheet.CurrentHp = Math.Min(player.MaxHp, player.Sheet.CurrentHp + regen);
        }
    }

    private void RefreshDerivedStats(PlayerEntity player)
    {
        var bonuses = CombatantFactory.CharacterBonuses(player.Sheet, player.Backpack, _catalog);
        player.MaxHp = CharacterRules.MaxHp(player.Sheet, _catalog, bonuses);
        if (player.Sheet.CurrentHp <= 0 || player.Sheet.CurrentHp > player.MaxHp)
        {
            player.Sheet.CurrentHp = player.MaxHp;
        }
    }

    private PvpParticipant Participant(PlayerEntity player)
    {
        return new PvpParticipant
        {
            CharacterId = player.CharacterId,
            FactionId = player.FactionId,
            PartyId = player.PartyId,
            GuildId = player.GuildId,
            Alignment = player.Sheet.GetAlignment(),
            Level = player.Sheet.Level,
        };
    }

    private static EncounterStartedMessage StartedMessage(WorldEncounter encounter)
    {
        Encounter fight = encounter.Encounter;
        return new EncounterStartedMessage
        {
            EncounterId = fight.Id,
            OriginX = fight.Grid.OriginX,
            OriginZ = fight.Grid.OriginZ,
            Width = fight.Grid.Width,
            Height = fight.Grid.Height,
            Combatants = fight.Combatants.Select(combatant => new CombatantDto
            {
                EntityId = combatant.EntityId,
                Team = combatant.Team,
                Hp = combatant.Hp,
                MaxHp = combatant.MaxHp,
                CellX = combatant.Position.X,
                CellY = combatant.Position.Y,
                Initiative = combatant.Initiative,
            }).ToArray(),
            TurnOrder = fight.TurnOrder.Select(combatant => combatant.EntityId).ToArray(),
        };
    }

    private static CombatEventDto ToDto(CombatEvent combatEvent)
    {
        return new CombatEventDto
        {
            Type = (byte)combatEvent.Type,
            ActorId = combatEvent.ActorId,
            TargetId = combatEvent.TargetId,
            Value = combatEvent.Value,
            Roll = combatEvent.Roll,
            Success = combatEvent.Success,
            CellX = combatEvent.Cell.X,
            CellY = combatEvent.Cell.Y,
            AbilityId = combatEvent.AbilityId,
            Condition = (byte)combatEvent.Condition,
            DamageType = (byte)combatEvent.DamageType,
        };
    }
}
