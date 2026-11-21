using System.Collections.Concurrent;
using System.Diagnostics;
using Realmforge.Protocol;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Dice;
using Realmforge.Server.Core.Auth;
using Realmforge.Server.Core.Characters;
using Realmforge.Server.Core.Social;
using Realmforge.Server.Grains;
using Realmforge.Server.Hosting;
using Realmforge.Server.World;
using Realmforge.Server.Zone.Transport;

namespace Realmforge.Server.Zone;

/// <summary>
/// The zone process main loop. Everything that touches simulations or sessions runs on this one thread;
/// async work (grain calls, bus) posts its results back through <see cref="Post"/>.
/// </summary>
public sealed class ZoneRuntime : BackgroundService, IZoneOutput
{
    private const string ChatSubjectPattern = "chat.>";
    private const string PartySubjectPrefix = "chat.party.";
    private const string GuildSubjectPrefix = "chat.guild.";
    private const string TradeSubject = "chat.trade";

    private readonly ZoneOptions _options;
    private readonly ContentCatalog _catalog;
    private readonly ConnectionInbox _inbox;
    private readonly IReadOnlyList<IZoneTransport> _transports;
    private readonly IGrainFactory _grains;
    private readonly TokenService _tokens;
    private readonly IMessageBus _bus;
    private readonly ILogger<ZoneRuntime> _logger;
    private readonly EffectDispatcher _effects;
    private readonly IGameServerLifecycle _lifecycle;
    private readonly ConcurrentQueue<Action> _loopActions = new();
    private readonly Dictionary<string, ZoneSimulation> _zones = new();
    private readonly Dictionary<Guid, HostedInstance> _instances = new();
    private readonly Dictionary<long, Session> _sessions = new();
    private readonly Dictionary<Guid, Session> _sessionsByCharacter = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastHeartbeat;
    private TimeSpan _lastMembershipRefresh;

    public ZoneRuntime(
        ZoneOptions options,
        ContentCatalog catalog,
        ConnectionInbox inbox,
        IEnumerable<IZoneTransport> transports,
        IGrainFactory grains,
        TokenService tokens,
        IMessageBus bus,
        IGameServerLifecycle lifecycle,
        ILogger<ZoneRuntime> logger)
    {
        _options = options;
        _lifecycle = lifecycle;
        _catalog = catalog;
        _inbox = inbox;
        _transports = transports.ToList();
        _grains = grains;
        _tokens = tokens;
        _bus = bus;
        _logger = logger;
        _effects = new EffectDispatcher(grains, catalog, tokens, bus, this, logger);
        _lastHeartbeat = -options.HeartbeatInterval;
    }

    private sealed class Session
    {
        public Session(IClientConnection connection, TimeSpan connectedAt)
        {
            Connection = connection;
            ConnectedAt = connectedAt;
        }

        public IClientConnection Connection { get; }
        public TimeSpan ConnectedAt { get; }
        public ZoneTicket? Ticket { get; set; }
        public ZoneSimulation? Simulation { get; set; }
        public long EntityId { get; set; }
        public bool IsJoining { get; set; }
        public bool IsClosed { get; set; }
    }

    private sealed class HostedInstance
    {
        public HostedInstance(ZoneSimulation simulation, TimeSpan createdAt)
        {
            Simulation = simulation;
            LastOccupiedAt = createdAt;
        }

        public ZoneSimulation Simulation { get; }
        public TimeSpan LastOccupiedAt { get; set; }
    }

    public void Post(Action action) => _loopActions.Enqueue(action);

    public void Send(Guid characterId, IServerMessage message)
    {
        if (_sessionsByCharacter.TryGetValue(characterId, out Session? session))
        {
            Deliver(session, message);
        }
    }

    public void RefreshCharacter(Guid characterId, CharacterState state)
    {
        if (_sessionsByCharacter.TryGetValue(characterId, out Session? session) && session.Simulation != null)
        {
            session.Simulation.ReplaceSheet(characterId, state.Sheet, state.Backpack);
            session.Simulation.UpdateMembership(characterId, state.PartyId, state.GuildId);
        }
    }

