using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Realmforge.Client.Core.Api;
using Realmforge.Client.Core.Net;
using Realmforge.Client.Core.World;
using Realmforge.Protocol;
using Realmforge.Rules.Characters;

namespace Realmforge.Client.Core.Session
{
    public enum SessionPhase
    {
        LoggedOut,
        SelectingCharacter,
        Connecting,
        InWorld,
        Transferring,
    }

    /// <summary>
    /// Drives the player's journey: login, character list, entering a zone, hopping between zones.
    /// Unity calls <see cref="Tick"/> every frame; everything else is triggered by UI.
    /// </summary>
    public sealed class GameSession
    {
        public const float PingIntervalSeconds = 2f;

        private readonly GatewayClient _gateway;
        private readonly IGameTransport _transport;
        private string _ticket = string.Empty;
        private float _sincePing;

        public GameSession(GatewayClient gateway, IGameTransport transport)
        {
            _gateway = gateway;
            _transport = transport;
            _transport.Connected += SendHello;
            _transport.Received += OnReceived;
            _transport.Disconnected += OnDisconnected;
            World.ZoneTransferRequested += BeginTransfer;
            World.ZoneEntered += _ => Phase = SessionPhase.InWorld;
        }

        public SessionPhase Phase { get; private set; } = SessionPhase.LoggedOut;
        public WorldState World { get; } = new WorldState();
        public List<CharacterSummaryDto> Characters { get; private set; } = new List<CharacterSummaryDto>();
        public CharacterSummaryDto? ActiveCharacter { get; private set; }
        public GatewayClient Gateway => _gateway;

        public event Action<SessionPhase>? PhaseChanged;
        public event Action<string>? ConnectionLost;

        public async Task Login(string email, string password, bool register)
        {
            if (register)
            {
                await _gateway.Register(email, password);
            }
            else
            {
                await _gateway.Login(email, password);
            }
            await RefreshCharacters();
            SetPhase(SessionPhase.SelectingCharacter);
        }

        public async Task RefreshCharacters()
        {
            Characters = await _gateway.ListCharacters();
        }

        public async Task<CharacterSummaryDto> CreateCharacter(CreateCharacterRequest request)
        {
            CharacterSummaryDto created = await _gateway.CreateCharacter(request);
            await RefreshCharacters();
            return created;
        }

        public async Task EnterWorld(CharacterSummaryDto character)
        {
            ActiveCharacter = character;
            SetPhase(SessionPhase.Connecting);
            EnterWorldDto entry = await _gateway.EnterWorld(character.CharacterId);
            Connect(entry.Host, entry.UdpPort, entry.WebSocketPort, entry.Ticket);
        }

        public void LeaveWorld()
        {
            _transport.Disconnect();
            ActiveCharacter = null;
            SetPhase(SessionPhase.SelectingCharacter);
        }

        public void Send(IClientMessage message)
        {
            if (_transport.IsConnected)
            {
                _transport.Send(ProtocolCodec.Encode(message), reliable: true);
            }
        }

        public void Tick(float deltaSeconds)
        {
            _transport.Poll();
            World.Advance(deltaSeconds);
            if (Phase != SessionPhase.InWorld)
            {
                return;
            }
            _sincePing += deltaSeconds;
            if (_sincePing >= PingIntervalSeconds)
            {
                _sincePing = 0;
                Send(new PingRequest { ClientTime = World.Clock.LocalTime });
            }
        }

        private void Connect(string host, int udpPort, int webSocketPort, string ticket)
        {
            _ticket = ticket;
            _transport.Connect(host, udpPort, webSocketPort);
        }

        private void SendHello()
        {
            Send(new HelloRequest { Ticket = _ticket, ProtocolVersion = ProtocolCodec.ProtocolVersion });
        }

        private void OnReceived(byte[] payload)
        {
            IServerMessage? message = ProtocolCodec.DecodeServer(payload);
            if (message != null)
            {
                World.Apply(message);
            }
        }

        /// <summary>The old zone drops us after a portal; the transfer message already carries the next ticket.</summary>
        private void BeginTransfer(ZoneTransferMessage transfer)
        {
            SetPhase(SessionPhase.Transferring);
            _transport.Disconnect();
            Connect(transfer.Host, transfer.Port, transfer.WebSocketPort, transfer.Ticket);
        }

        private void OnDisconnected(string reason)
        {
            if (Phase == SessionPhase.InWorld || Phase == SessionPhase.Connecting)
            {
                SetPhase(SessionPhase.SelectingCharacter);
                ConnectionLost?.Invoke(reason);
            }
        }

        private void SetPhase(SessionPhase phase)
        {
            if (Phase == phase)
            {
                return;
            }
            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }
    }
}
