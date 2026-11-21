using Realmforge.Client.Core.Api;
using Realmforge.Client.Core.Content;
using Realmforge.Client.Core.Net;
using Realmforge.Client.Core.Session;
using Realmforge.Client.Unity.Input;
using Realmforge.Client.Unity.Net;
using Realmforge.Client.Unity.UI;
using Realmforge.Client.Unity.World;
using Realmforge.Rules.Definitions;
using UnityEngine;

namespace Realmforge.Client.Unity
{
    /// <summary>
    /// The only component the boot scene needs: builds the session from Client.Core and wires Unity views to it.
    /// Everything game-specific lives in Client.Core so it can be unit-tested without the editor.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        private const string LocalGatewayUrl = "http://localhost:5080";

        [SerializeField] private string gatewayUrl = LocalGatewayUrl;

        private GameSession _session = null!;
        private ContentCatalog _catalog = null!;
        private IsometricCameraRig _cameraRig = null!;
        private EntityPresenter _entities = null!;
        private ZoneLoader _zoneLoader = null!;

        /// <summary>Lets any empty scene boot the game, so the project has no hand-maintained boot scene to break.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (FindAnyObjectByType<GameBootstrap>() == null)
            {
                new GameObject("Realmforge").AddComponent<GameBootstrap>();
            }
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            _catalog = LoadContent();
            _session = new GameSession(new GatewayClient(new UnityHttpTransport(), gatewayUrl), CreateTransport());
            _cameraRig = CreateCameraRig();
            _entities = gameObject.AddComponent<EntityPresenter>();
            _entities.Bind(_session.World);
            _zoneLoader = gameObject.AddComponent<ZoneLoader>();
            UiRoot ui = gameObject.AddComponent<UiRoot>();
            ui.Bind(_session, _catalog);
            gameObject.AddComponent<PlayerInputController>().Bind(_session, _cameraRig, ui.IsPointerOverUi);
            SubscribeToWorld();
        }

        private void Update()
        {
            _session.Tick(Time.unscaledDeltaTime);
            if (_cameraRig.Target == null)
            {
                _cameraRig.Target = _entities.Find(_session.World.LocalEntityId);
            }
        }

        private void OnApplicationQuit()
        {
            _session.LeaveWorld();
        }

        private void SubscribeToWorld()
        {
            _session.World.ZoneEntered += welcome =>
            {
                _cameraRig.Target = null;
                float size = _catalog.Zones.TryGetValue(welcome.ZoneId, out ZoneDefinition? zone) ? zone.SizeMeters : 0;
                _zoneLoader.Load(welcome.SceneAddress, size);
            };
            _session.World.EncounterStarted += _ => _cameraRig.Model.FrameCombat(true);
            _session.World.EncounterEnded += _ => _cameraRig.Model.FrameCombat(false);
        }

        private static ContentCatalog LoadContent()
        {
            TextAsset index = Resources.Load<TextAsset>(ClientContent.ResourcesFolder + "/" + ClientContent.IndexName);
            return ClientContent.Load(index.text, path => Resources.Load<TextAsset>(ClientContent.ResourcePath(path))?.text);
        }

        private static IGameTransport CreateTransport()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new WebSocketGameTransport();
#else
            return new UdpGameTransport();
#endif
        }

        private static IsometricCameraRig CreateCameraRig()
        {
            Camera camera = Camera.main != null ? Camera.main : new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            return camera.TryGetComponent(out IsometricCameraRig rig) ? rig : camera.gameObject.AddComponent<IsometricCameraRig>();
        }
    }
}
