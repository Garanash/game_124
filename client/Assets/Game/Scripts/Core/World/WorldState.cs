using System;
using System.Collections.Generic;
using Realmforge.Protocol;

namespace Realmforge.Client.Core.World
{
    public sealed class EntityView
    {
        public EntityView(EntitySpawnMessage spawn)
        {
            Id = spawn.EntityId;
            Kind = spawn.Kind;
            Name = spawn.Name;
            DefinitionId = spawn.DefinitionId;
            Cosmetics = spawn.Cosmetics;
            Level = spawn.Level;
            Hp = spawn.Hp;
            MaxHp = spawn.MaxHp;
        }

        public long Id { get; }
        public EntityKind Kind { get; }
        public string Name { get; }
        public string DefinitionId { get; }
        public string[] Cosmetics { get; }
        public int Level { get; }
        public int Hp { get; set; }
        public int MaxHp { get; }
        public byte Flags { get; set; }
        public SnapshotBuffer Poses { get; } = new SnapshotBuffer();

        public bool InCombat => (Flags & EntityStateFlags.InCombat) != 0;
        public bool IsDead => (Flags & EntityStateFlags.Dead) != 0;
        public bool IsGathering => (Flags & EntityStateFlags.Gathering) != 0;
    }

    public sealed class QuestEntry
    {
        public string QuestId { get; set; } = string.Empty;
        public string StepId { get; set; } = string.Empty;
        public int Count { get; set; }
        public bool Completed { get; set; }
    }

    public sealed class ChatLine
    {
        public ChatLine(string channel, string sender, string text)
        {
            Channel = channel;
            Sender = sender;
            Text = text;
        }

        public string Channel { get; }
        public string Sender { get; }
        public string Text { get; }
    }

    /// <summary>
    /// Client-side mirror of what the zone server tells us. Pure data plus change events; views in Unity
    /// subscribe to the events and read positions through <see cref="PoseOf"/> every frame.
    /// </summary>
    public sealed class WorldState
    {
        public const int MaxChatLines = 100;

        private readonly Dictionary<long, EntityView> _entities = new Dictionary<long, EntityView>();
        private readonly Dictionary<string, QuestEntry> _quests = new Dictionary<string, QuestEntry>();
        private readonly List<ChatLine> _chat = new List<ChatLine>();

        public ServerClock Clock { get; } = new ServerClock();
        public long LocalEntityId { get; private set; }
        public string ZoneId { get; private set; } = string.Empty;
        public string SceneAddress { get; private set; } = string.Empty;
        public CombatState? Combat { get; private set; }
        public DialogueMessage? Dialogue { get; private set; }
        public IReadOnlyCollection<EntityView> Entities => _entities.Values;
        public IReadOnlyCollection<QuestEntry> Quests => _quests.Values;
        public IReadOnlyList<ChatLine> Chat => _chat;
        public EntityView? LocalPlayer => Find(LocalEntityId);

        public event Action<WelcomeMessage>? ZoneEntered;
        public event Action<EntityView>? EntitySpawned;
        public event Action<long>? EntityDespawned;
        public event Action<CombatState>? EncounterStarted;
        /// <summary>A party member started a fight nearby; the UI offers a "join" button that sends JoinEncounterRequest.</summary>
        public event Action<EncounterStartedMessage>? EncounterJoinOffered;
        public event Action<CombatState>? CombatUpdated;
        public event Action<EncounterEndedMessage>? EncounterEnded;
        public event Action<ChatLine>? ChatReceived;
        public event Action<QuestEntry>? QuestUpdated;
        public event Action<DialogueMessage>? DialogueOpened;
        public event Action<InventoryChangedMessage>? InventoryChanged;
        public event Action<ErrorMessage>? ServerError;
        public event Action<ZoneTransferMessage>? ZoneTransferRequested;

        public EntityView? Find(long entityId) => _entities.TryGetValue(entityId, out EntityView? view) ? view : null;

        public EntityPose PoseOf(EntityView entity) => entity.Poses.Sample(Clock.RenderTime);

        public string NameOf(long entityId) => Find(entityId)?.Name ?? "#" + entityId;

        public void Advance(double deltaSeconds) => Clock.Advance(deltaSeconds);

        public void CloseDialogue() => Dialogue = null;

