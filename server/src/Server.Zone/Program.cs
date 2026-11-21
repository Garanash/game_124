using Realmforge.Rules.Definitions;
using Realmforge.Server.Core;
using Realmforge.Server.Core.Auth;
using Realmforge.Server.Hosting;
using Realmforge.Server.Zone;
using Realmforge.Server.Zone.Transport;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddRealmforgeClusterClient();

ZoneOptions zoneOptions = builder.Configuration.GetSection(ZoneOptions.SectionName).Get<ZoneOptions>() ?? new ZoneOptions();
TokenOptions tokenOptions = builder.Configuration.GetSection(TokenOptions.SectionName).Get<TokenOptions>() ?? new TokenOptions();
ContentCatalog catalog = ContentSource.Load(builder.Configuration["Content:Path"]);
builder.WebHost.UseUrls($"http://0.0.0.0:{zoneOptions.WebSocketPort}");

builder.Services.AddSingleton(zoneOptions);
builder.Services.AddSingleton(catalog);
builder.Services.AddSingleton(new TokenService(tokenOptions, TimeProvider.System));
builder.Services.AddSingleton<ConnectionInbox>();
builder.Services.AddSingleton<UdpTransport>();
builder.Services.AddSingleton<WebSocketTransport>();
builder.Services.AddSingleton<IZoneTransport>(services => services.GetRequiredService<UdpTransport>());
builder.Services.AddSingleton<IZoneTransport>(services => services.GetRequiredService<WebSocketTransport>());
builder.Services.AddRealmforgeMessageBus(builder.Configuration);
AddGameServerLifecycle(builder.Services);
builder.Services.AddHostedService<ZoneRuntime>();
builder.Services.AddHostedService<GameServerHealthReporter>();

WebApplication app = builder.Build();
app.UseWebSockets();
app.MapGet("/health", () => Results.Ok());
app.Map(WebSocketTransport.Path, (HttpContext http, WebSocketTransport transport) => transport.Accept(http));

await app.RunAsync();

static void AddGameServerLifecycle(IServiceCollection services)
{
    int? agonesPort = AgonesSdkLifecycle.SdkPortFromEnvironment();
    if (agonesPort == null)
    {
        services.AddSingleton<IGameServerLifecycle, StandaloneLifecycle>();
        return;
    }
    services.AddHttpClient(nameof(AgonesSdkLifecycle));
    services.AddSingleton<IGameServerLifecycle>(provider => new AgonesSdkLifecycle(
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AgonesSdkLifecycle)),
        agonesPort.Value,
        provider.GetRequiredService<ILogger<AgonesSdkLifecycle>>()));
}
