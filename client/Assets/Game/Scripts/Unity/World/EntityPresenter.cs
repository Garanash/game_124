using System.Collections.Generic;
using Realmforge.Client.Core.World;
using Realmforge.Protocol;
using UnityEngine;

namespace Realmforge.Client.Unity.World
{
    /// <summary>Tags spawned objects so input raycasts can map a collider back to a server entity.</summary>
    public sealed class EntityHandle : MonoBehaviour
    {
        public long EntityId { get; set; }
        public EntityKind Kind { get; set; }
    }

    /// <summary>
    /// Mirrors <see cref="WorldState"/> entities as GameObjects. Prefabs are looked up by definition id under
    /// Resources/Prefabs/&lt;Kind&gt;/ so artists can drop in low-poly models one by one; missing ones get a primitive.
    /// </summary>
    public sealed class EntityPresenter : MonoBehaviour
    {
        private const string PrefabRoot = "Prefabs/";
        private const float DeadTiltDegrees = 90f;
        private const float RadiansToDegrees = 57.29578f;

        private static readonly Dictionary<EntityKind, Color> PlaceholderColors = new Dictionary<EntityKind, Color>
        {
            { EntityKind.Player, new Color(0.25f, 0.55f, 0.95f) },
            { EntityKind.Npc, new Color(0.95f, 0.8f, 0.25f) },
            { EntityKind.Mob, new Color(0.85f, 0.25f, 0.2f) },
            { EntityKind.GatheringNode, new Color(0.3f, 0.75f, 0.35f) },
            { EntityKind.Portal, new Color(0.7f, 0.35f, 0.95f) },
        };

        private readonly Dictionary<long, GameObject> _views = new Dictionary<long, GameObject>();
        private WorldState? _world;

        public void Bind(WorldState world)
        {
            _world = world;
            world.EntitySpawned += Spawn;
            world.EntityDespawned += Despawn;
        }

        public Transform? Find(long entityId) => _views.TryGetValue(entityId, out GameObject? view) ? view.transform : null;

        private void LateUpdate()
        {
            if (_world == null)
            {
                return;
            }
            foreach (EntityView entity in _world.Entities)
            {
                if (!_views.TryGetValue(entity.Id, out GameObject? view))
                {
                    continue;
                }
                EntityPose pose = _world.PoseOf(entity);
                view.transform.position = new Vector3(pose.X, 0, pose.Z);
                float tilt = entity.IsDead ? DeadTiltDegrees : 0f;
                view.transform.rotation = Quaternion.Euler(tilt, pose.Heading * RadiansToDegrees, 0);
            }
        }

        private void Spawn(EntityView entity)
        {
            Despawn(entity.Id);
            GameObject view = CreateView(entity);
            view.name = $"{entity.Kind}:{entity.Name}#{entity.Id}";
            if (!view.TryGetComponent(out EntityHandle handle))
            {
                handle = view.AddComponent<EntityHandle>();
            }
            handle.EntityId = entity.Id;
            handle.Kind = entity.Kind;
            _views[entity.Id] = view;
        }

        private void Despawn(long entityId)
        {
            if (_views.Remove(entityId, out GameObject? view))
            {
                Destroy(view);
            }
        }

        private GameObject CreateView(EntityView entity)
        {
            GameObject? prefab = Resources.Load<GameObject>(PrefabRoot + entity.Kind + "/" + entity.DefinitionId);
            if (prefab != null)
            {
                return Instantiate(prefab, transform);
            }
            PrimitiveType shape = entity.Kind == EntityKind.GatheringNode || entity.Kind == EntityKind.Portal ? PrimitiveType.Cylinder : PrimitiveType.Capsule;
            GameObject placeholder = GameObject.CreatePrimitive(shape);
            placeholder.transform.SetParent(transform, false);
            placeholder.GetComponent<Renderer>().material.color = PlaceholderColors[entity.Kind];
            return placeholder;
        }
    }
}
