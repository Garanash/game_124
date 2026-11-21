namespace Realmforge.Server.Grains.Instances;

public sealed record InstanceAllocationRequest(Guid RunId, string InstanceId, string ZoneId, int PartySize, IReadOnlyList<Guid> CharacterIds);

/// <summary>Finds a server process for a fresh instance run: an Agones game server in production, any zone server in dev.</summary>
public interface IInstanceAllocator
{
    Task<ZoneServerInfo?> Allocate(InstanceAllocationRequest request);
}

/// <summary>
/// Dev and single-node fallback: zone servers can host instance simulations on demand, so the run is
/// placed on the least loaded zone server that is alive.
/// </summary>
public sealed class LocalInstanceAllocator : IInstanceAllocator
{
    private readonly IGrainFactory _grains;

    public LocalInstanceAllocator(IGrainFactory grains)
    {
        _grains = grains;
    }

    public async Task<ZoneServerInfo?> Allocate(InstanceAllocationRequest request)
    {
        IReadOnlyList<ZoneServerInfo> servers = await _grains.GetGrain<IZoneDirectoryGrain>(GrainKeys.Global).List();
        ZoneServerInfo? chosen = servers.Where(server => server.InstanceRunId == null).OrderBy(server => server.Players).FirstOrDefault();
        return chosen == null ? null : chosen with { InstanceRunId = request.RunId, ZoneIds = new[] { request.ZoneId } };
    }
}
