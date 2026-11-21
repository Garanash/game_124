namespace Realmforge.Server.Core.Matchmaking;

/// <summary>A premade group (or solo player) queued together; groups are never split.</summary>
public sealed record QueueTicket(Guid TicketId, IReadOnlyList<Guid> CharacterIds, int AverageLevel, DateTimeOffset QueuedAt);

public sealed record MatchedGroup(Guid InstanceRunId, IReadOnlyList<QueueTicket> Tickets)
{
    public IEnumerable<Guid> CharacterIds => Tickets.SelectMany(ticket => ticket.CharacterIds);
}

/// <summary>
/// Fills instance runs of an exact size. The oldest ticket anchors a run and is completed with the
/// largest groups that still fit, preferring close levels, so long-waiting players are served first.
/// </summary>
public sealed class Matchmaker
{
    /// <summary>Levels within this window are considered a fair match; it widens the longer the anchor waits.</summary>
    private const int BaseLevelWindow = 3;
    private static readonly TimeSpan WindowGrowthInterval = TimeSpan.FromSeconds(30);

    private readonly List<QueueTicket> _queue = new();

    public Matchmaker(int runSize)
    {
        if (runSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(runSize));
        }
        RunSize = runSize;
    }

    public int RunSize { get; }
    public IReadOnlyList<QueueTicket> Queue => _queue;

    public DomainResult Enqueue(QueueTicket ticket)
    {
        if (ticket.CharacterIds.Count == 0 || ticket.CharacterIds.Count > RunSize)
        {
            return DomainResult.Fail("queue.group_size", $"Group must have 1-{RunSize} members.");
        }
        HashSet<Guid> queued = _queue.SelectMany(existing => existing.CharacterIds).ToHashSet();
        if (ticket.CharacterIds.Any(queued.Contains))
        {
            return DomainResult.Fail("queue.already_queued", "A member is already queued.");
        }
        _queue.Add(ticket);
        return DomainResult.Ok;
    }

    public bool Cancel(Guid characterId)
    {
        return _queue.RemoveAll(ticket => ticket.CharacterIds.Contains(characterId)) > 0;
    }

    public IReadOnlyList<MatchedGroup> FormRuns(DateTimeOffset now)
    {
        var runs = new List<MatchedGroup>();
        while (TryFormRun(now, out MatchedGroup? run))
        {
            runs.Add(run!);
        }
        return runs;
    }

    private bool TryFormRun(DateTimeOffset now, out MatchedGroup? run)
    {
        foreach (QueueTicket anchor in _queue.OrderBy(ticket => ticket.QueuedAt))
        {
            int window = BaseLevelWindow + (int)((now - anchor.QueuedAt) / WindowGrowthInterval);
            var chosen = new List<QueueTicket> { anchor };
            int filled = anchor.CharacterIds.Count;
            IEnumerable<QueueTicket> candidates = _queue
                .Where(ticket => ticket != anchor && Math.Abs(ticket.AverageLevel - anchor.AverageLevel) <= window)
                .OrderByDescending(ticket => ticket.CharacterIds.Count)
                .ThenBy(ticket => Math.Abs(ticket.AverageLevel - anchor.AverageLevel))
                .ThenBy(ticket => ticket.QueuedAt);
            foreach (QueueTicket candidate in candidates)
            {
                if (filled + candidate.CharacterIds.Count <= RunSize)
                {
                    chosen.Add(candidate);
                    filled += candidate.CharacterIds.Count;
                }
                if (filled == RunSize)
                {
                    break;
                }
            }
            if (filled == RunSize)
            {
                foreach (QueueTicket ticket in chosen)
                {
                    _queue.Remove(ticket);
                }
                run = new MatchedGroup(Guid.NewGuid(), chosen);
                return true;
            }
        }
        run = null;
        return false;
    }
}
