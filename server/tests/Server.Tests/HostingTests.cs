using Microsoft.Extensions.Logging.Abstractions;
using Realmforge.Server.Core.Social;
using Realmforge.Server.Gateway.Accounts;
using Realmforge.Server.Hosting;
using Realmforge.Server.Zone;
using Xunit;

namespace Realmforge.Server.Tests;

public sealed class HostingTests
{
    [Fact]
    public void ChatSubjectsRouteToTheRightAudience()
    {
        Guid party = Guid.NewGuid();

        Assert.Equal((ChatScope.Party, party), ZoneRuntime.ParseChatSubject($"chat.party.{party:N}"));
        Assert.Equal((ChatScope.Guild, party), ZoneRuntime.ParseChatSubject($"chat.guild.{party:N}"));
        Assert.Equal((ChatScope.Trade, (Guid?)null), ZoneRuntime.ParseChatSubject("chat.trade"));
        Assert.Equal((ChatScope.World, (Guid?)null), ZoneRuntime.ParseChatSubject("chat.world"));
    }

    [Theory]
    [InlineData("chat.>", "chat.party.abc", true)]
    [InlineData("chat.>", "analytics.login", false)]
    [InlineData("chat.world", "chat.world", true)]
    [InlineData("chat.world", "chat.trade", false)]
    public void InProcessBusMatchesNatsTailWildcard(string pattern, string subject, bool expected)
    {
        Assert.Equal(expected, InProcessMessageBus.Matches(pattern, subject));
    }

    [Fact]
    public async Task InProcessBusDeliversToSubscribers()
    {
        var bus = new InProcessMessageBus(NullLogger<InProcessMessageBus>.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        IAsyncEnumerator<(string Subject, string Payload)> subscription = bus.SubscribeAsync<string>("chat.>", cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        Task<bool> next = subscription.MoveNextAsync().AsTask();
        await Task.Delay(50);

        await bus.PublishAsync("chat.world", "привет");

        Assert.True(await next);
        Assert.Equal(("chat.world", "привет"), subscription.Current);
    }

    [Theory]
    [InlineData("  Hero@Mail.COM ", "hero@mail.com")]
    [InlineData("no-at-sign", null)]
    [InlineData("@nouser.com", null)]
    [InlineData("user@", null)]
    public void EmailsAreNormalizedBeforeLookup(string input, string? expected)
    {
        Assert.Equal(expected, AccountEmail.Normalize(input));
    }
}
