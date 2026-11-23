using Realmforge.Rules.Characters;
using Realmforge.Rules.Cosmetics;
using Realmforge.Rules.Housing;
using Realmforge.Rules.Inventory;
using Realmforge.Rules.Professions;
using Realmforge.Rules.Quests;

namespace Realmforge.Server.Core.Characters;

/// <summary>Where matchmaking placed the character; the client exchanges it for a zone ticket.</summary>
public sealed record InstanceAssignment(Guid RunId, string InstanceId, string ZoneId, DateTimeOffset AssignedAt);

/// <summary>Everything persisted for one character; owned by its CharacterGrain.</summary>
public sealed class CharacterState
{
    public CharacterSheet Sheet { get; set; } = new();
    public Backpack Backpack { get; set; } = new();
    public Equipment Equipment { get; set; } = new();
    public StackInventory Materials { get; set; } = new();
    public List<QuestProgress> ActiveQuests { get; set; } = new();
    public AppearanceLoadout Appearance { get; set; } = new();
    public OfflineJobState? OfflineJob { get; set; }
    public HousePlot? House { get; set; }
    public Guid? PartyId { get; set; }
    public Guid? GuildId { get; set; }
    public InstanceAssignment? PendingInstance { get; set; }
    /// <summary>Result of the last offline job, auto-claimed while the player was away; shown on next login.</summary>
    public RewardOutcome? LastOfflineReport { get; set; }
    public string FactionId { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }

    public bool Exists => Sheet.Id != Guid.Empty && !IsDeleted;
}

public sealed record CharacterSummary(Guid CharacterId, string Name, string RaceId, int Level, string ZoneId, IReadOnlyList<string> Classes);

public sealed record DialogueOption(string Id, string Text);

public sealed record DialogueView(string NpcId, string NpcName, string Text, IReadOnlyList<DialogueOption> Options);

public sealed record QuestProgressView(string QuestId, string StepId, int Count, bool Completed);

public sealed record ItemGrant(string ItemId, int Quantity);

/// <summary>What changed after a reward so the zone can tell the client.</summary>
public sealed record RewardOutcome(long XpGranted, long Gold, IReadOnlyList<ItemGrant> Items, int NewLevel, bool LeveledUp, IReadOnlyList<QuestProgressView> Quests);
