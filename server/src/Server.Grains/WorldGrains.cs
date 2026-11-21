using Realmforge.Rules.Pvp;

namespace Realmforge.Server.Grains;

public sealed class LadderState
{
    public Dictionary<Guid, LadderEntry> Entries { get; set; } = new();
}

public sealed class ArenaLadderGrain : Grain, IArenaLadderGrain
{
    private const int MaxTopCount = 100;

    private readonly IPersistentState<LadderState> _state;

    public ArenaLadderGrain([PersistentState("arena-ladder", GrainStorageNames.Main)] IPersistentState<LadderState> state)
    {
        _state = state;
    }

    public async Task<(int WinnerRating, int LoserRating)> ReportResult(Guid winnerId, Guid loserId)
    {
        LadderEntry winner = Entry(winnerId);
        LadderEntry loser = Entry(loserId);
        (int winnerRating, int loserRating) = ArenaRating.Update(winner.Rating, loser.Rating);
        _state.State.Entries[winnerId] = winner with { Rating = winnerRating, Wins = winner.Wins + 1 };
        _state.State.Entries[loserId] = loser with { Rating = loserRating, Losses = loser.Losses + 1 };
        await _state.WriteStateAsync();
        return (winnerRating, loserRating);
    }

    public Task<LadderEntry> Get(Guid characterId) => Task.FromResult(Entry(characterId));

    public Task<IReadOnlyList<LadderEntry>> Top(int count)
    {
        IReadOnlyList<LadderEntry> top = _state.State.Entries.Values
            .OrderByDescending(entry => entry.Rating)
            .Take(Math.Clamp(count, 1, MaxTopCount))
            .ToList();
        return Task.FromResult(top);
    }

    private LadderEntry Entry(Guid characterId) =>
        _state.State.Entries.GetValueOrDefault(characterId) ?? new LadderEntry(characterId, ArenaRating.StartingRating, 0, 0);
}

/// <summary>
/// Live map of zone and instance servers. Entries expire unless refreshed by heartbeats, so a crashed
/// server stops receiving players within <see cref="HeartbeatTimeout"/>.
/// </summary>
public sealed class ZoneDirectoryGrain : Grain, IZoneDirectoryGrain
{
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, (ZoneServerInfo Server, DateTimeOffset SeenAt)> _servers = new();
    private readonly Dictionary<Guid, ZoneServerInfo> _instances = new();
    private readonly TimeProvider _time;

    public ZoneDirectoryGrain(TimeProvider time)
    {
        _time = time;
    }

    public Task Heartbeat(ZoneServerInfo server)
    {
        _servers[server.ServerId] = (server, _time.GetUtcNow());
        if (server.InstanceRunId.HasValue)
        {
            _instances[server.InstanceRunId.Value] = server;
        }
        return Task.CompletedTask;
    }

    public Task<ZoneServerInfo?> ResolveZone(string zoneId)
    {
        ZoneServerInfo? best = Alive()
            .Where(server => server.InstanceRunId == null && server.ZoneIds.Contains(zoneId))
            .OrderBy(server => server.Players)
            .FirstOrDefault();
        return Task.FromResult(best);
    }

    public Task<ZoneServerInfo?> ResolveInstance(Guid runId) => Task.FromResult(_instances.GetValueOrDefault(runId));

    public Task AssignInstance(Guid runId, ZoneServerInfo server)
    {
        _instances[runId] = server;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ZoneServerInfo>> List() => Task.FromResult<IReadOnlyList<ZoneServerInfo>>(Alive().ToList());

    private IEnumerable<ZoneServerInfo> Alive()
    {
        DateTimeOffset cutoff = _time.GetUtcNow() - HeartbeatTimeout;
        return _servers.Values.Where(entry => entry.SeenAt >= cutoff).Select(entry => entry.Server);
    }
}
