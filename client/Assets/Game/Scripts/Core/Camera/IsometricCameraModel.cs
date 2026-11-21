using System;
using System.Numerics;

namespace Realmforge.Client.Core.Camera
{
    /// <summary>
    /// Diablo-style fixed-pitch camera: looks down at the hero from a 45° diagonal, zooms along its view ray and
    /// rotates only in quarter turns so the isometric art always reads the same way.
    /// </summary>
    public sealed class IsometricCameraModel
    {
        public const float PitchDegrees = 50f;
        public const float DefaultYawDegrees = 45f;
        public const float YawStepDegrees = 90f;
        public const float MinDistance = 8f;
        public const float MaxDistance = 28f;
        public const float DefaultDistance = 16f;
        public const float CombatDistance = 20f;
        private const float DegreesToRadians = (float)(Math.PI / 180);

        public float YawDegrees { get; private set; } = DefaultYawDegrees;
        public float Distance { get; private set; } = DefaultDistance;

        public void Zoom(float delta) => Distance = Math.Clamp(Distance - delta, MinDistance, MaxDistance);

        public void RotateClockwise() => YawDegrees = (YawDegrees + YawStepDegrees) % 360f;

        public void RotateCounterClockwise() => YawDegrees = (YawDegrees - YawStepDegrees + 360f) % 360f;

        /// <summary>Pulls the camera back during combat so the whole arena grid fits on screen.</summary>
        public void FrameCombat(bool inCombat) => Distance = inCombat ? Math.Max(Distance, CombatDistance) : Math.Min(Distance, DefaultDistance);

        /// <summary>Camera position relative to the followed target (y is up).</summary>
        public Vector3 Offset()
        {
            float pitch = PitchDegrees * DegreesToRadians;
            float yaw = YawDegrees * DegreesToRadians;
            float horizontal = Distance * (float)Math.Cos(pitch);
            return new Vector3(
                -horizontal * (float)Math.Sin(yaw),
                Distance * (float)Math.Sin(pitch),
                -horizontal * (float)Math.Cos(yaw));
        }

        /// <summary>Where a screen ray hits the ground plane (y = 0); null when the ray points at the sky.</summary>
        public static Vector3? GroundHit(Vector3 rayOrigin, Vector3 rayDirection)
        {
            const float ParallelEpsilon = 1e-5f;
            if (rayDirection.Y > -ParallelEpsilon)
            {
                return null;
            }
            float distance = -rayOrigin.Y / rayDirection.Y;
            return rayOrigin + rayDirection * distance;
        }
    }
}
