using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Realmforge.Client.Unity.World
{
    /// <summary>
    /// Each zone is an Addressables scene so new locations ship as downloadable bundles without a client update.
    /// Zones whose art is not built yet fall back to a flat placeholder ground so the server content stays playable.
    /// </summary>
    public sealed class ZoneLoader : MonoBehaviour
    {
        private const float PlaceholderGroundScale = 60f;
        private const float PlaceholderGroundSize = 10f;
        private const float PlaceholderSunPitch = 50f;
        private const float PlaceholderSunYaw = -30f;

        private AsyncOperationHandle<SceneInstance>? _loadedScene;
        private GameObject? _placeholderGround;

        public string CurrentAddress { get; private set; } = string.Empty;

        public void Load(string sceneAddress, float zoneSizeMeters)
        {
            if (sceneAddress == CurrentAddress)
            {
                return;
            }
            CurrentAddress = sceneAddress;
            StopAllCoroutines();
            StartCoroutine(Switch(sceneAddress, zoneSizeMeters));
        }

        private IEnumerator Switch(string sceneAddress, float zoneSizeMeters)
        {
            yield return Unload();
            AsyncOperationHandle<IList<IResourceLocation>> locations = Addressables.LoadResourceLocationsAsync(sceneAddress);
            yield return locations;
            bool hasScene = locations.Status == AsyncOperationStatus.Succeeded && locations.Result.Count > 0;
            Addressables.Release(locations);
            if (!hasScene)
            {
                Debug.LogWarning($"Zone scene '{sceneAddress}' is not in Addressables yet; using placeholder ground.");
                CreatePlaceholderGround(zoneSizeMeters);
                yield break;
            }
            AsyncOperationHandle<SceneInstance> load = Addressables.LoadSceneAsync(sceneAddress, LoadSceneMode.Additive);
            yield return load;
            _loadedScene = load;
        }

        private IEnumerator Unload()
        {
            if (_placeholderGround != null)
            {
                Destroy(_placeholderGround);
                _placeholderGround = null;
            }
            if (_loadedScene.HasValue)
            {
                AsyncOperationHandle<SceneInstance> unload = Addressables.UnloadSceneAsync(_loadedScene.Value);
                _loadedScene = null;
                yield return unload;
            }
        }

        private void CreatePlaceholderGround(float zoneSizeMeters)
        {
            _placeholderGround = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _placeholderGround.name = "PlaceholderGround";
            float half = zoneSizeMeters / 2f;
            _placeholderGround.transform.position = new Vector3(half, 0, half);
            float scale = zoneSizeMeters > 0 ? zoneSizeMeters / PlaceholderGroundSize : PlaceholderGroundScale;
            _placeholderGround.transform.localScale = new Vector3(scale, 1, scale);
            var sun = new GameObject("PlaceholderSun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.SetParent(_placeholderGround.transform, false);
            sun.transform.rotation = Quaternion.Euler(PlaceholderSunPitch, PlaceholderSunYaw, 0);
        }
    }
}
