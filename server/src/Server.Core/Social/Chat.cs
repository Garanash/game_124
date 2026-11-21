namespace Realmforge.Server.Core.Social;

public enum ChatScope
{
    Say,
    Zone,
    Party,
    Guild,
    World,
    Trade,
}

/// <summary>Channel addresses look like "say", "zone", "party", "guild", "world", "trade".</summary>
public sealed record ChatChannel(ChatScope Scope)
{
    public const float SayRadiusMeters = 30f;

    public static bool TryParse(string text, out ChatChannel channel)
    {
        bool parsed = Enum.TryParse(text, ignoreCase: true, out ChatScope scope);
        channel = new ChatChannel(scope);
        return parsed && Enum.IsDefined(scope);
    }

    /// <summary>NATS subject used to fan a message out across zone servers; say/zone stay local.</summary>
    public string? Subject(string zoneId, Guid? partyId, Guid? guildId)
    {
        return Scope switch
        {
            ChatScope.Party when partyId.HasValue => $"chat.party.{partyId:N}",
            ChatScope.Guild when guildId.HasValue => $"chat.guild.{guildId:N}",
            ChatScope.World => "chat.world",
            ChatScope.Trade => "chat.trade",
            _ => null,
        };
    }
}

public static class ChatText
{
    public const int MaxLength = 256;

    public static string? Sanitize(string text)
    {
        string cleaned = new string(text.Where(symbol => !char.IsControl(symbol)).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            return null;
        }
        return cleaned.Length > MaxLength ? cleaned[..MaxLength] : cleaned;
    }
}

/// <summary>Token bucket per sender: bursts of a few lines are fine, sustained spam is dropped.</summary>
public sealed class ChatRateLimiter
{
    private const double Capacity = 5;
    private const double RefillPerSecond = 0.5;

    private readonly Dictionary<Guid, (double Tokens, DateTimeOffset Updated)> _buckets = new();
    private readonly TimeProvider _time;

    public ChatRateLimiter(TimeProvider time)
    {
        _time = time;
    }

    public bool TryConsume(Guid senderId)
    {
        DateTimeOffset now = _time.GetUtcNow();
        (double tokens, DateTimeOffset updated) = _buckets.TryGetValue(senderId, out var bucket) ? bucket : (Capacity, now);
        tokens = Math.Min(Capacity, tokens + (now - updated).TotalSeconds * RefillPerSecond);
        if (tokens < 1)
        {
            _buckets[senderId] = (tokens, now);
            return false;
        }
        _buckets[senderId] = (tokens - 1, now);
        return true;
    }
}
