using System.Collections.Generic;
using Realmforge.Rules.Characters;

namespace Realmforge.Rules.Definitions
{
    public enum QuestStepType
    {
        Talk,
        Kill,
        Collect,
        Reach,
        Choice,
        Craft,
    }

    public sealed class ReputationChange
    {
        public string FactionId { get; set; } = string.Empty;
        public int Amount { get; set; }
    }

    public sealed class QuestChoice
    {
        public string Id { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public AlignmentShift Alignment { get; set; } = new AlignmentShift();
        public List<ReputationChange> Reputation { get; set; } = new List<ReputationChange>();
        /// <summary>Step to jump to; empty completes the quest.</summary>
        public string Next { get; set; } = string.Empty;
    }

    public sealed class QuestStep
    {
        public string Id { get; set; } = string.Empty;
        public QuestStepType Type { get; set; }
        public string Text { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public int Count { get; set; } = 1;
        public List<QuestChoice> Choices { get; set; } = new List<QuestChoice>();
        /// <summary>Next step for non-choice steps; empty completes the quest.</summary>
        public string Next { get; set; } = string.Empty;
    }

    public sealed class QuestRewards
    {
        public int Xp { get; set; }
        public int Gold { get; set; }
        public List<ItemQuantity> Items { get; set; } = new List<ItemQuantity>();
        public string UnlocksClassId { get; set; } = string.Empty;
        public List<ReputationChange> Reputation { get; set; } = new List<ReputationChange>();
    }

    public sealed class QuestDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string GiverNpcId { get; set; } = string.Empty;
        public int MinLevel { get; set; } = 1;
        public List<string> Requires { get; set; } = new List<string>();
        public AlignmentRequirement Alignment { get; set; } = new AlignmentRequirement();
        public List<QuestStep> Steps { get; set; } = new List<QuestStep>();
        public QuestRewards Rewards { get; set; } = new QuestRewards();
    }
}
