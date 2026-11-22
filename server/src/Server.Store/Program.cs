using Microsoft.AspNetCore.Authentication.JwtBearer;
using Realmforge.Rules.Definitions;
using Realmforge.Server.Core;
using Realmforge.Server.Core.Auth;
using Realmforge.Server.Core.LiveOps;
using Realmforge.Server.Core.Store;
using Realmforge.Server.Hosting;
using Realmforge.Server.Store;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddRealmforgeClusterClient();

ContentCatalog catalog = ContentSource.Load(builder.Configuration["Content:Path"]);
TokenOptions tokenOptions = builder.Configuration.GetSection(TokenOptions.SectionName).Get<TokenOptions>() ?? new TokenOptions();
StoreOptions storeOptions = builder.Configuration.GetSection(StoreOptions.SectionName).Get<StoreOptions>() ?? new StoreOptions();
FeatureFlags features = builder.Configuration.GetSection(FeatureFlags.SectionName).Get<FeatureFlags>() ?? new FeatureFlags();
var tokens = new TokenService(tokenOptions, TimeProvider.System);

builder.Services.AddSingleton(catalog);
builder.Services.AddSingleton(storeOptions);
builder.Services.AddSingleton(features);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRealmforgeMessageBus(builder.Configuration);
builder.Services.AddRealmforgeAnalytics();
AddReceiptValidators(builder.Services, storeOptions);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = tokens.ValidationParameters(TokenService.SessionAudience);
    });
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

WebApplication app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok());
app.MapStore();

await app.RunAsync();

static void AddReceiptValidators(IServiceCollection services, StoreOptions options)
{
    if (options.AllowDevPurchases)
    {
        services.AddSingleton<IReceiptValidator, DevReceiptValidator>();
    }
    foreach (string platform in new[] { StorePlatforms.Apple, StorePlatforms.Google, StorePlatforms.Steam })
    {
        services.AddSingleton<IReceiptValidator>(new UnconfiguredPlatformValidator(platform));
    }
}

public partial class Program;
