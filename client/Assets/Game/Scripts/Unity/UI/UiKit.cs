using System;
using System.Threading.Tasks;
using Realmforge.Client.Core.Api;
using UnityEngine;
using UnityEngine.UIElements;

namespace Realmforge.Client.Unity.UI
{
    /// <summary>Small helpers so screens are built in code without UXML boilerplate.</summary>
    public static class UiKit
    {
        public const int TitleFontSize = 28;
        public const int BodyFontSize = 16;
        public const float PanelPadding = 12f;
        public static readonly Color PanelColor = new Color(0.08f, 0.07f, 0.1f, 0.88f);
        public static readonly Color AccentColor = new Color(0.86f, 0.68f, 0.32f);
        public static readonly Color SelectedColor = new Color(0.86f, 0.68f, 0.32f, 0.45f);
        public static readonly Color ErrorColor = new Color(0.95f, 0.4f, 0.35f);

        public static VisualElement Panel(string name)
        {
            var panel = new VisualElement { name = name };
            panel.style.backgroundColor = PanelColor;
            panel.style.paddingLeft = PanelPadding;
            panel.style.paddingRight = PanelPadding;
            panel.style.paddingTop = PanelPadding;
            panel.style.paddingBottom = PanelPadding;
            return panel;
        }

        public static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            return row;
        }

        public static Label Title(string text)
        {
            var label = new Label(text);
            label.style.fontSize = TitleFontSize;
            label.style.color = AccentColor;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        public static Label Text(string text)
        {
            var label = new Label(text);
            label.style.fontSize = BodyFontSize;
            label.style.color = Color.white;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        public static Button Button(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.fontSize = BodyFontSize;
            return button;
        }

        public static void Fill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.right = 0;
            element.style.top = 0;
            element.style.bottom = 0;
        }

        /// <summary>Runs a gateway call from a button and shows its error in <paramref name="status"/> instead of throwing.</summary>
        public static async void RunAsync(Label status, Func<Task> action)
        {
            status.text = "…";
            status.style.color = Color.white;
            try
            {
                await action();
                status.text = string.Empty;
            }
            catch (ApiException error)
            {
                status.text = error.Message;
                status.style.color = ErrorColor;
            }
        }
    }
}
