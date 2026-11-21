using System.Text.RegularExpressions;

namespace Realmforge.Server.Core.Social;

public enum GuildRank
{
    Recruit,
    Member,
    Officer,
    Master,
}

public sealed partial class GuildState
{
    public const int MaxMembers = 200;
    public const int MinNameLength = 3;
    public const int MaxNameLength = 24;

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public Dictionary<Guid, GuildRank> Members { get; set; } = new();
    public HashSet<Guid> PendingInvites { get; set; } = new();
    public string MessageOfTheDay { get; set; } = string.Empty;

    [GeneratedRegex(@"^[\p{L}\d' -]+$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[\p{L}\d]{2,5}$")]
    private static partial Regex TagPattern();

    public static DomainResult ValidateIdentity(string name, string tag)
    {
        string trimmed = name.Trim();
        if (trimmed.Length < MinNameLength || trimmed.Length > MaxNameLength || !NamePattern().IsMatch(trimmed))
        {
            return DomainResult.Fail("guild.name", "Guild name must be 3-24 letters, digits, spaces or apostrophes.");
        }
        return TagPattern().IsMatch(tag) ? DomainResult.Ok : DomainResult.Fail("guild.tag", "Tag must be 2-5 letters or digits.");
    }

    public static GuildState Found(Guid guildId, string name, string tag, Guid founderId)
    {
        return new GuildState { Id = guildId, Name = name.Trim(), Tag = tag.ToUpperInvariant(), Members = { [founderId] = GuildRank.Master } };
    }

    public DomainResult Invite(Guid inviterId, Guid inviteeId)
    {
        if (!HasRank(inviterId, GuildRank.Officer))
        {
            return DomainResult.Fail("guild.rank", "Officers and above may invite.");
        }
        if (Members.ContainsKey(inviteeId))
        {
            return DomainResult.Fail("guild.already_member", "Already a member.");
        }
        if (Members.Count >= MaxMembers)
        {
            return DomainResult.Fail("guild.full", "The guild is full.");
        }
        PendingInvites.Add(inviteeId);
        return DomainResult.Ok;
    }

    public DomainResult Accept(Guid inviteeId)
    {
        if (!PendingInvites.Remove(inviteeId))
        {
            return DomainResult.Fail("guild.no_invite", "No pending invite.");
        }
        Members[inviteeId] = GuildRank.Recruit;
        return DomainResult.Ok;
    }

    public DomainResult SetRank(Guid actorId, Guid memberId, GuildRank rank)
    {
        if (!Members.ContainsKey(memberId))
        {
            return DomainResult.Fail("guild.not_member", "Not a member.");
        }
        if (!HasRank(actorId, GuildRank.Master) && (rank >= GuildRank.Officer || !HasRank(actorId, GuildRank.Officer)))
        {
            return DomainResult.Fail("guild.rank", "Not allowed to assign this rank.");
        }
        if (rank == GuildRank.Master)
        {
            Members[actorId] = GuildRank.Officer;
        }
        Members[memberId] = rank;
        return DomainResult.Ok;
    }

    /// <summary>When the master leaves, the highest-ranked remaining member inherits the guild.</summary>
    public DomainResult Leave(Guid memberId)
    {
        if (!Members.Remove(memberId, out GuildRank rank))
        {
            return DomainResult.Fail("guild.not_member", "Not a member.");
        }
        if (rank == GuildRank.Master && Members.Count > 0)
        {
            Guid heir = Members.OrderByDescending(pair => pair.Value).First().Key;
            Members[heir] = GuildRank.Master;
        }
        return DomainResult.Ok;
    }

    public bool HasRank(Guid memberId, GuildRank minimum)
    {
        return Members.TryGetValue(memberId, out GuildRank rank) && rank >= minimum;
    }
}
