using System.Collections.Generic;
using Mirror;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Sza.Bow
{
    /// <summary>Lazy, mesh-accurate arrow queries in a separate physics world. No player colliders or layer matrix changes.</summary>
    public sealed class MagicBowHitGeometry : MonoBehaviour
    {
        private sealed class Surface
        {
            public MeshFilter filter;
            public MeshRenderer renderer;
            public MeshCollider proxy;
        }
        private sealed class Node
        {
            public Transform transform;
            public int updated;
        }
        private static MagicBowHitGeometry instance;
        private Scene queryScene;
        private PhysicsScene physics;
        private Transform queryRoot;
        private readonly Dictionary<MeshFilter, Surface> cache = new Dictionary<MeshFilter, Surface>();
        private readonly List<Surface> surfaces = new List<Surface>();
        private readonly List<MeshFilter> expired = new List<MeshFilter>();
        private readonly Dictionary<Transform, Node> nodes = new Dictionary<Transform, Node>();
        private readonly List<MeshCollider> enabledProxies = new List<MeshCollider>();
        private readonly List<MeshCollider> previousProxies = new List<MeshCollider>();
        private NativeArray<RaycastCommand> commands;
        private NativeArray<RaycastHit> results;
        private float nextRefresh;
        private int revision;
        public int CachedMeshCount => cache.Count;
        public int ActiveProxyCount => enabledProxies.Count;
        public PhysicsScene QueryPhysics => physics;

        public static void Invalidate()
        {
            if (instance != null) instance.nextRefresh = 0f;
        }
        private static MagicBowHitGeometry Get()
        {
            if (instance != null) return instance;
            var go = new GameObject("Magic Bow · Arrow Geometry Queries");
            return go.AddComponent<MagicBowHitGeometry>();
        }
        private void Awake()
        {
            instance = this;
            queryScene = SceneManager.CreateScene("__MagicBowPhysics_" + GetInstanceID(),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            physics = queryScene.GetPhysicsScene();
            var root = new GameObject("Arrow-only mesh proxies");
            SceneManager.MoveGameObjectToScene(root, queryScene);
            queryRoot = root.transform;
            commands = new NativeArray<RaycastCommand>(1, Allocator.Persistent);
            results = new NativeArray<RaycastHit>(1, Allocator.Persistent);
            SceneManager.sceneLoaded += SceneLoaded;
        }
        private void SceneLoaded(Scene scene, LoadSceneMode mode) => nextRefresh = 0f;

        public static bool Cast(NetworkMagicBowWeapon weapon, NetworkIdentity shooter, Vector3 origin, Vector3 direction,
            float distance, out RaycastHit hit)
        {
            hit = default;
            if (!Application.isPlaying || distance <= 0f) return false;
            return Get().Query(weapon, shooter, origin, direction, distance, out hit);
        }
        private void Refresh()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.5f;
            surfaces.Clear();
            expired.Clear();
            foreach (var pair in cache) if (pair.Key == null) expired.Add(pair.Key);
            foreach (var filter in expired) cache.Remove(filter);
            foreach (var filter in FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (filter.gameObject.scene == queryScene || filter.sharedMesh == null) continue;
                if (filter.GetComponentInParent<MagicBowProjectile>() != null ||
                    filter.GetComponentInParent<NetworkMagicBow>() != null) continue;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null) continue;
                Surface surface;
                if (!cache.TryGetValue(filter, out surface))
                {
                    surface = new Surface { filter = filter, renderer = renderer };
                    cache.Add(filter, surface);
                }
                surfaces.Add(surface);
            }
        }
        private bool Query(NetworkMagicBowWeapon weapon, NetworkIdentity shooter, Vector3 origin, Vector3 direction,
            float distance, out RaycastHit hit)
        {
            Refresh();
            previousProxies.Clear(); previousProxies.AddRange(enabledProxies);
            enabledProxies.Clear();
            revision++;
            Ray ray = new Ray(origin, direction);
            foreach (var surface in surfaces)
            {
                var renderer = surface.renderer;
                if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy ||
                    (weapon.hitLayers.value & 1 << renderer.gameObject.layer) == 0 || renderer.transform.IsChildOf(weapon.transform) ||
                    shooter != null && renderer.transform.IsChildOf(shooter.transform)) continue;
                Bounds bounds = renderer.bounds;
                bounds.Expand(weapon.hitRadius * 2f);
                float entry;
                if (!bounds.Contains(origin) && (!bounds.IntersectRay(ray, out entry) || entry > distance)) continue;
                if (!IsPhysicalSurface(renderer)) continue;
                var scale = renderer.transform.lossyScale;
                if (Mathf.Abs(scale.x * scale.y * scale.z) < 0.000000001f) continue;
                var node = SyncNode(renderer.transform);
                if (surface.proxy == null)
                {
                    surface.proxy = node.gameObject.AddComponent<MeshCollider>();
                    surface.proxy.convex = false;
                }
                if (surface.proxy.sharedMesh != surface.filter.sharedMesh) surface.proxy.sharedMesh = surface.filter.sharedMesh;
                surface.proxy.gameObject.layer = renderer.gameObject.layer;
                if (!surface.proxy.enabled) surface.proxy.enabled = true;
                enabledProxies.Add(surface.proxy);
            }
            foreach (var collider in previousProxies)
                if (collider != null && !enabledProxies.Contains(collider)) collider.enabled = false;
            hit = default;
            if (enabledProxies.Count == 0) return false;
            // Only this empty physics world is advanced, to synchronize the static proxy transforms.
            physics.Simulate(Time.fixedDeltaTime);
            bool found = physics.SphereCast(origin, weapon.hitRadius, direction, out hit, distance,
                weapon.hitLayers, QueryTriggerInteraction.Ignore);
            // Per-query backface detection also catches single-sided and mirrored walls, without changing global physics flags.
            var parameters = new QueryParameters { layerMask = weapon.hitLayers, hitMultipleFaces = false,
                hitTriggers = QueryTriggerInteraction.Ignore, hitBackfaces = true };
            commands[0] = new RaycastCommand(physics, origin, direction, parameters, distance);
            results[0] = default;
            RaycastCommand.ScheduleBatch(commands, results, 1, 1, default(JobHandle)).Complete();
            var center = results[0];
            if (center.collider != null && (!found || center.distance < hit.distance)) { hit = center; found = true; }
            return found;
        }

        private Transform SyncNode(Transform source)
        {
            Node node;
            if (!nodes.TryGetValue(source, out node))
            {
                node = new Node { transform = new GameObject(source.name).transform };
                SceneManager.MoveGameObjectToScene(node.transform.gameObject, queryScene);
                nodes.Add(source, node);
            }
            if (node.updated == revision) return node.transform;
            node.updated = revision;
            Transform parent = source.parent != null ? SyncNode(source.parent) : queryRoot;
            if (node.transform.parent != parent) node.transform.SetParent(parent, false);
            if (node.transform.localPosition != source.localPosition) node.transform.localPosition = source.localPosition;
            if (node.transform.localRotation != source.localRotation) node.transform.localRotation = source.localRotation;
            if (node.transform.localScale != source.localScale) node.transform.localScale = source.localScale;
            return node.transform;
        }

        public static bool IsVisualOnly(Renderer renderer)
        {
            if (renderer.GetComponent<TextMesh>() != null || renderer.GetComponentInParent<Canvas>() != null ||
                renderer.GetComponentInParent<Camera>() != null ||
                renderer.GetComponentInParent<Xcy.Boundary.PartBoundaryZone>() != null) return true;
            for (var t = renderer.transform; t != null; t = t.parent) if (t.CompareTag("EditorOnly")) return true;
            bool surface = false;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null) continue;
                string name = material.shader.name.ToLowerInvariant();
                if (name.Contains("sky") || name.Contains("panorama") || name.Contains("insideoutvideo") ||
                    name.Contains("holographic") || name.Contains("gridcage") || name.Contains("boundary") ||
                    name.Contains("particles") || name.StartsWith("ui/") || name.Contains("fog") || name.Contains("cloud")) continue;
                surface = true;
            }
            return !surface;
        }
        public static bool IsPhysicalSurface(MeshRenderer renderer)
        {
            var rule = MagicBowHitRule.Resolve(renderer.transform);
            if (rule == MagicBowHitRule.Response.Ignore) return false;
            if (renderer.GetComponentInParent<NetworkMagicBow>() != null ||
                renderer.GetComponentInParent<MagicBowProjectile>() != null ||
                renderer.GetComponentInParent<PlayerNetworkSetup>() != null ||
                renderer.GetComponentInParent<NavMeshAgent>() != null) return false;
            return rule == MagicBowHitRule.Response.Include || !IsVisualOnly(renderer);
        }
        public static bool AcceptCollider(Collider collider)
        {
            var rule = MagicBowHitRule.Resolve(collider.transform);
            if (rule == MagicBowHitRule.Response.Ignore) return false;
            if (rule == MagicBowHitRule.Response.Include) return true;
            if (collider.GetComponentInParent<Xcy.Boundary.PartBoundaryZone>() != null) return false;
            if (collider.GetComponentInParent<PlayerNetworkSetup>() != null) return !collider.isTrigger;
            var renderer = collider.GetComponent<Renderer>();
            if (renderer != null && IsVisualOnly(renderer)) return false;
            if (!collider.isTrigger) return true;
            foreach (var child in collider.GetComponentsInChildren<MeshRenderer>())
                if (child.enabled && IsPhysicalSurface(child)) return true;
            return false;
        }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            if (commands.IsCreated) commands.Dispose();
            if (results.IsCreated) results.Dispose();
            if (queryScene.IsValid() && queryScene.isLoaded) SceneManager.UnloadSceneAsync(queryScene);
            if (instance == this) instance = null;
        }
    }
}
