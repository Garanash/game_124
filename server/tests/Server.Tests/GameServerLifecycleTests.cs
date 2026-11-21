using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Realmforge.Server.Zone;
using Xunit;

namespace Realmforge.Server.Tests;

public sealed class GameServerLifecycleTests
{
    private const int SdkPort = 9358;

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task AgonesLifecycleCallsTheLocalSdkSidecar()
    {
        var handler = new RecordingHandler();
        var lifecycle = new AgonesSdkLifecycle(new HttpClient(handler), SdkPort, NullLogger<AgonesSdkLifecycle>.Instance);

        await lifecycle.MarkReady(CancellationToken.None);
        await lifecycle.ReportHealth(CancellationToken.None);
        await lifecycle.RequestShutdown(CancellationToken.None);

        Assert.True(lifecycle.IsDedicatedInstanceServer);
        Assert.Equal(
            new[] { "POST http://localhost:9358/ready", "POST http://localhost:9358/health", "POST http://localhost:9358/shutdown" },
            handler.Requests);
    }

    [Fact]
    public void StandaloneServersHostTheOpenWorld()
    {
        Assert.False(new StandaloneLifecycle().IsDedicatedInstanceServer);
    }
}
