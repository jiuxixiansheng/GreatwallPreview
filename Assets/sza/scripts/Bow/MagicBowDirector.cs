using Mirror;
using UnityEngine;

namespace Sza.Bow
{
    public sealed class MagicBowDirector : MonoBehaviour
    {
        public NetworkMagicBow bowPrefab;
        [Header("世界坐标，可在场景视图移动这些点")]
        public Transform spawnPoint;
        public Transform aimPoint;
        public Transform dropPoint;
        [Min(0.1f), Tooltip("箭飞出多少米后触发 Love hit，不要求碰撞命中。")]
        public float hitDistance = 24f;
        private NetworkMagicBow instance;
        public NetworkMagicBow Instance => instance;

        public void Evaluate(float time)
        {
            if (Application.isPlaying && NetworkClient.active && !NetworkServer.active) return;
            if (!Application.isPlaying && instance != null && time < instance.DemoTime) ResetDemo();
            if (instance == null)
            {
                if (bowPrefab == null || spawnPoint == null || aimPoint == null) return;
                Vector3 forward = aimPoint.position - spawnPoint.position;
                if (forward.sqrMagnitude < 0.001f) return;
                Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
                instance = Instantiate(bowPrefab, spawnPoint.position, rotation);
                instance.name = "MagicBow · 射箭与拾取";
                if (!Application.isPlaying) instance.gameObject.hideFlags = HideFlags.HideAndDontSave;
                instance.Configure(spawnPoint.position, rotation, forward,
                    dropPoint != null ? dropPoint.position : spawnPoint.position, hitDistance);
                // Include a seeked pose/state in Mirror's initial spawn snapshot.
                instance.Evaluate(time);
                if (Application.isPlaying && NetworkServer.active) NetworkServer.Spawn(instance.gameObject);
            }
            instance.Evaluate(time);
        }

        public void ResetDemo()
        {
            if (instance == null) return;
            if (!Application.isPlaying) DestroyImmediate(instance.gameObject);
            else if (NetworkServer.active && instance.netId != 0) NetworkServer.Destroy(instance.gameObject);
            else Destroy(instance.gameObject);
            instance = null;
        }
        private void OnDisable() => ResetDemo();
        private void OnDestroy() => ResetDemo();

        private void OnDrawGizmosSelected()
        {
            if (spawnPoint == null || aimPoint == null) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(spawnPoint.position, 0.15f);
            Vector3 target = spawnPoint.position + (aimPoint.position - spawnPoint.position).normalized * hitDistance;
            Gizmos.DrawLine(spawnPoint.position, target);
            Gizmos.DrawWireSphere(target, 0.3f);
            if (dropPoint != null) { Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(dropPoint.position, 0.2f); }
        }
    }
}
