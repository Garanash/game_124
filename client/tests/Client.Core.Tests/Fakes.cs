using Realmforge.Client.Core.Api;
using Realmforge.Client.Core.Net;
using Realmforge.Content;
using Realmforge.Rules.Definitions;

namespace Realmforge.Client.Core.Tests;

internal static class ShippedContent
{
    private static readonly Lazy<ContentCatalog> Catalog = new(() => ContentLoader.LoadDirectory(FindContentDirectory()));

    public static ContentCatalog Value => Catalog.Value;

    private static string FindContentDirectory()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Realmforge.sln")))
        {
            directory = directory.Parent;
        }
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Realmforge.sln not found."), "content");
    }
}

internal sealed class FakeHttp : IHttpTransport
{
    private readonly Queue<HttpResponse> _responses = new();

    public List<(string Method, string Url, string? Body, string? Token)> Requests { get; } = new();

    public FakeHttp Reply(int status, string body)
    {
        _responses.Enqueue(new HttpResponse(status, body));
        return this;
    }

    public Task<HttpResponse> Send(string method, string url, string? jsonBody, string? bearerToken)
    {
        Requests.Add((method, url, jsonBody, bearerToken));
        return Task.FromResult(_responses.Dequeue());
    }
}

internal sealed class FakeTransport : IGameTransport
{
    public List<byte[]> Sent { get; } = new();
    public (string Host, int UdpPort, int WebSocketPort)? LastConnect { get; private set; }
    public bool IsConnected { get; private set; }

    public event Action? Connected;
    public event Action<byte[]>? Received;
    public event Action<string>? Disconnected;

    public void Connect(string host, int udpPort, int webSocketPort)
    {
        LastConnect = (host, udpPort, webSocketPort);
        IsConnected = true;
        Connected?.Invoke();
    }

    public void Send(byte[] payload, bool reliable) => Sent.Add(payload);

    public void Poll()
    {
    }

    public void Disconnect() => IsConnected = false;

    public void Deliver(byte[] payload) => Received?.Invoke(payload);

    public void Drop(string reason)
    {
        IsConnected = false;
        Disconnected?.Invoke(reason);
    }
}
