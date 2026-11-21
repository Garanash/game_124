namespace Realmforge.Server.Zone;

/// <summary>Lets the zone tell its orchestrator when it can take players and when it is done.</summary>
public interface IGameServerLifecycle
{
    /// <summary>Dedicated instance servers host no open world and exit after their run closes.</summary>
    bool IsDedicatedInstanceServer { get; }
    Task MarkReady(CancellationToken cancellation);
    Task ReportHealth(CancellationToken cancellation);
    Task RequestShutdown(CancellationToken cancellation);
}

/// <summary>Plain processes (local runs, docker-compose, open-world deployments) have nobody to report to.</summary>
public sealed class StandaloneLifecycle : IGameServerLifecycle
{
    public bool IsDedicatedInstanceServer => false;
    public Task MarkReady(CancellationToken cancellation) => Task.CompletedTask;
    public Task ReportHealth(CancellationToken cancellation) => Task.CompletedTask;
    public Task RequestShutdown(CancellationToken cancellation) => Task.CompletedTask;
}

/// <summary>
/// Talks to the Agones SDK sidecar over its local REST API. A game server that stops sending health pings is
/// restarted by Agones, and one that calls shutdown is replaced by a fresh Ready server in the fleet.
/// </summary>
public sealed class AgonesSdkLifecycle : IGameServerLifecycle
{
    public const string SdkPortVariable = "AGONES_SDK_HTTP_PORT";
    private const string EmptyJson = "{}";

    private readonly HttpClient _http;
    private readonly ILogger<AgonesSdkLifecycle> _logger;

    public AgonesSdkLifecycle(HttpClient http, int sdkPort, ILogger<AgonesSdkLifecycle> logger)
    {
        _http = http;
        _http.BaseAddress = new Uri($"http://localhost:{sdkPort}");
        _logger = logger;
    }

    public bool IsDedicatedInstanceServer => true;

    public Task MarkReady(CancellationToken cancellation) => Post("/ready", cancellation);

    public Task ReportHealth(CancellationToken cancellation) => Post("/health", cancellation);

    public Task RequestShutdown(CancellationToken cancellation) => Post("/shutdown", cancellation);

    /// <summary>The sidecar port is injected by Agones into every game server pod.</summary>
    public static int? SdkPortFromEnvironment() =>
        int.TryParse(Environment.GetEnvironmentVariable(SdkPortVariable), out int port) ? port : null;

    private async Task Post(string path, CancellationToken cancellation)
    {
        using var content = new StringContent(EmptyJson, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.PostAsync(path, content, cancellation);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Agones SDK {Path} returned {Status}", path, (int)response.StatusCode);
        }
    }
}

/// <summary>Sends readiness once the transports are up and keeps the health ping going.</summary>
public sealed class GameServerHealthReporter : BackgroundService
{
    public static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(2);

    private readonly IGameServerLifecycle _lifecycle;
    private readonly ILogger<GameServerHealthReporter> _logger;

    public GameServerHealthReporter(IGameServerLifecycle lifecycle, ILogger<GameServerHealthReporter> logger)
    {
        _lifecycle = lifecycle;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _lifecycle.MarkReady(stoppingToken);
        using var timer = new PeriodicTimer(HealthInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _lifecycle.ReportHealth(stoppingToken);
            }
            catch (HttpRequestException exception)
            {
                _logger.LogWarning(exception, "Health ping failed");
            }
        }
    }
}
