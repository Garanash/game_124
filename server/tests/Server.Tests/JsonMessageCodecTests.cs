using System.Text;
using System.Text.Json;
using Realmforge.Protocol;
using Realmforge.Server.Zone.Transport;
using Xunit;

namespace Realmforge.Server.Tests;

public class JsonMessageCodecTests
{
    [Fact]
    public void Decodes_client_envelope_into_the_named_message()
    {
        byte[] payload = Encoding.UTF8.GetBytes("""{"type":"MoveToRequest","data":{"x":12.5,"z":40}}""");

        var move = Assert.IsType<MoveToRequest>(JsonMessageCodec.Instance.Decode(payload));

        Assert.Equal(12.5f, move.X);
        Assert.Equal(40f, move.Z);
    }

    [Fact]
    public void Envelope_without_data_creates_an_empty_message()
    {
        byte[] payload = Encoding.UTF8.GetBytes("""{"type":"PingRequest"}""");

        Assert.IsType<PingRequest>(JsonMessageCodec.Instance.Decode(payload));
    }

    [Fact]
    public void Unknown_type_is_rejected()
    {
        byte[] payload = Encoding.UTF8.GetBytes("""{"type":"WelcomeMessage","data":{}}""");

        Assert.ThrowsAny<JsonException>(() => JsonMessageCodec.Instance.Decode(payload));
    }

    [Fact]
    public void Encodes_server_message_with_type_name_and_camel_case_fields()
    {
        var snapshot = new WorldSnapshotMessage { Tick = 7, Entities = new[] { new EntityState { EntityId = 3, X = 1, Z = 2 } } };

        using JsonDocument document = JsonDocument.Parse(JsonMessageCodec.Instance.Encode(snapshot));

        Assert.Equal(nameof(WorldSnapshotMessage), document.RootElement.GetProperty("type").GetString());
        JsonElement data = document.RootElement.GetProperty("data");
        Assert.Equal(7, data.GetProperty("tick").GetInt64());
        Assert.Equal(3, data.GetProperty("entities")[0].GetProperty("entityId").GetInt64());
    }
}
