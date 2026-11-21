using System.Net.WebSockets;
using System.Threading.Channels;

namespace Realmforge.Server.Zone.Transport;

/// <summary>
/// Browser builds cannot open UDP sockets, so WebGL clients speak the same binary protocol over a WebSocket
/// served by Kestrel on <see cref="Path"/>.
/// </summary>
public sealed class WebSocketTransport : IZoneTransport
{
    public const string Path = "/ws";

    private readonly ZoneOptions _options;
    private readonly ConnectionInbox _inbox;

    public WebSocketTransport(ZoneOptions options, ConnectionInbox inbox)
    {
        _options = options;
        _inbox = inbox;
    }

    public void Start()
    {
    }

    public void Poll()
    {
    }

    public void Stop()
    {
    }

    public async Task Accept(HttpContext http)
    {
        if (!http.WebSockets.IsWebSocketRequest)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        using WebSocket socket = await http.WebSockets.AcceptWebSocketAsync();
        var connection = new WebSocketConnection(ConnectionInbox.NextConnectionId(), socket, http.Connection.RemoteIpAddress?.ToString() ?? "unknown", _options.SendQueueLength);
        _inbox.Post(new ConnectionEvent(connection, ConnectionEventKind.Connected, null));
        Task sending = connection.RunSendLoop(http.RequestAborted);
        try
        {
            await ReceiveLoop(connection, socket, http.RequestAborted);
        }
        finally
        {
            connection.Close();
            await sending;
            _inbox.Post(new ConnectionEvent(connection, ConnectionEventKind.Disconnected, null));
        }
    }

    private async Task ReceiveLoop(WebSocketConnection connection, WebSocket socket, CancellationToken cancellation)
    {
        var buffer = new byte[_options.MaxMessageBytes];
        while (socket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
        {
            int length = 0;
            ValueWebSocketReceiveResult received;
            do
            {
                if (length == buffer.Length)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "message too big", CancellationToken.None);
                    return;
                }
                received = await socket.ReceiveAsync(buffer.AsMemory(length), cancellation);
                length += received.Count;
            }
            while (!received.EndOfMessage);
            if (received.MessageType == WebSocketMessageType.Close)
            {
                return;
            }
            if (received.MessageType == WebSocketMessageType.Binary)
            {
                _inbox.Post(new ConnectionEvent(connection, ConnectionEventKind.Message, buffer[..length]));
            }
        }
    }

    private sealed class WebSocketConnection : IClientConnection
    {
        private readonly WebSocket _socket;
        private readonly Channel<byte[]> _outgoing;

        public WebSocketConnection(long id, WebSocket socket, string remoteAddress, int queueLength)
        {
            Id = id;
            _socket = socket;
            RemoteAddress = remoteAddress;
            _outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(queueLength)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
        }

        public long Id { get; }
        public string RemoteAddress { get; }

        /// <summary>TCP already orders and retransmits, so both delivery kinds share one queue.</summary>
        public void Send(byte[] payload, Delivery delivery) => _outgoing.Writer.TryWrite(payload);

        public void Close() => _outgoing.Writer.TryComplete();

        public async Task RunSendLoop(CancellationToken cancellation)
        {
            try
            {
                await foreach (byte[] payload in _outgoing.Reader.ReadAllAsync(cancellation))
                {
                    if (_socket.State != WebSocketState.Open)
                    {
                        return;
                    }
                    await _socket.SendAsync(payload, WebSocketMessageType.Binary, endOfMessage: true, cancellation);
                }
                if (_socket.State == WebSocketState.Open)
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
            {
            }
        }
    }
}
