using System;
using Realmforge.Client.Core.Api;
using Realmforge.Client.Core.Session;
using UnityEngine.UIElements;

namespace Realmforge.Client.Unity.UI
{
    public sealed class LoginScreen
    {
        private readonly TextField _email = new TextField("Почта");
        private readonly TextField _password = new TextField("Пароль") { isPasswordField = true };
        private readonly Label _status = UiKit.Text(string.Empty);

        public LoginScreen(GameSession session)
        {
            Root = UiKit.Panel("login");
            Root.style.alignSelf = Align.Center;
            Root.style.marginTop = 120;
            Root.style.width = 420;
            Root.Add(UiKit.Title("Realmforge"));
            Root.Add(_email);
            Root.Add(_password);
            VisualElement buttons = UiKit.Row();
            buttons.Add(UiKit.Button("Войти", () => Submit(session, register: false)));
            buttons.Add(UiKit.Button("Регистрация", () => Submit(session, register: true)));
            Root.Add(buttons);
            Root.Add(_status);
        }

        public VisualElement Root { get; }

        private void Submit(GameSession session, bool register)
        {
            UiKit.RunAsync(_status, () => session.Login(_email.value.Trim(), _password.value, register));
        }
    }

    public sealed class CharacterSelectScreen
    {
        private readonly GameSession _session;
        private readonly VisualElement _list = new VisualElement();
        private readonly Label _status = UiKit.Text(string.Empty);

        public CharacterSelectScreen(GameSession session, Action openCreation)
        {
            _session = session;
            Root = UiKit.Panel("character-select");
            Root.style.alignSelf = Align.Center;
            Root.style.marginTop = 80;
            Root.style.width = 520;
            Root.Add(UiKit.Title("Ваши герои"));
            Root.Add(_list);
            Root.Add(UiKit.Button("Создать героя", openCreation));
            Root.Add(_status);
        }

        public VisualElement Root { get; }

        public void Refresh()
        {
            _list.Clear();
            if (_session.Characters.Count == 0)
            {
                _list.Add(UiKit.Text("Пока нет ни одного героя."));
            }
            foreach (CharacterSummaryDto character in _session.Characters)
            {
                VisualElement row = UiKit.Row();
                string classes = character.Classes.Count > 0 ? string.Join(", ", character.Classes) : "без класса";
                row.Add(UiKit.Text($"{character.Name} — {character.RaceId}, ур. {character.Level} ({classes})"));
                row.Add(UiKit.Button("Играть", () => UiKit.RunAsync(_status, () => _session.EnterWorld(character))));
                _list.Add(row);
            }
        }

        public void ShowStatus(string message)
        {
            _status.text = message;
            _status.style.color = UiKit.ErrorColor;
        }
    }
}
