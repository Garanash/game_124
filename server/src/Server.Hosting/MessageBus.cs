using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using Realmforge.Server.Core;

namespace Realmforge.Server.Hosting;

/// <summary>Cross-process fan-out (chat, analytics). Falls back to an in-process bus when NATS is not configured.</summary>
public interface IMessageBus
{
    Task PublishAsync<T>(string subject, T payload);
    IAsyncEnumerable<(string Subject, T Payload)> SubscribeAsync<T>(string subjectPattern, CancellationToken cancellation);
}

public static class MessageBusRegistration
{

    public static IServiceCollection AddRealmforgeMessageBus(this IServiceCollection services, IConfiguration configuration)
    {
        string? nats = configuration.GetConnectionString(ConnectionNames.Nats);
        if (string.IsNullOrWhiteSpace(nats))
        {
            return services.AddSingleton<IMessageBus, InProcessMessageBus>();
        }
        services.AddSingleton(_ => new NatsConnection(new NatsOpts { Url = nats }));
        return services.AddSingleton<IMessageBus, NatsMessageBus>();
    }
}

public sealed class NatsMessageBus : IMessageBus
{
    private readonly NatsConnection _connection;

    public NatsMessageBus(NatsConnection connection)
    {
        _connection = connection;
    }

    public async Task PublishAsync<T>(string subject, T payload)
    {
        await _connection.PublishAsync(subject, JsonSerializer.SerializeToUtf8Bytes(payload));
    }

    public async IAsyncEnumerable<(string Subject, T Payload)> SubscribeAsync<T>(
        string subjectPattern,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
    {
        await foreach (NatsMsg<byte[]> message in _connection.SubscribeAsync<byte[]>(subjectPattern, cancellationToken: cancellation))
        {
            if (message.Data == null)
            {
                continue;
            }
            T? payload = JsonSerializer.Deserialize<T>(message.Data);
            if (payload != null)
            {
                yield return (message.Subject, payload);
            }
        }
    }
}

/// <summary>Single-process development bus; supports the NATS "&gt;" tail wildcard used by our subscriptions.</summary>
public sealed class InProcessMessageBus : IMessageBus
{
    private const string TailWildcard = ">";

    private readonly List<(string Pattern, System.Threading.Channels.Channel<(string, byte[])> Channel)> _subscribers = new();
    private readonly object _gate = new();
    private readonly ILogger<InProcessMessageBus> _logger;

    public InProcessMessageBus(ILogger<InProcessMessageBus> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync<T>(string subject, T payload)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        lock (_gate)
        {
            foreach ((string pattern, var channel) in _subscribers.Where(subscriber => Matches(subscriber.Pattern, subject)))
            {
                if (!channel.Writer.TryWrite((subject, bytes)))
                {
                    _logger.LogWarning("Dropped bus message on {Subject} for {Pattern}", subject, pattern);
                }
            }
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<(string Subject, T Payload)> SubscribeAsync<T>(
        string subjectPattern,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<(string, byte[])>();
        lock (_gate)
        {
            _subscribers.Add((subjectPattern, channel));
        }
        try
        {
            await foreach ((string subject, byte[] bytes) in channel.Reader.ReadAllAsync(cancellation))
            {
                T? payload = JsonSerializer.Deserialize<T>(bytes);
                if (payload != null)
                {
                    yield return (subject, payload);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _subscribers.RemoveAll(subscriber => subscriber.Channel == channel);
            }
        }
    }

    public static bool Matches(string pattern, string subject)
    {
        if (pattern.EndsWith(TailWildcard, StringComparison.Ordinal))
        {
            return subject.StartsWith(pattern[..^TailWildcard.Length], StringComparison.Ordinal);
        }
        return pattern == subject;
    }
}
