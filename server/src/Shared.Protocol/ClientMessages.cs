using MemoryPack;

namespace Realmforge.Protocol
{
    [MemoryPackable]
    [MemoryPackUnion(0, typeof(HelloRequest))]
    [MemoryPackUnion(1, typeof(MoveToRequest))]
    [MemoryPackUnion(2, typeof(CombatCommandRequest))]
    [MemoryPackUnion(3, typeof(ChatSendRequest))]
    [MemoryPackUnion(4, typeof(InteractRequest))]
    [MemoryPackUnion(5, typeof(QuestChoiceRequest))]
    [MemoryPackUnion(6, typeof(JoinEncounterRequest))]
    [MemoryPackUnion(7, typeof(UsePortalRequest))]
    [MemoryPackUnion(8, typeof(PingRequest))]
    public partial interface IClientMessage
    {
    }

    /// <summary>First message on a zone connection; the ticket is a gateway-issued JWT naming the character.</summary>
    [MemoryPackable]
    public partial class HelloRequest : IClientMessage
    {
        public string Ticket { get; set; } = string.Empty;
        public int ProtocolVersion { get; set; }
    }

    [MemoryPackable]
    public partial class MoveToRequest : IClientMessage
    {
        public float X { get; set; }
        public float Z { get; set; }
    }

    [MemoryPackable]
    public partial class CombatCommandRequest : IClientMessage
    {
        public byte CommandType { get; set; }
        public int CellX { get; set; }
        public int CellY { get; set; }
        public long TargetId { get; set; }
        public string AbilityId { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public partial class ChatSendRequest : IClientMessage
    {
        public string Channel { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>Talk to an NPC, gather a node, or open a door: the server decides by the target kind.</summary>
    [MemoryPackable]
    public partial class InteractRequest : IClientMessage
    {
        public long TargetEntityId { get; set; }
        public string OptionId { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public partial class QuestChoiceRequest : IClientMessage
    {
        public string QuestId { get; set; } = string.Empty;
        public string ChoiceId { get; set; } = string.Empty;
    }

    [MemoryPackable]
    public partial class JoinEncounterRequest : IClientMessage
    {
        public System.Guid EncounterId { get; set; }
    }

    [MemoryPackable]
    public partial class UsePortalRequest : IClientMessage
    {
        public int PortalIndex { get; set; }
    }

    [MemoryPackable]
    public partial class PingRequest : IClientMessage
    {
        public double ClientTime { get; set; }
    }
}
