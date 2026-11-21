using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;

namespace Realmforge.Rules.Quests
{
    public sealed class QuestProgress
    {
        public string QuestId { get; set; } = string.Empty;
        public string StepId { get; set; } = string.Empty;
        public int Count { get; set; }
        public bool IsComplete { get; set; }
        public List<string> ChoicesMade { get; set; } = new List<string>();
    }

    public sealed class QuestEvent
    {
        public QuestEvent(QuestStepType type, string targetId, int amount = 1)
        {
            Type = type;
            TargetId = targetId;
            Amount = amount;
        }

        public QuestStepType Type { get; }
        public string TargetId { get; }
        public int Amount { get; }
    }

    /// <summary>Result of advancing a quest: alignment/reputation deltas to apply and rewards if completed.</summary>
    public sealed class QuestAdvance
    {
        public bool Changed { get; set; }
        public bool Completed { get; set; }
        public int LawShift { get; set; }
        public int GoodShift { get; set; }
        public List<ReputationChange> Reputation { get; } = new List<ReputationChange>();
        public QuestRewards? Rewards { get; set; }
    }

    public static class QuestRules
    {
        public static RuleResult CanAccept(CharacterSheet sheet, QuestDefinition quest, IReadOnlyCollection<QuestProgress> active)
        {
            if (sheet.CompletedQuests.Contains(quest.Id) || active.Any(progress => progress.QuestId == quest.Id))
            {
                return RuleResult.Fail("quest.taken", "Quest already taken or completed.");
            }
            if (sheet.Level < quest.MinLevel)
            {
                return RuleResult.Fail("quest.level", "Requires level " + quest.MinLevel);
            }
            if (quest.Requires.Any(required => !sheet.CompletedQuests.Contains(required)))
            {
                return RuleResult.Fail("quest.requires", "Complete prerequisite quests first.");
            }
            if (!quest.Alignment.IsSatisfiedBy(sheet.GetAlignment()))
            {
                return RuleResult.Fail("quest.alignment", "This quest-giver does not trust you.");
            }
            return quest.Steps.Count > 0 ? RuleResult.Ok() : RuleResult.Fail("quest.empty", "Quest has no steps.");
        }

        public static QuestProgress Start(QuestDefinition quest)
        {
            return new QuestProgress { QuestId = quest.Id, StepId = quest.Steps[0].Id };
        }

        public static QuestAdvance OnEvent(QuestDefinition quest, QuestProgress progress, QuestEvent questEvent)
        {
            var advance = new QuestAdvance();
            QuestStep? step = CurrentStep(quest, progress);
            if (step == null || progress.IsComplete || step.Type == QuestStepType.Choice)
            {
                return advance;
            }
            if (step.Type != questEvent.Type || step.TargetId != questEvent.TargetId)
            {
                return advance;
            }
            progress.Count += questEvent.Amount;
            advance.Changed = true;
            if (progress.Count >= step.Count)
            {
                MoveTo(quest, progress, step.Next, advance);
            }
            return advance;
        }

        public static QuestAdvance Choose(QuestDefinition quest, QuestProgress progress, string choiceId)
        {
            var advance = new QuestAdvance();
            QuestStep? step = CurrentStep(quest, progress);
            QuestChoice? choice = step?.Choices.FirstOrDefault(option => option.Id == choiceId);
            if (step == null || step.Type != QuestStepType.Choice || choice == null || progress.IsComplete)
            {
                return advance;
            }
            progress.ChoicesMade.Add(choice.Id);
            advance.Changed = true;
            advance.LawShift = choice.Alignment.Law;
            advance.GoodShift = choice.Alignment.Good;
            advance.Reputation.AddRange(choice.Reputation);
            MoveTo(quest, progress, choice.Next, advance);
            return advance;
        }

        /// <summary>Applies alignment, reputation and completion bookkeeping; item/gold/xp rewards are granted by the caller.</summary>
        public static void ApplyToSheet(CharacterSheet sheet, QuestDefinition quest, QuestAdvance advance)
        {
            sheet.SetAlignment(sheet.GetAlignment().Shift(advance.LawShift, advance.GoodShift));
            IEnumerable<ReputationChange> reputation = advance.Reputation;
            if (advance.Completed)
            {
                sheet.CompletedQuests.Add(quest.Id);
                reputation = reputation.Concat(quest.Rewards.Reputation);
                if (!string.IsNullOrEmpty(quest.Rewards.UnlocksClassId))
                {
                    sheet.UnlockedClasses.Add(quest.Rewards.UnlocksClassId);
                }
                CharacterRules.GrantXp(sheet, quest.Rewards.Xp);
                sheet.Gold += quest.Rewards.Gold;
            }
            foreach (ReputationChange change in reputation)
            {
                sheet.Reputation[change.FactionId] = FactionRules.ClampReputation(sheet.ReputationWith(change.FactionId) + change.Amount);
            }
        }

        public static QuestStep? CurrentStep(QuestDefinition quest, QuestProgress progress)
        {
            return quest.Steps.FirstOrDefault(step => step.Id == progress.StepId);
        }

        private static void MoveTo(QuestDefinition quest, QuestProgress progress, string nextStepId, QuestAdvance advance)
        {
            progress.Count = 0;
            if (string.IsNullOrEmpty(nextStepId) || quest.Steps.All(step => step.Id != nextStepId))
            {
                progress.IsComplete = true;
                advance.Completed = true;
                advance.Rewards = quest.Rewards;
                return;
            }
            progress.StepId = nextStepId;
        }
    }
}
