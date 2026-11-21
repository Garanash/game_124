namespace Realmforge.Server.Core.Social;

/// <summary>A party of up to 5 that can be converted into a raid of up to 25 for raid instances.</summary>
public sealed class PartyState
{
    public const int MaxPartySize = 5;
    public const int MaxRaidSize = 25;

    public Guid Id { get; set; }
    public Guid LeaderId { get; set; }
    public List<Guid> Members { get; set; } = new();
    public HashSet<Guid> PendingInvites { get; set; } = new();
    public bool IsRaid { get; set; }

    public int Capacity => IsRaid ? MaxRaidSize : MaxPartySize;
    public bool IsDisbanded => Members.Count == 0;

    public static PartyState Create(Guid partyId, Guid leaderId)
    {
        return new PartyState { Id = partyId, LeaderId = leaderId, Members = { leaderId } };
    }

    public DomainResult Invite(Guid inviterId, Guid inviteeId)
    {
        if (inviterId != LeaderId)
        {
            return DomainResult.Fail("party.not_leader", "Only the leader can invite.");
        }
        if (Members.Contains(inviteeId))
        {
            return DomainResult.Fail("party.already_member", "Already in the party.");
        }
        if (Members.Count + PendingInvites.Count >= Capacity)
        {
            return DomainResult.Fail("party.full", "The party is full.");
        }
        PendingInvites.Add(inviteeId);
        return DomainResult.Ok;
    }

    public DomainResult Accept(Guid inviteeId)
    {
        if (!PendingInvites.Remove(inviteeId))
        {
            return DomainResult.Fail("party.no_invite", "No pending invite.");
        }
        if (Members.Count >= Capacity)
        {
            return DomainResult.Fail("party.full", "The party is full.");
        }
        Members.Add(inviteeId);
        return DomainResult.Ok;
    }

    public void Decline(Guid inviteeId)
    {
        PendingInvites.Remove(inviteeId);
    }

    /// <summary>Leadership passes to the longest-standing member when the leader leaves.</summary>
    public DomainResult Leave(Guid memberId)
    {
        if (!Members.Remove(memberId))
        {
            return DomainResult.Fail("party.not_member", "Not in the party.");
        }
        if (memberId == LeaderId && Members.Count > 0)
        {
            LeaderId = Members[0];
        }
        if (Members.Count == 0)
        {
            PendingInvites.Clear();
        }
        return DomainResult.Ok;
    }

    public DomainResult Kick(Guid leaderId, Guid memberId)
    {
        if (leaderId != LeaderId)
        {
            return DomainResult.Fail("party.not_leader", "Only the leader can kick.");
        }
        return memberId == leaderId ? DomainResult.Fail("party.kick_self", "Use leave instead.") : Leave(memberId);
    }

    public DomainResult ConvertToRaid(Guid leaderId)
    {
        if (leaderId != LeaderId)
        {
            return DomainResult.Fail("party.not_leader", "Only the leader can form a raid.");
        }
        IsRaid = true;
        return DomainResult.Ok;
    }
}
