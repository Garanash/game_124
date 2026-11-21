using Realmforge.Protocol;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Inventory;
using Realmforge.Rules.Professions;
using Realmforge.Rules.Pvp;
using Realmforge.Rules.Quests;
using Realmforge.Rules.Stats;
using Xunit;

namespace Realmforge.Rules.Tests;

public class SystemsTests
{
    [Fact]
    public void Backpack_rejects_overlap_and_rotates_shapes()
    {
        var catalog = TestCatalog.Create();
        var backpack = new Backpack { Width = 3, Height = 3 };
        backpack.Place(Guid.NewGuid(), "sword", 0, 0, 0, catalog);

        Assert.Equal("backpack.overlap", backpack.CanPlace("whetstone", 0, 1, 0, catalog).Code);
        Assert.Equal("backpack.out_of_bounds", backpack.CanPlace("sword", 2, 2, 0, catalog).Code);
        Assert.True(backpack.CanPlace("sword", 1, 2, 1, catalog).Success);
    }

    [Fact]
    public void Adjacent_items_trigger_synergy()
    {
        var catalog = TestCatalog.Create();
        var backpack = new Backpack();
        backpack.Place(Guid.NewGuid(), "sword", 0, 0, 0, catalog);
        backpack.Place(Guid.NewGuid(), "whetstone", 3, 0, 0, catalog);
        Assert.Equal(0, BackpackRules.ComputeBonuses(backpack, catalog)[Stat.DamageBonus]);

        backpack.Place(Guid.NewGuid(), "whetstone", 1, 1, 0, catalog);
        Assert.Equal(1, BackpackRules.ComputeBonuses(backpack, catalog)[Stat.DamageBonus]);
    }

    [Fact]
    public void Touching_items_fuse_into_better_item()
    {
        var catalog = TestCatalog.Create();
        var backpack = new Backpack();
        backpack.Place(Guid.NewGuid(), "sword", 0, 0, 0, catalog);
        backpack.Place(Guid.NewGuid(), "sword", 3, 0, 0, catalog);
        Assert.Empty(BackpackRules.FindFusions(backpack, catalog));

        backpack.Place(Guid.NewGuid(), "sword", 1, 0, 0, catalog);
        Assert.Single(BackpackRules.FindFusions(backpack, catalog));

        Assert.True(BackpackRules.Fuse(backpack, "forge_great_sword", Guid.NewGuid(), catalog).Success);
        Assert.Contains(backpack.Items, item => item.ItemId == "great_sword");
        Assert.Equal(2, backpack.Items.Count);
    }

    [Fact]
    public void Quest_choice_shifts_alignment_and_completion_unlocks_class()
    {
        var catalog = TestCatalog.Create();
        CharacterSheet sheet = CharacterRules.Create(Guid.NewGuid(), Guid.NewGuid(), TestCatalog.DwarfRequest(), catalog, DateTime.UtcNow);
        var quest = new QuestDefinition
        {
            Id = "trial",
            Steps =
            {
                new QuestStep { Id = "kill", Type = QuestStepType.Kill, TargetId = "wolf", Count = 2, Next = "mercy" },
                new QuestStep
                {
                    Id = "mercy",
                    Type = QuestStepType.Choice,
                    Choices = { new QuestChoice { Id = "execute", Alignment = new AlignmentShift { Law = -10, Good = -20 } } },
                },
            },
            Rewards = new QuestRewards { Xp = 300, UnlocksClassId = "fighter" },
        };
        Assert.True(QuestRules.CanAccept(sheet, quest, Array.Empty<QuestProgress>()).Success);
        QuestProgress progress = QuestRules.Start(quest);

        QuestRules.OnEvent(quest, progress, new QuestEvent(QuestStepType.Kill, "wolf"));
        QuestRules.OnEvent(quest, progress, new QuestEvent(QuestStepType.Kill, "wolf"));
        Assert.Equal("mercy", progress.StepId);

        QuestAdvance advance = QuestRules.Choose(quest, progress, "execute");
        QuestRules.ApplyToSheet(sheet, quest, advance);

        Assert.True(advance.Completed);
        Assert.Contains("fighter", sheet.UnlockedClasses);
        Assert.Equal(Alignment.StartingMagnitude - 20, sheet.GoodEvil);
        Assert.Equal(2, sheet.Level);
    }

    [Fact]
    public void Offline_job_is_capped_and_deterministic()
    {
        var catalog = TestCatalog.Create();
        catalog.LootTables["herbs"] = new LootTable { Id = "herbs", Entries = { new LootEntry { ItemId = "herb", Min = 1, Max = 3 } } };
        catalog.Nodes["herb_patch"] = new GatheringNodeDefinition { Id = "herb_patch", ProfessionId = "herbalism", LootTableId = "herbs" };
        catalog.OfflineJobs["pick_herbs"] = new OfflineJobDefinition { Id = "pick_herbs", Kind = OfflineJobKind.Gather, ProfessionId = "herbalism", TargetId = "herb_patch", CycleMinutes = 10 };
        CharacterSheet sheet = CharacterRules.Create(Guid.NewGuid(), Guid.NewGuid(), TestCatalog.DwarfRequest(), catalog, DateTime.UtcNow);
        DateTime start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        OfflineJobState state = OfflineJobs.Start(sheet, catalog.OfflineJobs["pick_herbs"], start, TimeSpan.FromHours(20));
        OfflineJobResult first = OfflineJobs.Simulate(sheet, state, catalog, new StackInventory(), start.AddDays(2));
        OfflineJobResult second = OfflineJobs.Simulate(sheet, state, catalog, new StackInventory(), start.AddDays(3));

        Assert.Equal(72, first.Cycles);
        Assert.Equal(first.Loot.Items.Single().Quantity, second.Loot.Items.Single().Quantity);
        Assert.True(ProfessionRules.Skill(sheet, "herbalism") > 1);
    }

    [Fact]
    public void Arena_rating_is_zero_sum()
    {
        (int winner, int loser) = ArenaRating.Update(1500, 1500);
        Assert.Equal(3000, winner + loser);
        Assert.True(winner > 1500);
    }

    [Fact]
    public void Protocol_round_trips_union_messages()
    {
        byte[] payload = ProtocolCodec.Encode(new MoveToRequest { X = 1.5f, Z = -2 });
        var decoded = Assert.IsType<MoveToRequest>(ProtocolCodec.DecodeClient(payload));
        Assert.Equal(1.5f, decoded.X);

        byte[] snapshot = ProtocolCodec.Encode(new WorldSnapshotMessage { Tick = 5, Entities = new[] { new EntityState { EntityId = 9, X = 3 } } });
        var decodedSnapshot = Assert.IsType<WorldSnapshotMessage>(ProtocolCodec.DecodeServer(snapshot));
        Assert.Equal(9, decodedSnapshot.Entities[0].EntityId);
    }
}
