using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Realmforge.Server.Hosting;

public sealed record AnalyticsEvent(
    string Name,
    Guid? AccountId,
    Guid? CharacterId,
    IReadOnlyDictionary<string, string> Properties,
    DateTimeOffset OccurredAt);

/// <summary>
/// Fire-and-forget product analytics. Events go to the bus under "analytics.&lt;name&gt;", where a warehouse
/// loader (or any NATS consumer) picks them up; gameplay never waits for or fails because of analytics.
/// </summary>
public interface IAnalytics
{
    void Track(string name, Guid? accountId = null, Guid? characterId = null, IReadOnlyDictionary<string, string>? properties = null);
}

public static class AnalyticsEvents
{
    public const string SubjectPrefix = "analytics.";
    public const string AccountRegistered = "account_registered";
    public const string LoggedIn = "logged_in";
    public const string CharacterCreated = "character_created";
    public const string PurchaseCompleted = "purchase_completed";
    public const string PurchaseRejected = "purchase_rejected";
    public const string CrystalsSpent = "crystals_spent";
}

public sealed class BusAnalytics : IAnalytics
{
    private readonly IMessageBus _bus;
    private readonly TimeProvider _time;
    private readonly ILogger<BusAnalytics> _logger;

    public BusAnalytics(IMessageBus bus, TimeProvider time, ILogger<BusAnalytics> logger)
    {
        _bus = bus;
        _time = time;
        _logger = logger;
    }

    public void Track(string name, Guid? accountId = null, Guid? characterId = null, IReadOnlyDictionary<string, string>? properties = null)
    {
        var analyticsEvent = new AnalyticsEvent(name, accountId, characterId, properties ?? new Dictionary<string, string>(), _time.GetUtcNow());
        _ = PublishQuietly(analyticsEvent);
    }

    private async Task PublishQuietly(AnalyticsEvent analyticsEvent)
    {
        try
        {
            await _bus.PublishAsync(AnalyticsEvents.SubjectPrefix + analyticsEvent.Name, analyticsEvent);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Dropped analytics event {Event}", analyticsEvent.Name);
        }
    }
}

public static class AnalyticsRegistration
{
    public static IServiceCollection AddRealmforgeAnalytics(this IServiceCollection services) =>
        services.AddSingleton<IAnalytics, BusAnalytics>();
}
