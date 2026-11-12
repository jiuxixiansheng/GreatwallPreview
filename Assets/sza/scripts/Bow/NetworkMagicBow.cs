using Mirror;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Sza.Bow
{
    /// <summary>Server owns the demonstration and physics; NetworkGrabbable owns hand poses.</summary>
    [RequireComponent(typeof(NetworkGrabbable))]
    public sealed class NetworkMagicBow : NetworkBehaviour
    {
        [Header("模型与动画")]
        public GameObject model;
        public AnimationClip drawAndFire;
        public Transform arrow;
        public Vector3 arrowDrawOffset = new Vector3(-0.72f, 0f, 0f);
        public Renderer[] bowRenderers;
        public Renderer arrowRenderer;
        public ParticleSystem appearanceEffect, hitEffect;
        public Transform pickupPrompt;

        [Header("演示节奏（秒）")]
        [Min(0.05f)] public float appearSeconds = 0.8f;
        [Min(0.05f)] public float drawSeconds = 1.2f;
        [Min(0.05f)] public float arrowRevealSeconds = 0.8f;
        [Min(0.05f)] public float releaseSeconds = 0.18f;
        [Min(0.1f)] public float arrowSpeed = 18f;
        [Min(0.05f)] public float dropSeconds = 0.5f;
        [Header("拾取提示")]
        [ColorUsage(false, true)] public Color highlightColor = new Color(4f, 3.5f, 0.05f);
        [Min(0.1f)] public float breathePeriod = 1.8f;

        [SyncVar] private float demoTime;
        [SyncVar] private Vector3 origin, direction, dropPosition;
        [SyncVar] private Quaternion orientation;
        [SyncVar] private float flightDistance = 24f;
        [SyncVar] private bool dropped, everPicked;
        private NetworkGrabbable networkGrab;
        private XRGrabInteractable grab;
        private Rigidbody body;
        private MaterialPropertyBlock properties;
        private Vector3 arrowRest;
        private Quaternion arrowRotation;
        private bool initialized;
        private bool cached;

        public bool Dropped => dropped;
        public bool EverPicked => everPicked;
        public float DemoTime => demoTime;
        public float LaunchTime => appearSeconds + drawSeconds + arrowRevealSeconds;
        public float HitTime => LaunchTime + flightDistance / Mathf.Max(0.1f, arrowSpeed);
        public float DropTime => HitTime + dropSeconds;

        private void Awake()
        {
            Cache();
        }

        private void Cache()
        {
            if (cached) return;
            cached = true;
            networkGrab = GetComponent<NetworkGrabbable>();
            grab = GetComponent<XRGrabInteractable>();
            body = GetComponent<Rigidbody>();
            properties = new MaterialPropertyBlock();
            if (arrow != null) { arrowRest = arrow.localPosition; arrowRotation = arrow.localRotation; }
            networkGrab.ServerGrabbed += MarkPicked;
            grab.selectEntered.AddListener(OnLocalGrab);
            foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            SetEffect(appearanceEffect, -1f);
            SetEffect(hitEffect, -1f);
        }

        public override void OnStartClient() { initialized = true; Render(); }

        public void Configure(Vector3 position, Quaternion rotation, Vector3 aim, Vector3 drop, float distance)
        {
            if (NetworkClient.active && !NetworkServer.active) return;
            Cache();
            origin = position;
            orientation = rotation;
            direction = aim.normalized;
            dropPosition = drop;
            flightDistance = Mathf.Max(0.1f, distance);
            transform.SetPositionAndRotation(position, rotation);
            networkGrab.SetScriptedMotion(true);
            initialized = true;
            Evaluate(0f);
        }

        public void Evaluate(float seconds)
        {
            if (Application.isPlaying && NetworkClient.active && !NetworkServer.active) return;
            demoTime = Mathf.Max(0f, seconds);
            if (!dropped)
            {
                float fall = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HitTime, DropTime, demoTime));
                transform.SetPositionAndRotation(Vector3.Lerp(origin, dropPosition, fall),
                    Quaternion.Slerp(orientation, orientation * Quaternion.Euler(80f, 0f, 0f), fall));
                if (demoTime >= DropTime)
                {
                    dropped = true;
                    networkGrab.SetScriptedMotion(false);
                    if (Application.isPlaying) body.WakeUp();
                }
            }
            Render();
        }

        private void OnLocalGrab(SelectEnterEventArgs args)
        {
            // A remote prediction can still be rejected. Only offline selection latches here.
            if (!NetworkClient.active && !NetworkServer.active) MarkPicked();
        }
        private void MarkPicked() { everPicked = true; }

        private void LateUpdate()
        {
            if (initialized) Render();
        }

        private void Render()
        {
            float drawStart = appearSeconds;
            float revealStart = drawStart + drawSeconds;
            float releaseStart = revealStart + arrowRevealSeconds;
            float draw = Mathf.Clamp01((demoTime - drawStart) / Mathf.Max(0.05f, drawSeconds));
            float animationTime = demoTime < releaseStart ? draw :
                Mathf.Lerp(1f, 1.35f, Mathf.Clamp01((demoTime - releaseStart) / Mathf.Max(0.05f, releaseSeconds)));
            if (drawAndFire != null) drawAndFire.SampleAnimation(model, animationTime);
            float reveal = Mathf.Clamp01((demoTime - revealStart) / Mathf.Max(0.05f, arrowRevealSeconds));
            if (arrow != null)
            {
                arrow.gameObject.SetActive(demoTime >= revealStart && demoTime < HitTime);
                if (demoTime < LaunchTime)
                {
                    arrow.localPosition = arrowRest + arrowDrawOffset * draw;
                    arrow.localRotation = arrowRotation;
                }
                else
                {
                    float travel = Mathf.Clamp((demoTime - LaunchTime) * arrowSpeed, 0f, flightDistance);
                    arrow.SetPositionAndRotation(origin + orientation * (arrowRest + arrowDrawOffset) + direction * travel,
                        orientation * arrowRotation);
                }
            }
            bool held = networkGrab.IsHeld || (!NetworkClient.active && !NetworkServer.active && grab.isSelected);
            double clock = NetworkClient.active || NetworkServer.active ? NetworkTime.time : Time.unscaledTimeAsDouble;
            float pulse = 0.55f + 0.45f * Mathf.Sin((float)(clock % breathePeriod / breathePeriod) * Mathf.PI * 2f);
            foreach (var renderer in bowRenderers)
                SetMaterial(renderer, Mathf.Clamp01(demoTime / Mathf.Max(0.05f, appearSeconds)), dropped && !held ? pulse : 0f);
            SetMaterial(arrowRenderer, reveal, 0f);
            if (appearanceEffect != null) appearanceEffect.transform.position = origin;
            if (hitEffect != null) hitEffect.transform.position = origin + orientation * (arrowRest + arrowDrawOffset) + direction * flightDistance;
            SetEffect(appearanceEffect, demoTime);
            SetEffect(hitEffect, demoTime - HitTime);
            if (pickupPrompt != null)
            {
                pickupPrompt.gameObject.SetActive(dropped && !everPicked && !held);
                pickupPrompt.position = transform.position + Vector3.up * (0.65f + 0.015f * Mathf.Sin(Time.unscaledTime * 2f));
                var camera = Sza.Telescope.TelescopeVideoManager.LocalCamera;
                if (camera == null && !NetworkClient.active) camera = Camera.main;
                if (camera != null) pickupPrompt.rotation = camera.transform.rotation;
            }
        }

        private void SetMaterial(Renderer renderer, float reveal, float highlight)
        {
            if (renderer == null) return;
            renderer.GetPropertyBlock(properties);
            properties.SetFloat("_Reveal", reveal);
            properties.SetFloat("_Highlight", highlight);
            properties.SetColor("_HighlightColor", highlightColor);
            renderer.SetPropertyBlock(properties);
        }

        private static void SetEffect(ParticleSystem effect, float age)
        {
            if (effect == null) return;
            bool visible = age >= 0f && age < 5f;
            effect.gameObject.SetActive(visible);
            if (visible) effect.Simulate(age, true, true, false);
        }

        private void OnDestroy()
        {
            if (networkGrab != null) networkGrab.ServerGrabbed -= MarkPicked;
            if (grab != null) grab.selectEntered.RemoveListener(OnLocalGrab);
        }
    }
}