    public Guid? AccountOf(Guid characterId) => _sessionsByCharacter.GetValueOrDefault(characterId)?.Ticket?.AccountId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (ZoneDefinition zone in HostedZones())
        {
            _zones[zone.Id] = CreateSimulation(zone);
        }
        _logger.LogInformation("Zone server {Server} hosts {Count} zones: {Zones}", _options.ServerId, _zones.Count, string.Join(", ", _zones.Keys));
        foreach (IZoneTransport transport in _transports)
        {
            transport.Start();
        }
        _ = Task.Run(() => RelayChat(stoppingToken), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(ZoneSimulation.TickSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                RunFrame();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            SaveEveryone();
            foreach (IZoneTransport transport in _transports)
            {
                transport.Stop();
            }
        }
    }

    private void RunFrame()
    {
        foreach (IZoneTransport transport in _transports)
        {
            transport.Poll();
        }
        while (_inbox.TryTake(out ConnectionEvent connectionEvent))
        {
            HandleConnectionEvent(connectionEvent);
        }
        while (_loopActions.TryDequeue(out Action? action))
        {
            action();
        }
        foreach (ZoneSimulation simulation in AllSimulations())
        {
            simulation.Tick();
            FlushOutbox(simulation);
            foreach (ZoneEffect effect in simulation.DrainEffects())
            {
                _effects.Dispatch(effect);
            }
        }
        DropSilentConnections();
        RetireEmptyInstances();
        PeriodicHousekeeping();
    }

    private void HandleConnectionEvent(ConnectionEvent connectionEvent)
    {
        IClientConnection connection = connectionEvent.Connection;
        switch (connectionEvent.Kind)
        {
            case ConnectionEventKind.Connected:
                _sessions[connection.Id] = new Session(connection, _clock.Elapsed);
                break;
            case ConnectionEventKind.Disconnected:
                EndSession(connection.Id);
                break;
            case ConnectionEventKind.Message when _sessions.TryGetValue(connection.Id, out Session? session):
                HandleMessage(session, connectionEvent.Payload!);
                break;
        }
    }

    private void HandleMessage(Session session, byte[] payload)
    {
        IClientMessage? message;
        try
        {
            message = ProtocolCodec.DecodeClient(payload);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.LogWarning("Malformed packet from {Address}: {Error}", session.Connection.RemoteAddress, exception.Message);
            Kick(session, "protocol.malformed", "Повреждённый пакет.");
            return;
        }
        if (message == null)
        {
            return;
        }
        if (session.Simulation != null)
        {
            session.Simulation.Handle(session.EntityId, message);
            return;
        }
        if (message is HelloRequest hello && !session.IsJoining)
        {
            BeginJoin(session, hello);
        }
    }

    private void BeginJoin(Session session, HelloRequest hello)
    {
        if (hello.ProtocolVersion != ProtocolCodec.ProtocolVersion)
        {
            Kick(session, "protocol.version", "Обновите клиент игры.");
            return;
        }
        ZoneTicket? ticket = _tokens.ValidateZoneTicket(hello.Ticket);
        ZoneSimulation? simulation = ticket == null ? null : SimulationFor(ticket);
        if (ticket == null || simulation == null)
        {
            Kick(session, "auth.ticket", "Билет входа недействителен или зона не на этом сервере.");
            return;
        }
        session.Ticket = ticket;
        session.IsJoining = true;
        _ = LoadAndJoin(session, ticket, simulation);
    }

    private async Task LoadAndJoin(Session session, ZoneTicket ticket, ZoneSimulation simulation)
    {
        try
        {
            ICharacterGrain character = _grains.GetGrain<ICharacterGrain>(ticket.CharacterId);
            CharacterState state = await character.GetState();
            RewardOutcome? offlineReport = await character.TakeOfflineReport();
            Post(() => CompleteJoin(session, ticket, simulation, state, offlineReport));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Loading character {Character} failed", ticket.CharacterId);
            Post(() => Kick(session, "character.load_failed", "Не удалось загрузить персонажа."));
        }
    }

