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

        [Header("望远镜上下浮动")]
        public bool floatTelescope = true;
        [Tooltip("相对摆放位置向上、向下各偏移多少米，不受物体和父物体缩放影响。")]
        [Min(0f)] public float telescopeFloatAmplitude = 0.06f;
        [Tooltip("完成一次上下浮动所需秒数；剧情暂停时仍会继续浮动。")]
        [Min(0.1f)] public float telescopeFloatPeriod = 3.2f;

        [Header("提示 UI 浮动")]
        [Min(0f)] public float floatAmplitude = 0.018f;

        private readonly List<Transform> hands = new List<Transform>();
        private NetworkIdentity cachedPlayer;
        private Vector3 promptPosition;
        private bool touching;
        private Vector3 telescopeRestPosition;
        private bool floating;

        public bool IsAvailable => isActiveAndEnabled && gameObject.activeInHierarchy &&
                                   touchVolume != null && touchVolume.enabled;

        private void Awake()
        {
            if (touchVolume == null) touchVolume = GetComponent<Collider>();
            if (promptRoot != null) promptPosition = promptRoot.localPosition;
        }

        private void OnEnable()
        {
            touching = false;
            floating = false;
            RefreshPrompt();
        }

        private void OnDisable()
        {
            RestoreTelescopePosition();
            if (promptRoot != null) promptRoot.gameObject.SetActive(false);
            if (manager != null) manager.ResetInteractionGate();
        }

        public void RefreshPrompt()
        {
            if (promptRoot != null) promptRoot.gameObject.SetActive(manager != null && manager.CanInteract);
        }

        private void LateUpdate()
        {
            UpdateTelescopeFloat();
            if (manager == null) return;
            Camera camera = TelescopeVideoManager.LocalCamera;
            if (promptRoot != null)
            {
                RefreshPrompt();
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

        private void UpdateTelescopeFloat()
        {
            if (!floatTelescope || telescopeFloatAmplitude <= 0f)
            {
                RestoreTelescopePosition();
                return;
            }
            if (!floating)
            {
                telescopeRestPosition = transform.localPosition;
                floating = true;
            }

            double clock = NetworkServer.active || NetworkClient.active ? NetworkTime.time : Time.unscaledTimeAsDouble;
            double period = Mathf.Max(0.1f, telescopeFloatPeriod);
            float offset = Mathf.Sin((float)(clock % period / period) * Mathf.PI * 2f) * telescopeFloatAmplitude;
            Vector3 worldOffset = Vector3.up * offset;
            transform.localPosition = telescopeRestPosition + (transform.parent == null ? worldOffset :
                transform.parent.InverseTransformVector(worldOffset));
            // ClosestPoint is queried below, in this frame, against the moving trigger.
            if (touchVolume != null && touchVolume.enabled) Physics.SyncTransforms();
        }

        private void RestoreTelescopePosition()
        {
            if (!floating) return;
            transform.localPosition = telescopeRestPosition;
            floating = false;
            if (touchVolume != null && touchVolume.enabled) Physics.SyncTransforms();
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
