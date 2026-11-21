using System.Net.Http.Headers;
using System.Net.Http.Json;
using LiteNetLib;
using Realmforge.Protocol;

// Plays the same path as a real client: account, character, zone ticket, UDP session, walking around.
// Usage: dotnet run --project tools/BotClient -- [gatewayUrl] [seconds]
const string DefaultGateway = "http://127.0.0.1:5080";
const int DefaultSeconds = 6;
const string ConnectionKey = "realmforge";
const int PollMilliseconds = 15;
const float WalkDistanceMeters = 10f;
int[] pointBuyAbilities = { 15, 14, 13, 12, 10, 8 };

string gateway = args.Length > 0 ? args[0] : DefaultGateway;
int seconds = args.Length > 1 ? int.Parse(args[1]) : DefaultSeconds;
using var http = new HttpClient { BaseAddress = new Uri(gateway) };

string suffix = Guid.NewGuid().ToString("N")[..8];
var credentials = new { Email = $"bot-{suffix}@realmforge.local", Password = "bot-password-1" };
Session session = await Post<Session>("/auth/register", credentials);
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);

var characterRequest = new { Name = "Бот" + new string(suffix.Where(char.IsLetter).Take(4).ToArray()), RaceId = "dwarf", Alignment = "LawfulGood", Abilities = pointBuyAbilities };
CharacterSummary character = await Post<CharacterSummary>("/characters", characterRequest);
Console.WriteLine($"Created {character.Name} ({character.RaceId}) in {character.ZoneId}");

EnterWorld enter = await Post<EnterWorld>($"/characters/{character.CharacterId}/enter", new { });
Console.WriteLine($"Zone ticket for {enter.ZoneId} at {enter.Host}:{enter.UdpPort}");

var stats = new Dictionary<string, int>();
long myEntityId = 0;
float myX = 0, myZ = 0;
var listener = new EventBasedNetListener();
var client = new NetManager(listener) { AutoRecycle = true, ChannelsCount = 2 };
listener.PeerConnectedEvent += peer =>
    peer.Send(ProtocolCodec.Encode(new HelloRequest { Ticket = enter.Ticket, ProtocolVersion = ProtocolCodec.ProtocolVersion }), DeliveryMethod.ReliableOrdered);
listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
{
    IServerMessage? message = ProtocolCodec.DecodeServer(reader.GetRemainingBytes());
    if (message == null)
    {
        return;
    }
    string kind = message.GetType().Name;
    stats[kind] = stats.GetValueOrDefault(kind) + 1;
    switch (message)
    {
        case WelcomeMessage welcome:
            myEntityId = welcome.EntityId;
            Console.WriteLine($"Welcome: entity {welcome.EntityId}, scene {welcome.SceneAddress}, content {welcome.ContentVersion}");
            break;
        case WorldSnapshotMessage snapshot:
            foreach (EntityState state in snapshot.Entities.Where(entity => entity.EntityId == myEntityId))
            {
                myX = state.X;
                myZ = state.Z;
            }
            break;
        case ErrorMessage error:
            Console.WriteLine($"Server error {error.Code}: {error.Text}");
            break;
    }
};
client.Start();
NetPeer server = client.Connect(enter.Host, enter.UdpPort, ConnectionKey);

DateTime walkAt = DateTime.UtcNow.AddSeconds(2);
DateTime stopAt = DateTime.UtcNow.AddSeconds(seconds);
bool walked = false;
float startX = 0;
while (DateTime.UtcNow < stopAt)
{
    client.PollEvents();
    if (!walked && myEntityId != 0 && DateTime.UtcNow >= walkAt)
    {
        walked = true;
        startX = myX;
        server.Send(ProtocolCodec.Encode(new MoveToRequest { X = myX + WalkDistanceMeters, Z = myZ }), DeliveryMethod.ReliableOrdered);
        server.Send(ProtocolCodec.Encode(new ChatSendRequest { Channel = "say", Text = "Привет, мир!" }), DeliveryMethod.ReliableOrdered);
    }
    Thread.Sleep(PollMilliseconds);
}
client.Stop();

Console.WriteLine("Received: " + string.Join(", ", stats.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")));
Console.WriteLine($"Walked {myX - startX:F1} m along X");
bool healthy = myEntityId != 0 && stats.ContainsKey(nameof(WorldSnapshotMessage)) && myX - startX > 1;
Console.WriteLine(healthy ? "BOT OK" : "BOT FAILED");
return healthy ? 0 : 1;

async Task<T> Post<T>(string path, object body)
{
    HttpResponseMessage response = await http.PostAsJsonAsync(path, body);
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"{path} -> {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
    return (await response.Content.ReadFromJsonAsync<T>())!;
}

internal sealed record Session(Guid AccountId, string Token);

internal sealed record CharacterSummary(Guid CharacterId, string Name, string RaceId, int Level, string ZoneId);

internal sealed record EnterWorld(string ZoneId, string Host, int UdpPort, int WebSocketPort, string Ticket);
