using LiteNetLib;

namespace Realmforge.Server.Zone.Transport;

/// <summary>Native clients (desktop, mobile) connect over reliable UDP.</summary>
public sealed class UdpTransport : IZoneTransport
{
    private const byte ReliableChannel = 0;
    private const byte SnapshotChannel = 1;
    private const int ChannelCount = 2;
    private const int DisconnectTimeoutMilliseconds = 10_000;

    private readonly ZoneOptions _options;
    private readonly ConnectionInbox _inbox;
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _manager;
    private readonly Dictionary<int, UdpConnection> _connections = new();
    private readonly ILogger<UdpTransport> _logger;

    public UdpTransport(ZoneOptions options, ConnectionInbox inbox, ILogger<UdpTransport> logger)
    {
        _options = options;
        _inbox = inbox;
        _logger = logger;
        _manager = new NetManager(_listener)
        {
            AutoRecycle = true,
            ChannelsCount = ChannelCount,
            DisconnectTimeout = DisconnectTimeoutMilliseconds,
        };
        _listener.ConnectionRequestEvent += request => request.AcceptIfKey(_options.ConnectionKey);
        _listener.PeerConnectedEvent += OnConnected;
        _listener.NetworkReceiveEvent += OnReceive;
        _listener.PeerDisconnectedEvent += OnDisconnected;
    }

    public void Start()
    {
        if (!_manager.Start(_options.UdpPort))
        {
            throw new InvalidOperationException($"Cannot bind UDP port {_options.UdpPort}.");
        }
        _logger.LogInformation("UDP transport listening on {Port}", _options.UdpPort);
    }

    public void Poll() => _manager.PollEvents();

    public void Stop() => _manager.Stop();

    private void OnConnected(NetPeer peer)
    {
        var connection = new UdpConnection(ConnectionInbox.NextConnectionId(), peer);
        _connections[peer.Id] = connection;
        _inbox.Post(new ConnectionEvent(connection, ConnectionEventKind.Connected, null));
    }

    private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        if (!_connections.TryGetValue(peer.Id, out UdpConnection? connection))
        {
            return;
        }
        if (reader.AvailableBytes > _options.MaxMessageBytes)
        {
            peer.Disconnect();
            return;
        }
        _inbox.Post(new ConnectionEvent(connection, ConnectionEventKind.Message, reader.GetRemainingBytes()));
    }

    private void OnDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (_connections.Remove(peer.Id, out UdpConnection? connection))
        {
            _inbox.Post(new ConnectionEvent(connection, ConnectionEventKind.Disconnected, null));
        }
    }

    private sealed class UdpConnection : IClientConnection
    {
        private readonly NetPeer _peer;

        public UdpConnection(long id, NetPeer peer)
        {
            Id = id;
            _peer = peer;
        }

        public long Id { get; }
        public string RemoteAddress => _peer.Address.ToString();

        public void Send(byte[] payload, Delivery delivery)
        {
            if (delivery == Delivery.Latest)
            {
                _peer.Send(payload, SnapshotChannel, DeliveryMethod.Sequenced);
                return;
            }
            _peer.Send(payload, ReliableChannel, DeliveryMethod.ReliableOrdered);
        }

        public void Close() => _peer.Disconnect();
    }
}
