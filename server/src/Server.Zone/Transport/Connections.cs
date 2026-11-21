using System.Collections.Concurrent;

namespace Realmforge.Server.Zone.Transport;

public enum Delivery
{
    /// <summary>Game state changes the client must not miss: spawns, combat events, dialogue.</summary>
    Reliable,
    /// <summary>Position snapshots: a newer one supersedes a lost one.</summary>
    Latest,
}

public interface IClientConnection
{
    long Id { get; }
    string RemoteAddress { get; }
    void Send(byte[] payload, Delivery delivery);
    void Close();
}

public enum ConnectionEventKind
{
    Connected,
    Message,
    Disconnected,
}

public readonly record struct ConnectionEvent(IClientConnection Connection, ConnectionEventKind Kind, byte[]? Payload);

/// <summary>Transports run on their own threads; the game loop drains this queue once per tick.</summary>
public sealed class ConnectionInbox
{
    private static long _lastConnectionId;
    private readonly ConcurrentQueue<ConnectionEvent> _events = new();

    public static long NextConnectionId() => Interlocked.Increment(ref _lastConnectionId);

    public void Post(ConnectionEvent connectionEvent) => _events.Enqueue(connectionEvent);

    public bool TryTake(out ConnectionEvent connectionEvent) => _events.TryDequeue(out connectionEvent);
}

public interface IZoneTransport
{
    void Start();
    /// <summary>Called from the game loop; transports with their own threads may do nothing.</summary>
    void Poll();
    void Stop();
}