    private void CompleteJoin(Session session, ZoneTicket ticket, ZoneSimulation simulation, CharacterState state, RewardOutcome? offlineReport)
    {
        session.IsJoining = false;
        if (session.IsClosed || !state.Exists)
        {
            return;
        }
        if (_sessionsByCharacter.TryGetValue(ticket.CharacterId, out Session? previous) && previous != session)
        {
            Kick(previous, "session.replaced", "Вход выполнен с другого устройства.");
        }
        PlayerEntity player = simulation.AddPlayer(new PlayerJoin(
            state.Sheet, state.Backpack, state.PartyId, state.GuildId, state.FactionId, state.Appearance.Slots.Values.ToArray()));
        session.Simulation = simulation;
        session.EntityId = player.Id;
        _sessionsByCharacter[ticket.CharacterId] = session;
        if (offlineReport != null)
        {
            Deliver(session, new InventoryChangedMessage
            {
                Added = offlineReport.Items.Select(item => new ItemStackDto { ItemId = item.ItemId, Quantity = item.Quantity }).ToArray(),
                Gold = offlineReport.Gold,
            });
        }
    }

    private void EndSession(long connectionId)
    {
        if (!_sessions.Remove(connectionId, out Session? session))
        {
            return;
        }
        session.IsClosed = true;
        if (session.Simulation != null)
        {
            session.Simulation.Disconnect(session.EntityId);
        }
        if (session.Ticket != null && _sessionsByCharacter.GetValueOrDefault(session.Ticket.CharacterId) == session)
        {
            _sessionsByCharacter.Remove(session.Ticket.CharacterId);
        }
    }

    private void Kick(Session session, string code, string text)
    {
        Deliver(session, new ErrorMessage { Code = code, Text = text });
        session.Connection.Close();
        EndSession(session.Connection.Id);
    }

    private void FlushOutbox(ZoneSimulation simulation)
    {
        IReadOnlyList<(long PlayerEntityId, IServerMessage Message)> outbox = simulation.DrainOutbox();
        if (outbox.Count == 0)
        {
            return;
        }
        Dictionary<long, Session> recipients = _sessions.Values
            .Where(session => session.Simulation == simulation)
            .ToDictionary(session => session.EntityId);
        foreach ((long entityId, IServerMessage message) in outbox)
        {
            if (recipients.TryGetValue(entityId, out Session? session))
            {
                Deliver(session, message);
            }
        }
    }

    private static void Deliver(Session session, IServerMessage message)
    {
        Delivery delivery = message is WorldSnapshotMessage ? Delivery.Latest : Delivery.Reliable;
        session.Connection.Send(ProtocolCodec.Encode(message), delivery);
    }

    private ZoneSimulation? SimulationFor(ZoneTicket ticket)
    {
        if (ticket.InstanceId is not Guid runId)
        {
            return _zones.GetValueOrDefault(ticket.ZoneId);
        }
        if (_instances.TryGetValue(runId, out HostedInstance? hosted))
        {
            return hosted.Simulation;
        }
        if (!_catalog.Zones.TryGetValue(ticket.ZoneId, out ZoneDefinition? zone) || zone.Kind != ZoneKind.Instance)
        {
            return null;
        }
        hosted = new HostedInstance(CreateSimulation(zone), _clock.Elapsed);
        _instances[runId] = hosted;
        _logger.LogInformation("Started instance run {Run} of {Zone}", runId, zone.Id);
        return hosted.Simulation;
    }

