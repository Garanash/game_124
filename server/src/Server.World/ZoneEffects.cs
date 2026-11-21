using Realmforge.Rules.Combat;
using Realmforge.Rules.Quests;

namespace Realmforge.Server.World;

/// <summary>
/// Side effects the simulation requests from the outside world (persistence, cross-server messaging).
/// The simulation stays synchronous and deterministic; the host drains and executes these asynchronously.
/// </summary>
public abstract record ZoneEffect(Guid CharacterId);

public sealed record GrantEncounterRewards(Guid CharacterId, long Xp, LootResult Loot, IReadOnlyList<string> KilledMobIds) : ZoneEffect(CharacterId);

public sealed record CharacterDied(Guid CharacterId) : ZoneEffect(CharacterId);

public sealed record ReportQuestEvent(Guid CharacterId, QuestEvent Event) : ZoneEffect(CharacterId);

public sealed record NodeGathered(Guid CharacterId, string NodeId) : ZoneEffect(CharacterId);

public sealed record NpcInteracted(Guid CharacterId, long NpcEntityId, string NpcId, string OptionId) : ZoneEffect(CharacterId);

public sealed record QuestChoiceMade(Guid CharacterId, string QuestId, string ChoiceId) : ZoneEffect(CharacterId);

public sealed record SavePosition(Guid CharacterId, string ZoneId, float X, float Z, int Hp) : ZoneEffect(CharacterId);

public sealed record TransferToZone(Guid CharacterId, string ZoneId, float ArrivalX, float ArrivalZ) : ZoneEffect(CharacterId);

public sealed record PublishChat(Guid CharacterId, string Subject, string Channel, string SenderName, string Text) : ZoneEffect(CharacterId);

public sealed record PvpKill(Guid CharacterId, Guid VictimCharacterId, int KillerGoodShift, int KillerLawShift, int VictimGoldLossPercent)
    : ZoneEffect(CharacterId);

public sealed record InstanceBossDefeated(Guid CharacterId, string BossMobId) : ZoneEffect(CharacterId);
