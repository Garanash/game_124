using System;
using MemoryPack;

namespace Realmforge.Protocol
{
    public static class ProtocolCodec
    {
        /// <summary>Bumped on any breaking wire change; the zone rejects mismatched clients in Hello.</summary>
        public const int ProtocolVersion = 1;

        public static byte[] Encode(IClientMessage message)
        {
            return MemoryPackSerializer.Serialize(message);
        }

        public static byte[] Encode(IServerMessage message)
        {
            return MemoryPackSerializer.Serialize(message);
        }

        public static IClientMessage? DecodeClient(ReadOnlySpan<byte> payload)
        {
            return MemoryPackSerializer.Deserialize<IClientMessage>(payload);
        }

        public static IServerMessage? DecodeServer(ReadOnlySpan<byte> payload)
        {
            return MemoryPackSerializer.Deserialize<IServerMessage>(payload);
        }
    }
}