    private ZoneSimulation CreateSimulation(ZoneDefinition zone) =>
        new(zone, _catalog, new SeededRandom(SeededRandom.CombineSeed(zone.Id, Environment.TickCount64)), DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private IEnumerable<ZoneDefinition> HostedZones()
    {
        if (_lifecycle.IsDedicatedInstanceServer)
        {
            return Enumerable.Empty<ZoneDefinition>();
        }
        IEnumerable<ZoneDefinition> openWorld = _catalog.Zones.Values.Where(zone => zone.Kind != ZoneKind.Instance);
        return _options.ZoneIds.Count == 0 ? openWorld : openWorld.Where(zone => _options.ZoneIds.Contains(zone.Id));
    }

    private IEnumerable<ZoneSimulation> AllSimulations() =>
        _zones.Values.Concat(_instances.Values.Select(hosted => hosted.Simulation)).ToList();

    private void DropSilentConnections()
    {
        TimeSpan deadline = _clock.Elapsed - _options.HelloTimeout;
        foreach (Session session in _sessions.Values.Where(session => session.Ticket == null && session.ConnectedAt < deadline).ToList())
        {
            Kick(session, "auth.timeout", "Нет приветствия от клиента.");
        }
    }

    private void RetireEmptyInstances()
    {
        foreach ((Guid runId, HostedInstance hosted) in _instances.ToList())
        {
            bool occupied = hosted.Simulation.Players.Any() || _sessions.Values.Any(session => session.Simulation == hosted.Simulation);
            if (occupied)
            {
                hosted.LastOccupiedAt = _clock.Elapsed;
                continue;
            }
            if (_clock.Elapsed - hosted.LastOccupiedAt > _options.EmptyInstanceLifetime)
            {
                _instances.Remove(runId);
                _logger.LogInformation("Instance run {Run} closed", runId);
                if (_lifecycle.IsDedicatedInstanceServer && _instances.Count == 0)
                {
                    _ = SafeAsync(() => _lifecycle.RequestShutdown(CancellationToken.None), "agones shutdown");
                }
            }
        }
    }

    private void PeriodicHousekeeping()
    {
        TimeSpan now = _clock.Elapsed;
        if (now - _lastHeartbeat >= _options.HeartbeatInterval)
        {
            _lastHeartbeat = now;
            var info = new ZoneServerInfo(_options.ServerId, _zones.Keys.ToList(), _options.PublicHost, _options.UdpPort, _options.WebSocketPort, _sessionsByCharacter.Count, null);
            _ = SafeAsync(() => _grains.GetGrain<IZoneDirectoryGrain>(GrainKeys.Global).Heartbeat(info), "heartbeat");
        }
        if (now - _lastMembershipRefresh >= _options.MembershipRefreshInterval)
        {
            _lastMembershipRefresh = now;
            foreach (Guid characterId in _sessionsByCharacter.Keys.ToList())
            {
                _ = SafeAsync(async () =>
                {
                    CharacterState state = await _grains.GetGrain<ICharacterGrain>(characterId).GetState();
                    Post(() => _sessionsByCharacter.GetValueOrDefault(characterId)?.Simulation?.UpdateMembership(characterId, state.PartyId, state.GuildId));
                }, "membership refresh");
            }
        }
    }

    private async Task RelayChat(CancellationToken cancellation)
    {
        try
        {
            await foreach ((string subject, ChatEnvelope envelope) in _bus.SubscribeAsync<ChatEnvelope>(ChatSubjectPattern, cancellation))
            {
                (ChatScope scope, Guid? groupId) = ParseChatSubject(subject);
                var message = new ChatMessage { Channel = envelope.Channel, SenderName = envelope.SenderName, Text = envelope.Text, ServerTime = envelope.ServerTime };
                Post(() =>
                {
                    foreach (ZoneSimulation simulation in AllSimulations())
                    {
                        simulation.DeliverChat(scope, groupId, message);
                    }
                    foreach (ZoneSimulation simulation in AllSimulations())
                    {
                        FlushOutbox(simulation);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public static (ChatScope Scope, Guid? GroupId) ParseChatSubject(string subject)
    {
        if (subject.StartsWith(PartySubjectPrefix, StringComparison.Ordinal) && Guid.TryParse(subject[PartySubjectPrefix.Length..], out Guid party))
        {
            return (ChatScope.Party, party);
        }
        if (subject.StartsWith(GuildSubjectPrefix, StringComparison.Ordinal) && Guid.TryParse(subject[GuildSubjectPrefix.Length..], out Guid guild))
        {
            return (ChatScope.Guild, guild);
        }
        return (subject == TradeSubject ? ChatScope.Trade : ChatScope.World, null);
    }

    private void SaveEveryone()
    {
        foreach (Session session in _sessions.Values.ToList())
        {
            EndSession(session.Connection.Id);
        }
        foreach (ZoneSimulation simulation in AllSimulations())
        {
            foreach (SavePosition save in simulation.DrainEffects().OfType<SavePosition>())
            {
                _grains.GetGrain<ICharacterGrain>(save.CharacterId).SavePosition(save.ZoneId, save.X, save.Z, save.Hp).Wait(TimeSpan.FromSeconds(5));
            }
        }
    }

    private async Task SafeAsync(Func<Task> work, string what)
    {
        try
        {
            await work();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Zone {What} failed", what);
        }
    }
}
