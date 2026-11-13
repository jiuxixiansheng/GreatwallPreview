using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace Sza.Bow
{
    /// <summary>A server sweeps the arrow tip between physics steps; peers display the same shot clock.</summary>
    public sealed class MagicBowProjectile : MonoBehaviour
    {
        private NetworkMagicBowWeapon weapon;
        private uint shot;
        private Vector3 start, direction, tipOffset, tailOffset;
        private double started, revealed;
        private float traveled;
        private bool authority, completed;
        private NetworkIdentity shooter;
        private Renderer visual;
        private MaterialPropertyBlock material;
        private NavMeshAgent[] npcs;

        public void Initialize(NetworkMagicBowWeapon owner, uint id, Vector3 forward, double clock,
            double revealClock, NetworkIdentity player, bool simulate)
        {
            weapon = owner; shot = id; start = transform.position; direction = forward.normalized;
            started = clock; revealed = revealClock; shooter = player; authority = simulate;
            visual = GetComponent<Renderer>(); material = new MaterialPropertyBlock();
            var mesh = GetComponent<MeshFilter>().sharedMesh;
            tipOffset = transform.TransformVector(new Vector3(mesh.bounds.max.x, mesh.bounds.center.y, mesh.bounds.center.z));
            tailOffset = transform.TransformVector(new Vector3(mesh.bounds.min.x, mesh.bounds.center.y, mesh.bounds.center.z));
            npcs = authority ? FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None) : null;
            if (authority) MagicBowHitGeometry.Invalidate();
            Render();
        }
        private void Update()
        {
            if (completed || weapon == null) return;
            if (!authority)
            {
                float age = Mathf.Max(0f, (float)(NetworkMagicBowWeapon.Clock - started));
                transform.position = start + direction * Mathf.Min(age * weapon.projectileSpeed, weapon.maximumShotDistance);
                // Wait for the authoritative impact instead of displaying an arrow stuck in the air.
                visual.enabled = age <= weapon.maximumShotDistance / weapon.projectileSpeed + 0.1f;
            }
            Render();
        }
        private void Render()
        {
            visual.GetPropertyBlock(material);
            material.SetFloat("_Reveal", Mathf.Clamp01((float)(NetworkMagicBowWeapon.Clock - revealed) /
                Mathf.Max(0.02f, weapon.arrowDissolveSeconds)));
            material.SetFloat("_Highlight", 0f); visual.SetPropertyBlock(material);
        }
        private void FixedUpdate()
        {
            if (!authority || completed || weapon == null) return;
            float step = Mathf.Min(weapon.projectileSpeed * Time.fixedDeltaTime, weapon.maximumShotDistance - traveled);
            Vector3 tip = start + direction * traveled + tipOffset;
            Vector3 point, normal;
            if (traveled == 0f && Sweep(start + tailOffset, Vector3.Distance(tailOffset, tipOffset), out point, out normal))
            { Hit(point, normal); return; }
            if (Sweep(tip, Mathf.Max(0f, step), out point, out normal)) { Hit(point, normal); return; }
            traveled += step; transform.position = start + direction * traveled;
            if (traveled >= weapon.maximumShotDistance - 0.0001f) Hit(transform.position + tipOffset, -direction);
        }
        private bool Ignore(Collider collider) => collider == null || collider.transform.IsChildOf(weapon.transform) ||
            shooter != null && collider.GetComponentInParent<NetworkIdentity>() == shooter ||
            !MagicBowHitGeometry.AcceptCollider(collider);

        private bool Sweep(Vector3 tip, float step, out Vector3 point, out Vector3 normal)
        {
            float nearest = step + 1f;
            point = tip + direction * step; normal = -direction;
            if (traveled == 0f)
                foreach (var collider in Physics.OverlapSphere(tip, weapon.hitRadius, weapon.hitLayers, QueryTriggerInteraction.Collide))
                    if (!Ignore(collider)) { nearest = 0f; point = tip; break; }
            foreach (var hit in Physics.SphereCastAll(tip, weapon.hitRadius, direction, step, weapon.hitLayers, QueryTriggerInteraction.Collide))
                if (!Ignore(hit.collider) && hit.distance < nearest)
                { nearest = hit.distance; point = hit.point; normal = hit.normal; }
            RaycastHit geometryHit;
            if (MagicBowHitGeometry.Cast(weapon, shooter, tip, direction, step, out geometryHit) && geometryHit.distance < nearest)
            { nearest = geometryHit.distance; point = geometryHit.point; normal = geometryHit.normal; }
            // NPC navigation capsules are tested analytically; no new colliders disturb scene interactions.
            foreach (var agent in npcs)
            {
                if (agent == null || !agent.gameObject.activeInHierarchy ||
                    (weapon.hitLayers.value & 1 << agent.gameObject.layer) == 0 ||
                    MagicBowHitRule.Resolve(agent.transform) == MagicBowHitRule.Response.Ignore) continue;
                bool visible = false;
                foreach (var renderer in agent.GetComponentsInChildren<Renderer>())
                    if (renderer.enabled && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) { visible = true; break; }
                if (!visible) continue;
                var scale = agent.transform.lossyScale;
                float radius = agent.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float height = Mathf.Max(radius * 2f, agent.height * Mathf.Abs(scale.y));
                Vector3 bottom = agent.transform.position + Vector3.up * (agent.baseOffset * scale.y + radius);
                Vector3 top = bottom + Vector3.up * (height - radius * 2f);
                float distance;
                if (MagicBowCollision.RayCapsule(tip, direction, bottom, top, radius + weapon.hitRadius, out distance) &&
                    distance <= step && distance < nearest)
                { nearest = distance; point = tip + direction * distance; normal = -direction; }
            }
            // PlayerNetworkSetup intentionally disables remote colliders. Visible avatar bounds remain hittable.
            if (NetworkServer.active)
                foreach (var connection in NetworkServer.connections.Values)
                {
                    var player = connection.identity;
                    if (player == null || player == shooter || !player.gameObject.activeInHierarchy) continue;
                    foreach (var renderer in player.GetComponentsInChildren<Renderer>())
                    {
                        if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer) || !renderer.enabled ||
                            renderer.name == "Pinch_Pointer_LOD0" ||
                            (weapon.hitLayers.value & 1 << renderer.gameObject.layer) == 0 ||
                            MagicBowHitRule.Resolve(renderer.transform) == MagicBowHitRule.Response.Ignore ||
                            renderer.GetComponentInParent<Canvas>() != null) continue;
                        Bounds bounds = renderer.bounds; bounds.Expand(weapon.hitRadius * 2f);
                        float distance = 0f;
                        bool intersects = bounds.Contains(tip) || bounds.IntersectRay(new Ray(tip, direction), out distance);
                        if (intersects && distance <= step && distance < nearest)
                        { nearest = distance; point = tip + direction * distance; normal = -direction; }
                    }
                }
            return nearest <= step;
        }
        private void Hit(Vector3 point, Vector3 normal)
        {
            if (completed) return;
            completed = true;
            weapon.ProjectileHit(shot, point, normal);
        }
    }
}
