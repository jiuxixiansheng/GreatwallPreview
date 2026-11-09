using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Sza.Telescope
{
    /// <summary>Only the owning player's tracked hands may request this interaction.</summary>
    [DisallowMultipleComponent]
    public sealed class TelescopeVideoInteraction : MonoBehaviour
    {
        public TelescopeVideoManager manager;
        public Collider touchVolume;
        public Transform promptRoot;
        [Min(0.01f)] public float touchDistance = 0.045f;
        [Min(0f)] public float floatAmplitude = 0.018f;

        private readonly List<Transform> hands = new List<Transform>();
        private NetworkIdentity cachedPlayer;
        private Vector3 promptPosition;
        private bool touching;

        public bool IsAvailable => isActiveAndEnabled && gameObject.activeInHierarchy &&
                                   touchVolume != null && touchVolume.enabled;

        private void Awake()
        {
            if (touchVolume == null) touchVolume = GetComponent<Collider>();
            if (promptRoot != null) promptPosition = promptRoot.localPosition;
        }

        private void OnEnable() { touching = false; }

        private void LateUpdate()
        {
            if (manager == null) return;
            Camera camera = TelescopeVideoManager.LocalCamera;
            if (promptRoot != null)
            {
                promptRoot.gameObject.SetActive(manager.CanInteract);
                promptRoot.localPosition = promptPosition + transform.InverseTransformVector(Vector3.up *
                    (Mathf.Sin(Time.unscaledTime * 2f) * floatAmplitude));
                if (camera != null) promptRoot.rotation = camera.transform.rotation;
            }
            if (!IsAvailable) return;
            RefreshHands();
            bool inside = false;
            foreach (Transform hand in hands)
                if (hand != null && Vector3.Distance(hand.position,
                    touchVolume.ClosestPoint(hand.position)) <= touchDistance)
                    inside = true;
            // Re-entering is necessary to replay; keeping a hand in the volume never loops it.
            if (inside && !touching && manager.CanInteract) manager.RequestViewing();
            touching = inside;
        }

        private void RefreshHands()
        {
            var player = NetworkClient.localPlayer;
            if (player == cachedPlayer && hands.Count > 0) return;
            cachedPlayer = player;
            hands.Clear();
            if (player == null || !player.isLocalPlayer) return;
            foreach (var sync in player.GetComponentsInChildren<NetworkTransformBase>(true))
            {
                Transform target = sync.target;
                if (target == null || target == player.transform || target.GetComponent<Camera>() != null)
                    continue;
                string name = target.name.ToLowerInvariant();
                if ((name.Contains("controller") || name.Contains("hand")) && !hands.Contains(target))
                    hands.Add(target);
            }
        }

        public bool IsPlayerNearby(NetworkIdentity player, float distance)
        {
            if (!IsAvailable || player == null) return false;
            foreach (var sync in player.GetComponentsInChildren<NetworkTransformBase>(true))
                if (sync.target != null && Vector3.Distance(sync.target.position,
                    touchVolume.ClosestPoint(sync.target.position)) <= distance) return true;
            var camera = player.GetComponentInChildren<Camera>(true);
            return camera != null && Vector3.Distance(camera.transform.position,
                touchVolume.ClosestPoint(camera.transform.position)) <= distance;
        }

        [ContextMenu("测试触碰望远镜（运行时）")]
        public void TestTouch()
        {
            if (Application.isPlaying && manager != null) manager.RequestViewing();
        }
    }
}
