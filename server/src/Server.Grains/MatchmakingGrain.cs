using Microsoft.Extensions.Logging;
using Realmforge.Rules.Definitions;
using Realmforge.Server.Core;
using Realmforge.Server.Core.Characters;
using Realmforge.Server.Core.Matchmaking;
using Realmforge.Server.Grains.Instances;

namespace Realmforge.Server.Grains;

/// <summary>
/// One queue per instance and party size (key "inst_goblin_tunnels:3"). The queue is transient by design:
/// after a silo restart players simply queue again.
/// </summary>
public sealed class MatchmakingGrain : Grain, IMatchmakingGrain
{
    private static readonly TimeSpan FormInterval = TimeSpan.FromSeconds(5);

    private readonly ContentCatalog _catalog;
    private readonly IInstanceAllocator _allocator;
    private readonly TimeProvider _time;
    private readonly ILogger<MatchmakingGrain> _logger;
    private Matchmaker _matchmaker = null!;
    private InstanceDefinition _instance = null!;

    public MatchmakingGrain(ContentCatalog catalog, IInstanceAllocator allocator, TimeProvider time, ILogger<MatchmakingGrain> logger)
    {
        _catalog = catalog;
        _allocator = allocator;
        _time = time;
        _logger = logger;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        string[] parts = this.GetPrimaryKeyString().Split(':');
        if (parts.Length != 2 || !_catalog.Instances.TryGetValue(parts[0], out InstanceDefinition? instance)
            || !int.TryParse(parts[1], out int partySize) || !instance.PartySizes.Contains(partySize))
        {
            throw new ArgumentException("Unknown matchmaking queue " + this.GetPrimaryKeyString());
        }
        _instance = instance;
        _matchmaker = new Matchmaker(partySize);
        this.RegisterGrainTimer(FormRuns, new GrainTimerCreationOptions(FormInterval, FormInterval));
        return Task.CompletedTask;
    }

    public Task<DomainResult> Enqueue(QueueTicket ticket)
    {
        if (ticket.AverageLevel < _instance.MinLevel)
        {
            return Task.FromResult(DomainResult.Fail("queue.level", $"Требуется уровень {_instance.MinLevel}."));
        }
        return Task.FromResult(_matchmaker.Enqueue(ticket));
    }

    public Task<bool> Cancel(Guid characterId) => Task.FromResult(_matchmaker.Cancel(characterId));

    public Task<int> QueuedPlayers() => Task.FromResult(_matchmaker.Queue.Sum(ticket => ticket.CharacterIds.Count));

    public async Task<int> FormRunsNow()
    {
        return await FormRunsInternal();
    }

    private Task FormRuns(CancellationToken cancellation) => FormRunsInternal();

    private async Task<int> FormRunsInternal()
    {
        int started = 0;
        foreach (MatchedGroup run in _matchmaker.FormRuns(_time.GetUtcNow()))
        {
            List<Guid> characters = run.Tickets.SelectMany(ticket => ticket.CharacterIds).ToList();
            var request = new InstanceAllocationRequest(run.InstanceRunId, _instance.Id, _instance.ZoneId, _matchmaker.RunSize, characters);
            ZoneServerInfo? server = await _allocator.Allocate(request);
            if (server == null)
            {
                _logger.LogWarning("No server available for {Instance}; requeueing run", _instance.Id);
                foreach (QueueTicket ticket in run.Tickets)
                {
                    _matchmaker.Enqueue(ticket);
                }
                break;
            }
            await GrainFactory.GetGrain<IZoneDirectoryGrain>(GrainKeys.Global).AssignInstance(run.InstanceRunId, server);
            var assignment = new InstanceAssignment(run.InstanceRunId, _instance.Id, _instance.ZoneId, _time.GetUtcNow());
            await Task.WhenAll(characters.Select(id => GrainFactory.GetGrain<ICharacterGrain>(id).SetPendingInstance(assignment)));
            started++;
        }
        return started;
    }
}
