using System.Collections.Generic;
using System.Linq;
using Realmforge.Client.Core.Session;
using Realmforge.Client.Core.World;
using Realmforge.Protocol;
using Realmforge.Rules.Definitions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Realmforge.Client.Unity.UI
{
    /// <summary>In-world overlay: chat, quests, dialogue, combat bar with turn order and a party "join fight" prompt.</summary>
    public sealed class HudScreen
    {
        private const int VisibleChatLines = 8;
        private const int VisibleLogLines = 6;
        private const int SidePanelWidth = 360;
        private const string DefaultChatChannel = "say";

        private readonly GameSession _session;
        private readonly ContentCatalog _catalog;
        private readonly Label _chat = UiKit.Text(string.Empty);
        private readonly Label _quests = UiKit.Text(string.Empty);
        private readonly Label _toast = UiKit.Text(string.Empty);
        private readonly VisualElement _dialogue = UiKit.Panel("dialogue");
        private readonly VisualElement _combat = UiKit.Panel("combat");
        private readonly Label _turnInfo = UiKit.Text(string.Empty);
        private readonly Label _combatLog = UiKit.Text(string.Empty);
        private readonly VisualElement _abilityButtons = UiKit.Row();
        private readonly Button _joinFight;
        private EncounterStartedMessage? _joinOffer;

        public HudScreen(GameSession session, ContentCatalog catalog)
        {
            _session = session;
            _catalog = catalog;
            Root = new VisualElement { name = "hud", pickingMode = PickingMode.Ignore };
            UiKit.Fill(Root);
            Root.Add(BuildChat());
            Root.Add(BuildQuestPanel());
            Root.Add(BuildCombatPanel());
            _joinFight = UiKit.Button("Присоединиться к бою группы", JoinOfferedFight);
            _joinFight.style.position = Position.Absolute;
            _joinFight.style.top = 16;
            _joinFight.style.alignSelf = Align.Center;
            _joinFight.style.display = DisplayStyle.None;
            Root.Add(_joinFight);
            _dialogue.style.position = Position.Absolute;
            _dialogue.style.alignSelf = Align.Center;
            _dialogue.style.top = 120;
            _dialogue.style.width = 480;
            _dialogue.style.display = DisplayStyle.None;
            Root.Add(_dialogue);
            _toast.style.position = Position.Absolute;
            _toast.style.bottom = 200;
            _toast.style.alignSelf = Align.Center;
            Root.Add(_toast);
            Subscribe(session.World);
        }

        public VisualElement Root { get; }

        private void Subscribe(WorldState world)
        {
            world.ChatReceived += _ => RefreshChat();
            world.QuestUpdated += _ => RefreshQuests();
            world.DialogueOpened += ShowDialogue;
            world.EncounterStarted += combat => { _joinFight.style.display = DisplayStyle.None; RefreshCombat(combat); };
            world.CombatUpdated += RefreshCombat;
            world.EncounterEnded += ShowEncounterResult;
            world.EncounterJoinOffered += OfferJoin;
            world.ServerError += error => ShowToast(error.Text, UiKit.ErrorColor);
            world.InventoryChanged += inventory => ShowToast(DescribeLoot(inventory.Gold, inventory.Added), UiKit.AccentColor);
        }

        private VisualElement BuildChat()
        {
            VisualElement panel = UiKit.Panel("chat");
            panel.style.position = Position.Absolute;
            panel.style.left = 16;
            panel.style.bottom = 16;
            panel.style.width = SidePanelWidth;
            panel.Add(_chat);
            var input = new TextField();
            input.RegisterCallback<KeyDownEvent>(key =>
            {
                if (key.keyCode != KeyCode.Return || input.value.Length == 0)
                {
                    return;
                }
                _session.Send(ParseChat(input.value));
                input.value = string.Empty;
            });
            panel.Add(input);
            return panel;
        }

        /// <summary>"/party text" style prefixes pick a channel; plain text goes to nearby players.</summary>
        private static ChatSendRequest ParseChat(string line)
        {
            if (line.StartsWith("/") && line.IndexOf(' ') > 1)
            {
                int space = line.IndexOf(' ');
                return new ChatSendRequest { Channel = line.Substring(1, space - 1), Text = line.Substring(space + 1) };
            }
            return new ChatSendRequest { Channel = DefaultChatChannel, Text = line };
        }

        private VisualElement BuildQuestPanel()
        {
            VisualElement panel = UiKit.Panel("quests");
            panel.style.position = Position.Absolute;
            panel.style.right = 16;
            panel.style.top = 16;
            panel.style.width = SidePanelWidth;
            panel.Add(UiKit.Title("Задания"));
            panel.Add(_quests);
            return panel;
        }

        private VisualElement BuildCombatPanel()
        {
            _combat.style.position = Position.Absolute;
            _combat.style.bottom = 16;
            _combat.style.alignSelf = Align.Center;
            _combat.style.width = 640;
            _combat.style.display = DisplayStyle.None;
            _combat.Add(_turnInfo);
            _combat.Add(_combatLog);
            VisualElement actions = UiKit.Row();
            actions.Add(UiKit.Button("Уклонение", () => _session.Send(CombatCommands.Dodge())));
            actions.Add(UiKit.Button("Конец хода", () => _session.Send(CombatCommands.EndTurn())));
            _combat.Add(actions);
            _combat.Add(_abilityButtons);
            _combat.Add(UiKit.Text("Клик по клетке — шаг, по врагу — атака оружием."));
            return _combat;
        }

        private void RefreshChat()
        {
            IEnumerable<string> lines = _session.World.Chat.Skip(System.Math.Max(0, _session.World.Chat.Count - VisibleChatLines))
                .Select(line => $"[{line.Channel}] {line.Sender}: {line.Text}");
            _chat.text = string.Join("\n", lines);
        }

        private void RefreshQuests()
        {
            _quests.text = string.Join("\n", _session.World.Quests.Select(DescribeQuest));
        }

        private string DescribeQuest(QuestEntry entry)
        {
            string name = _catalog.Quests.TryGetValue(entry.QuestId, out QuestDefinition? quest) ? quest.Name : entry.QuestId;
            return entry.Completed ? $"✓ {name}" : $"• {name} ({entry.StepId}: {entry.Count})";
        }

        private void ShowDialogue(DialogueMessage dialogue)
        {
            _dialogue.Clear();
            _dialogue.Add(UiKit.Title(dialogue.NpcName));
            _dialogue.Add(UiKit.Text(dialogue.Text));
            foreach (DialogueOptionDto option in dialogue.Options)
            {
                _dialogue.Add(UiKit.Button(option.Text, () => ChooseDialogueOption(dialogue.NpcEntityId, option.Id)));
            }
            _dialogue.Add(UiKit.Button("Уйти", CloseDialogue));
            _dialogue.style.display = DisplayStyle.Flex;
        }

        private void ChooseDialogueOption(long npcEntityId, string optionId)
        {
            CloseDialogue();
            _session.Send(new InteractRequest { TargetEntityId = npcEntityId, OptionId = optionId });
        }

        private void CloseDialogue()
        {
            _session.World.CloseDialogue();
            _dialogue.style.display = DisplayStyle.None;
        }

        private void RefreshCombat(CombatState combat)
        {
            _combat.style.display = DisplayStyle.Flex;
            bool myTurn = combat.IsTurnOf(_session.World.LocalEntityId);
            string order = string.Join(" → ", combat.TurnOrder
                .Where(id => combat.Find(id)?.IsAlive ?? false)
                .Select(id => id == combat.CurrentTurnEntityId ? $"[{_session.World.NameOf(id)}]" : _session.World.NameOf(id)));
            _turnInfo.text = $"Раунд {combat.Round}. {(myTurn ? "Ваш ход" : "Ход противника")} — {combat.TurnTimeRemaining:0} с\n{order}";
            _combatLog.text = string.Join("\n", combat.Log.Skip(System.Math.Max(0, combat.Log.Count - VisibleLogLines)));
            RebuildAbilityButtons(combat, myTurn);
        }

        private void RebuildAbilityButtons(CombatState combat, bool myTurn)
        {
            _abilityButtons.Clear();
            CombatantView? enemy = combat.Combatants.FirstOrDefault(other => other.IsAlive && other.Team != combat.Find(_session.World.LocalEntityId)?.Team);
            foreach (AbilityDefinition ability in KnownAbilities())
            {
                Button button = UiKit.Button(ability.Name, () =>
                {
                    long target = ability.Target == TargetKind.Enemy && enemy != null ? enemy.EntityId : _session.World.LocalEntityId;
                    CombatantView? targetView = combat.Find(target);
                    _session.Send(CombatCommands.UseAbility(ability.Id, target, targetView?.CellX ?? 0, targetView?.CellY ?? 0));
                });
                button.SetEnabled(myTurn);
                _abilityButtons.Add(button);
            }
        }

        /// <summary>Abilities unlocked by the character's classes at its level; the server re-checks every use.</summary>
        private IEnumerable<AbilityDefinition> KnownAbilities()
        {
            if (_session.ActiveCharacter == null)
            {
                return Enumerable.Empty<AbilityDefinition>();
            }
            int level = _session.ActiveCharacter.Level;
            return _session.ActiveCharacter.Classes
                .Where(_catalog.Classes.ContainsKey)
                .SelectMany(classId => _catalog.Classes[classId].Abilities)
                .Where(unlock => unlock.Level <= level && _catalog.Abilities.ContainsKey(unlock.AbilityId))
                .Select(unlock => _catalog.Abilities[unlock.AbilityId])
                .Distinct();
        }

        private void ShowEncounterResult(EncounterEndedMessage ended)
        {
            _combat.style.display = DisplayStyle.None;
            string text = ended.Victory ? "Победа! " + DescribeLoot(ended.Gold, ended.Items) + $" Опыт: {ended.Xp}." : "Поражение.";
            ShowToast(text, ended.Victory ? UiKit.AccentColor : UiKit.ErrorColor);
        }

        private void OfferJoin(EncounterStartedMessage started)
        {
            _joinOffer = started;
            _joinFight.style.display = DisplayStyle.Flex;
        }

        private void JoinOfferedFight()
        {
            if (_joinOffer != null)
            {
                _session.Send(new JoinEncounterRequest { EncounterId = _joinOffer.EncounterId });
            }
            _joinFight.style.display = DisplayStyle.None;
        }

        private string DescribeLoot(long gold, ItemStackDto[] items)
        {
            IEnumerable<string> names = items.Select(item =>
                (_catalog.Items.TryGetValue(item.ItemId, out ItemDefinition? definition) ? definition.Name : item.ItemId) + " ×" + item.Quantity);
            string list = string.Join(", ", names);
            return gold > 0 ? $"Золото: {gold}. {list}" : list;
        }

        private void ShowToast(string text, Color color)
        {
            _toast.text = text;
            _toast.style.color = color;
        }
    }
}
