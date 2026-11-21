using Realmforge.Client.Core.Session;
using Realmforge.Rules.Definitions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Realmforge.Client.Unity.UI
{
    /// <summary>Owns the UI Toolkit document and swaps screens as the session moves between phases.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class UiRoot : MonoBehaviour
    {
        private static readonly Vector2Int ReferenceResolution = new Vector2Int(1920, 1080);

        [Tooltip("Optional asset with the default runtime theme; created at runtime when empty.")]
        [SerializeField] private PanelSettings? panelSettings;

        private VisualElement _root = null!;
        private GameSession _session = null!;
        private LoginScreen _login = null!;
        private CharacterSelectScreen _characterSelect = null!;
        private HudScreen _hud = null!;
        private ContentCatalog _catalog = null!;
        private readonly Label _loading = UiKit.Title("Загрузка мира…");

        public void Bind(GameSession session, ContentCatalog catalog)
        {
            _session = session;
            _catalog = catalog;
            UIDocument document = GetComponent<UIDocument>();
            document.panelSettings = panelSettings != null ? panelSettings : CreatePanelSettings();
            _root = document.rootVisualElement;
            _login = new LoginScreen(session);
            _characterSelect = new CharacterSelectScreen(session, OpenCharacterCreation);
            _hud = new HudScreen(session, catalog);
            _loading.style.alignSelf = Align.Center;
            _loading.style.marginTop = 200;
            session.PhaseChanged += Show;
            session.ConnectionLost += reason => _characterSelect.ShowStatus("Связь с миром потеряна: " + reason);
            Show(session.Phase);
        }

        public bool IsPointerOverUi(Vector2 screenPosition)
        {
            IPanel? panel = _root?.panel;
            if (panel == null)
            {
                return false;
            }
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            return panel.Pick(panelPosition) != null;
        }

        private void Show(SessionPhase phase)
        {
            _root.Clear();
            switch (phase)
            {
                case SessionPhase.LoggedOut:
                    _root.Add(_login.Root);
                    break;
                case SessionPhase.SelectingCharacter:
                    _characterSelect.Refresh();
                    _root.Add(_characterSelect.Root);
                    break;
                case SessionPhase.Connecting:
                case SessionPhase.Transferring:
                    _root.Add(_loading);
                    break;
                case SessionPhase.InWorld:
                    _root.Add(_hud.Root);
                    break;
            }
        }

        private void OpenCharacterCreation()
        {
            _root.Clear();
            var creation = new CharacterCreationScreen(_session, _catalog, () => Show(_session.Phase));
            _root.Add(creation.Root);
        }

        private static PanelSettings CreatePanelSettings()
        {
            PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = ReferenceResolution;
            return settings;
        }
    }
}
