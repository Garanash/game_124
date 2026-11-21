using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Npgsql;
using Realmforge.Rules.Definitions;
using Realmforge.Server.Core;
using Realmforge.Server.Core.Auth;
using Realmforge.Server.Gateway.Accounts;
using Realmforge.Server.Gateway.Endpoints;
using Realmforge.Server.Grains;
using Realmforge.Server.Hosting;

const int AuthRequestsPerMinute = 10;
const string WebClientCorsPolicy = "web-client";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddRealmforgeClusterClient();

ContentCatalog catalog = ContentSource.Load(builder.Configuration["Content:Path"]);
TokenOptions tokenOptions = builder.Configuration.GetSection(TokenOptions.SectionName).Get<TokenOptions>() ?? new TokenOptions();
var tokens = new TokenService(tokenOptions, TimeProvider.System);

builder.Services.AddSingleton(catalog);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(tokens);
AddAccountStore(builder);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = tokens.ValidationParameters(TokenService.SessionAudience);
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = AuthRequestsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddCors(cors => cors.AddPolicy(WebClientCorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>())
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

WebApplication app = builder.Build();
app.UseCors(WebClientCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok());
app.MapAuth();
app.MapCharacters();
app.MapSocial();
app.MapEconomy();

await app.RunAsync();

static void AddAccountStore(WebApplicationBuilder builder)
{
    string? postgres = builder.Configuration.GetConnectionString(ConnectionNames.Postgres);
    if (string.IsNullOrWhiteSpace(postgres))
    {
        builder.Services.AddSingleton<IAccountStore, InMemoryAccountStore>();
        return;
    }
    builder.Services.AddSingleton(NpgsqlDataSource.Create(postgres));
    builder.Services.AddSingleton<IAccountStore, PostgresAccountStore>();
}

public partial class Program;
