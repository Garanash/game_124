using System;
using System.Collections.Generic;
using Realmforge.Client.Core.CharacterCreation;
using Realmforge.Client.Core.Session;
using Realmforge.Rules;
using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Realmforge.Client.Unity.UI
{
    /// <summary>Race grid grouped by start location, the 3x3 alignment chart and a 27-point buy.</summary>
    public sealed class CharacterCreationScreen
    {
        private const string PortraitFolder = "Portraits/";
        private const int PortraitSize = 64;
        private const int AlignmentCellWidth = 150;
        private const int AlignmentColumns = 3;

        private static readonly Dictionary<AlignmentCell, string> AlignmentNames = new Dictionary<AlignmentCell, string>
        {
            { AlignmentCell.LawfulGood, "Законно-добрый" },
            { AlignmentCell.NeutralGood, "Нейтрально-добрый" },
            { AlignmentCell.ChaoticGood, "Хаотично-добрый" },
            { AlignmentCell.LawfulNeutral, "Законно-нейтральный" },
            { AlignmentCell.TrueNeutral, "Истинно нейтральный" },
            { AlignmentCell.ChaoticNeutral, "Хаотично-нейтральный" },
            { AlignmentCell.LawfulEvil, "Законно-злой" },
            { AlignmentCell.NeutralEvil, "Нейтрально-злой" },
            { AlignmentCell.ChaoticEvil, "Хаотично-злой" },
        };

        private static readonly Dictionary<Ability, string> AbilityNames = new Dictionary<Ability, string>
        {
            { Ability.Strength, "Сила" },
            { Ability.Dexterity, "Ловкость" },
            { Ability.Constitution, "Телосложение" },
            { Ability.Intelligence, "Интеллект" },
            { Ability.Wisdom, "Мудрость" },
            { Ability.Charisma, "Харизма" },
        };

        private readonly CharacterCreationModel _model;
        private readonly Dictionary<string, Button> _raceButtons = new Dictionary<string, Button>();
        private readonly Dictionary<AlignmentCell, Button> _alignmentButtons = new Dictionary<AlignmentCell, Button>();
        private readonly VisualElement _abilities = new VisualElement();
        private readonly Label _summary = UiKit.Text(string.Empty);
        private readonly Label _status = UiKit.Text(string.Empty);
        private readonly Button _create;

        public CharacterCreationScreen(GameSession session, ContentCatalog catalog, Action close)
        {
            _model = new CharacterCreationModel(catalog);
            Root = UiKit.Panel("character-creation");
            UiKit.Fill(Root);
            var scroll = new ScrollView();
            Root.Add(scroll);
            scroll.Add(UiKit.Title("Новый герой"));
            var name = new TextField("Имя");
            name.RegisterValueChangedCallback(change => { _model.Name = change.newValue; Refresh(); });
            scroll.Add(name);
            scroll.Add(BuildRaceGrid());
            scroll.Add(BuildAlignmentChart());
            scroll.Add(UiKit.Title("Характеристики"));
            scroll.Add(_abilities);
            scroll.Add(_summary);
            VisualElement buttons = UiKit.Row();
            _create = UiKit.Button("Создать", () => UiKit.RunAsync(_status, async () =>
            {
                await session.CreateCharacter(_model.ToRequest());
                close();
            }));
            buttons.Add(_create);
            buttons.Add(UiKit.Button("Назад", close));
            scroll.Add(buttons);
            scroll.Add(_status);
            _model.Changed += Refresh;
            Refresh();
        }

        public VisualElement Root { get; }

        private VisualElement BuildRaceGrid()
        {
            var container = new VisualElement();
            container.Add(UiKit.Title("Раса и стартовая земля"));
            foreach (RaceGroup group in _model.RaceGroups)
            {
                string continent = group.Continent != null ? group.Continent.Name + " — " : string.Empty;
                container.Add(UiKit.Text(continent + group.StartZone.Name));
                VisualElement row = UiKit.Row();
                foreach (RaceDefinition race in group.Races)
                {
                    row.Add(RaceButton(race));
                }
                container.Add(row);
            }
            return container;
        }

        private Button RaceButton(RaceDefinition race)
        {
            Button button = UiKit.Button(race.Name, () => _model.SelectRace(race.Id));
            button.style.flexDirection = FlexDirection.Column;
            Texture2D? portrait = Resources.Load<Texture2D>(PortraitFolder + race.Id);
            if (portrait != null)
            {
                var image = new Image { image = portrait, scaleMode = ScaleMode.ScaleToFit };
                image.style.width = PortraitSize;
                image.style.height = PortraitSize;
                button.Insert(0, image);
            }
            _raceButtons[race.Id] = button;
            return button;
        }

        private VisualElement BuildAlignmentChart()
        {
            var container = new VisualElement();
            container.Add(UiKit.Title("Мировоззрение"));
            VisualElement row = UiKit.Row();
            foreach (AlignmentCell cell in (AlignmentCell[])Enum.GetValues(typeof(AlignmentCell)))
            {
                if ((int)cell % AlignmentColumns == 0)
                {
                    row = UiKit.Row();
                    container.Add(row);
                }
                Button button = UiKit.Button(AlignmentNames[cell], () => _model.SelectAlignment(cell));
                button.style.width = AlignmentCellWidth;
                _alignmentButtons[cell] = button;
                row.Add(button);
            }
            return container;
        }

        private void Refresh()
        {
            foreach (KeyValuePair<string, Button> pair in _raceButtons)
            {
                Highlight(pair.Value, pair.Key == _model.SelectedRaceId);
            }
            foreach (KeyValuePair<AlignmentCell, Button> pair in _alignmentButtons)
            {
                Highlight(pair.Value, pair.Key == _model.Alignment);
            }
            RebuildAbilities();
            string zone = _model.StartZone?.Name ?? "—";
            _summary.text = $"Очки: {_model.PointsRemaining} из {PointBuy.Budget}. Начало пути: {zone}. Класс выбирается в игре у наставника.";
            RuleResult validation = _model.Validate();
            _create.SetEnabled(validation.Success);
            _status.text = validation.Success ? string.Empty : validation.Message;
        }

        private void RebuildAbilities()
        {
            _abilities.Clear();
            foreach (Ability ability in (Ability[])Enum.GetValues(typeof(Ability)))
            {
                VisualElement row = UiKit.Row();
                Button minus = UiKit.Button("−", () => _model.Decrease(ability));
                minus.SetEnabled(_model.CanDecrease(ability));
                Button plus = UiKit.Button("+", () => _model.Increase(ability));
                plus.SetEnabled(_model.CanIncrease(ability));
                int bonus = _model.RaceBonus(ability);
                string bonusText = bonus != 0 ? $" ({bonus:+0;-0} раса)" : string.Empty;
                row.Add(minus);
                row.Add(UiKit.Text($"{AbilityNames[ability]}: {_model.FinalScore(ability)}{bonusText}, мод. {_model.FinalModifier(ability):+0;-0;0}"));
                row.Add(plus);
                _abilities.Add(row);
            }
        }

        private static void Highlight(VisualElement element, bool selected)
        {
            element.style.backgroundColor = selected ? UiKit.SelectedColor : new StyleColor(StyleKeyword.Null);
        }
    }
}
