using System;
using LiteNetLib;

namespace Realmforge.Client.Core.Net
{
    /// <summary>
    /// One game connection. Native builds use UDP (<see cref="UdpGameTransport"/>); WebGL uses a browser
    /// WebSocket implemented in the Unity layer. Events fire only from <see cref="Poll"/>, on the main thread.
    /// </summary>
    public interface IGameTransport
    {
        bool IsConnected { get; }
        event Action Connected;
        event Action<byte[]> Received;
        event Action<string> Disconnected;
        void Connect(string host, int udpPort, int webSocketPort);
        void Send(byte[] payload, bool reliable);
        void Poll();
        void Disconnect();
    }

    public sealed class UdpGameTransport : IGameTransport
    {
        public const string ConnectionKey = "realmforge";
        private const int ChannelCount = 2;
        private const byte ReliableChannel = 0;
        private const byte UnreliableChannel = 1;

        private readonly EventBasedNetListener _listener = new EventBasedNetListener();
        private readonly NetManager _manager;
        private NetPeer? _server;

        public UdpGameTransport()
        {
            _manager = new NetManager(_listener) { AutoRecycle = true, ChannelsCount = ChannelCount };
            _listener.PeerConnectedEvent += _ => Connected?.Invoke();
            _listener.NetworkReceiveEvent += (peer, reader, channel, method) => Received?.Invoke(reader.GetRemainingBytes());
            _listener.PeerDisconnectedEvent += (peer, info) =>
            {
                if (peer != _server)
                {
                    return;
                }
                _server = null;
                Disconnected?.Invoke(info.Reason.ToString());
            };
        }

        public event Action? Connected;
        public event Action<byte[]>? Received;
        public event Action<string>? Disconnected;

        public bool IsConnected => _server != null && _server.ConnectionState == ConnectionState.Connected;

        public void Connect(string host, int udpPort, int webSocketPort)
        {
            if (!_manager.IsRunning)
            {
                _manager.Start();
            }
            _server = _manager.Connect(host, udpPort, ConnectionKey);
        }

        public void Send(byte[] payload, bool reliable)
        {
            _server?.Send(payload, reliable ? ReliableChannel : UnreliableChannel, reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced);
        }

        public void Poll() => _manager.PollEvents();

        public void Disconnect()
        {
            _server?.Disconnect();
            _server = null;
        }
    }
}
