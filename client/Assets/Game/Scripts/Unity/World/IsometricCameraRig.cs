using Realmforge.Client.Core.Camera;
using UnityEngine;

namespace Realmforge.Client.Unity.World
{
    /// <summary>Applies <see cref="IsometricCameraModel"/> to the Unity camera and smoothly follows the hero.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class IsometricCameraRig : MonoBehaviour
    {
        private const float FollowSharpness = 10f;

        public IsometricCameraModel Model { get; } = new IsometricCameraModel();
        public Transform? Target { get; set; }
        public Camera Camera { get; private set; } = null!;

        private void Awake()
        {
            Camera = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (Target == null)
            {
                return;
            }
            System.Numerics.Vector3 offset = Model.Offset();
            Vector3 desired = Target.position + new Vector3(offset.X, offset.Y, offset.Z);
            float blend = 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, blend);
            transform.LookAt(Target.position);
        }
    }
}
