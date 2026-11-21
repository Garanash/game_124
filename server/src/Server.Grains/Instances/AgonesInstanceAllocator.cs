using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Realmforge.Server.Grains.Instances;

public sealed class AgonesOptions
{
    public const string SectionName = "Agones";

    /// <summary>Base URL of the Agones allocator service, e.g. https://agones-allocator.agones-system:443.</summary>
    public string AllocatorUrl { get; set; } = string.Empty;
    public string Namespace { get; set; } = "realmforge";
    public string Fleet { get; set; } = "realmforge-instances";
    public string UdpPortName { get; set; } = "game";
    public string WebSocketPortName { get; set; } = "ws";
}

/// <summary>
/// Allocates a Ready game server from the instance fleet. The run id travels as a label; the instance
/// process reads it from the Agones SDK and registers itself in the zone directory.
/// </summary>
public sealed class AgonesInstanceAllocator : IInstanceAllocator
{
    public const string RunLabel = "realmforge.io/run";
    public const string InstanceLabel = "realmforge.io/instance";
    public const string ZoneLabel = "realmforge.io/zone";
    private const string FleetLabel = "agones.dev/fleet";
    private const string AllocationPath = "/gameserverallocation";

    private readonly HttpClient _http;
    private readonly AgonesOptions _options;

    public AgonesInstanceAllocator(HttpClient http, AgonesOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<ZoneServerInfo?> Allocate(InstanceAllocationRequest request)
    {
        var body = new AllocationRequest(
            _options.Namespace,
            new[] { new Selector(new Dictionary<string, string> { [FleetLabel] = _options.Fleet }) },
            new Metadata(new Dictionary<string, string>
            {
                [RunLabel] = request.RunId.ToString("N"),
                [InstanceLabel] = request.InstanceId,
                [ZoneLabel] = request.ZoneId,
            }));
        using HttpResponseMessage response = await _http.PostAsJsonAsync(_options.AllocatorUrl.TrimEnd('/') + AllocationPath, body);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        AllocationResponse? allocation = await response.Content.ReadFromJsonAsync<AllocationResponse>();
        if (allocation == null || string.IsNullOrEmpty(allocation.Address))
        {
            return null;
        }
        int udpPort = allocation.Ports.FirstOrDefault(port => port.Name == _options.UdpPortName)?.Port ?? 0;
        int webSocketPort = allocation.Ports.FirstOrDefault(port => port.Name == _options.WebSocketPortName)?.Port ?? 0;
        return new ZoneServerInfo(allocation.GameServerName, new[] { request.ZoneId }, allocation.Address, udpPort, webSocketPort, 0, request.RunId);
    }

    private sealed record Selector([property: JsonPropertyName("matchLabels")] Dictionary<string, string> MatchLabels);

    private sealed record Metadata([property: JsonPropertyName("labels")] Dictionary<string, string> Labels);

    private sealed record AllocationRequest(
        [property: JsonPropertyName("namespace")] string Namespace,
        [property: JsonPropertyName("gameServerSelectors")] Selector[] GameServerSelectors,
        [property: JsonPropertyName("metadata")] Metadata Metadata);

    private sealed record AllocatedPort(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("port")] int Port);

    private sealed record AllocationResponse(
        [property: JsonPropertyName("gameServerName")] string GameServerName,
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("ports")] AllocatedPort[] Ports);
}
