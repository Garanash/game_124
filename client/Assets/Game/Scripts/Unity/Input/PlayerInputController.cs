using System;
using Realmforge.Client.Core.Camera;
using Realmforge.Client.Core.Session;
using Realmforge.Client.Core.World;
using Realmforge.Client.Unity.World;
using Realmforge.Protocol;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Realmforge.Client.Unity.Input
{
    /// <summary>
    /// Diablo-style click controls. Out of combat a click walks or interacts; in combat the same click becomes
    /// a grid move or an attack, so players learn a single gesture.
    /// </summary>
    public sealed class PlayerInputController : MonoBehaviour
    {
        private const float MaxRayDistance = 500f;
        private const float ScrollToZoom = 0.01f;

        private GameSession? _session;
        private IsometricCameraRig? _cameraRig;
        private Func<Vector2, bool> _isPointerOverUi = _ => false;

        public void Bind(GameSession session, IsometricCameraRig cameraRig, Func<Vector2, bool> isPointerOverUi)
        {
            _session = session;
            _cameraRig = cameraRig;
            _isPointerOverUi = isPointerOverUi;
        }

        private void Update()
        {
            if (_session == null || _cameraRig == null || _session.Phase != SessionPhase.InWorld)
            {
                return;
            }
            HandleCamera(_cameraRig.Model);
            Mouse? mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }
            Vector2 pointer = mouse.position.ReadValue();
            if (!_isPointerOverUi(pointer))
            {
                HandleClick(_session, _cameraRig.Camera.ScreenPointToRay(pointer));
            }
        }

        private static void HandleCamera(IsometricCameraModel camera)
        {
            Mouse? mouse = Mouse.current;
            if (mouse != null)
            {
                camera.Zoom(mouse.scroll.ReadValue().y * ScrollToZoom);
            }
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }
            if (keyboard.qKey.wasPressedThisFrame)
            {
                camera.RotateCounterClockwise();
            }
            if (keyboard.eKey.wasPressedThisFrame)
            {
                camera.RotateClockwise();
            }
        }

        private static void HandleClick(GameSession session, Ray ray)
        {
            CombatState? combat = session.World.Combat;
            if (Physics.Raycast(ray, out RaycastHit hit, MaxRayDistance) && hit.collider.GetComponentInParent<EntityHandle>() is EntityHandle target
                && target.EntityId != session.World.LocalEntityId)
            {
                session.Send(combat != null ? CombatCommands.Attack(target.EntityId) : new InteractRequest { TargetEntityId = target.EntityId });
                return;
            }
            System.Numerics.Vector3? ground = IsometricCameraModel.GroundHit(ToNumerics(ray.origin), ToNumerics(ray.direction));
            if (!ground.HasValue)
            {
                return;
            }
            if (combat == null)
            {
                session.Send(new MoveToRequest { X = ground.Value.X, Z = ground.Value.Z });
            }
            else if (combat.TryWorldToCell(ground.Value.X, ground.Value.Z, out int cellX, out int cellY))
            {
                session.Send(CombatCommands.Move(cellX, cellY));
            }
        }

        private static System.Numerics.Vector3 ToNumerics(Vector3 vector) => new System.Numerics.Vector3(vector.x, vector.y, vector.z);
    }
}