        public void Apply(IServerMessage message)
        {
            switch (message)
            {
                case WelcomeMessage welcome:
                    EnterZone(welcome);
                    break;
                case EntitySpawnMessage spawn:
                    Spawn(spawn);
                    break;
                case EntityDespawnMessage despawn:
                    if (_entities.Remove(despawn.EntityId))
                    {
                        EntityDespawned?.Invoke(despawn.EntityId);
                    }
                    break;
                case WorldSnapshotMessage snapshot:
                    ApplySnapshot(snapshot);
                    break;
                case EncounterStartedMessage started:
                    StartEncounter(started);
                    break;
                case CombatEventsMessage events when Combat != null && Combat.EncounterId == events.EncounterId:
                    Combat.Apply(events);
                    CombatUpdated?.Invoke(Combat);
                    break;
                case EncounterEndedMessage ended:
                    EndEncounter(ended);
                    break;
                case ChatMessage chat:
                    AddChat(new ChatLine(chat.Channel, chat.SenderName, chat.Text));
                    break;
                case QuestUpdateMessage quest:
                    UpdateQuest(quest);
                    break;
                case DialogueMessage dialogue:
                    Dialogue = dialogue;
                    DialogueOpened?.Invoke(dialogue);
                    break;
                case InventoryChangedMessage inventory:
                    InventoryChanged?.Invoke(inventory);
                    break;
                case ErrorMessage error:
                    ServerError?.Invoke(error);
                    break;
                case ZoneTransferMessage transfer:
                    ZoneTransferRequested?.Invoke(transfer);
                    break;
                case PongMessage pong:
                    Clock.Observe(pong.ServerTime);
                    break;
            }
        }

        private void EnterZone(WelcomeMessage welcome)
        {
            foreach (long entityId in new List<long>(_entities.Keys))
            {
                EntityDespawned?.Invoke(entityId);
            }
            _entities.Clear();
            Combat = null;
            Dialogue = null;
            LocalEntityId = welcome.EntityId;
            ZoneId = welcome.ZoneId;
            SceneAddress = welcome.SceneAddress;
            Clock.Observe(welcome.ServerTime);
            ZoneEntered?.Invoke(welcome);
        }

        private void Spawn(EntitySpawnMessage spawn)
        {
            var view = new EntityView(spawn);
            view.Poses.Add(Clock.ServerTime - ServerClock.InterpolationDelaySeconds, new EntityPose(spawn.X, spawn.Z, 0));
            _entities[spawn.EntityId] = view;
            EntitySpawned?.Invoke(view);
        }

        private void ApplySnapshot(WorldSnapshotMessage snapshot)
        {
            Clock.Observe(snapshot.ServerTime);
            foreach (EntityState state in snapshot.Entities)
            {
                if (_entities.TryGetValue(state.EntityId, out EntityView? view))
                {
                    view.Flags = state.Flags;
                    view.Poses.Add(snapshot.ServerTime, new EntityPose(state.X, state.Z, state.Heading));
                }
            }
        }

        private void StartEncounter(EncounterStartedMessage started)
        {
            bool takingPart = Array.Exists(started.Combatants, combatant => combatant.EntityId == LocalEntityId);
            if (!takingPart)
            {
                EncounterJoinOffered?.Invoke(started);
                return;
            }
            Combat = new CombatState(started, NameOf);
            EncounterStarted?.Invoke(Combat);
        }

        private void EndEncounter(EncounterEndedMessage ended)
        {
            if (Combat == null || Combat.EncounterId != ended.EncounterId)
            {
                return;
            }
            Combat = null;
            EncounterEnded?.Invoke(ended);
        }

        private void AddChat(ChatLine line)
        {
            if (_chat.Count == MaxChatLines)
            {
                _chat.RemoveAt(0);
            }
            _chat.Add(line);
            ChatReceived?.Invoke(line);
        }

        private void UpdateQuest(QuestUpdateMessage update)
        {
            if (!_quests.TryGetValue(update.QuestId, out QuestEntry? entry))
            {
                entry = new QuestEntry { QuestId = update.QuestId };
                _quests[update.QuestId] = entry;
            }
            entry.StepId = update.StepId;
            entry.Count = update.Count;
            entry.Completed = update.Completed;
            QuestUpdated?.Invoke(entry);
        }
    }
}
