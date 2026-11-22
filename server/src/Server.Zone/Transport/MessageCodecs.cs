using System.Reflection;
using System.Text;
using System.Text.Json;
using MemoryPack;
using Realmforge.Protocol;

namespace Realmforge.Server.Zone.Transport;

public interface IMessageCodec
{
    bool IsText { get; }
    IClientMessage? Decode(byte[] payload);
    byte[] Encode(IServerMessage message);
}

public sealed class BinaryMessageCodec : IMessageCodec
{
    public static readonly BinaryMessageCodec Instance = new();

    public bool IsText => false;

    public IClientMessage? Decode(byte[] payload) => ProtocolCodec.DecodeClient(payload);

    public byte[] Encode(IServerMessage message) => ProtocolCodec.Encode(message);
}

/// <summary>
/// Browser clients speak JSON envelopes <c>{"type":"MoveToRequest","data":{...}}</c>. Type names come from the
/// MemoryPack unions, so both encodings always cover exactly the same message set.
/// </summary>
public sealed class JsonMessageCodec : IMessageCodec
{
    public const string FormatQueryValue = "json";
    private const string TypeProperty = "type";
    private const string DataProperty = "data";

    public static readonly JsonMessageCodec Instance = new();

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly Dictionary<string, Type> ClientTypes = UnionMembers(typeof(IClientMessage));

    public bool IsText => true;

    public IClientMessage? Decode(byte[] payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        string? typeName = root.GetProperty(TypeProperty).GetString();
        if (typeName == null || !ClientTypes.TryGetValue(typeName, out Type? type))
        {
            throw new JsonException($"Unknown message type '{typeName}'.");
        }
        return root.TryGetProperty(DataProperty, out JsonElement data)
            ? (IClientMessage?)data.Deserialize(type, Options)
            : (IClientMessage?)Activator.CreateInstance(type);
    }

    public byte[] Encode(IServerMessage message)
    {
        var envelope = new Dictionary<string, object>
        {
            [TypeProperty] = message.GetType().Name,
            [DataProperty] = message,
        };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, Options));
    }

    private static Dictionary<string, Type> UnionMembers(Type unionInterface)
    {
        return unionInterface.GetCustomAttributes<MemoryPackUnionAttribute>()
            .ToDictionary(union => union.Type.Name, union => union.Type, StringComparer.Ordinal);
    }
}
