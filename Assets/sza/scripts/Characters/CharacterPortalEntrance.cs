using System.Collections.Generic;
using UnityEngine;

namespace Sza.Characters
{
    /// <summary>The blue local Z side of the portal is visible; the rear half-space is hidden.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class CharacterPortalEntrance : MonoBehaviour
    {
        [Tooltip("仅裁切这个角色的 Renderer，不向场景深度缓冲写入遮挡盒。")]
        public TimelineCharacterActor actor;
        [Tooltip("传送门中心。蓝色 Z 轴朝向角色走出的方向。")]
        public Transform portalPlane;
        public GameObject effectRoot;
        [Tooltip("整体特效大小，开关门的缩放在此基础上变化。")]
        public Vector3 effectScale = Vector3.one * 1.35f;
        [Tooltip("选中时显示的门后隐藏区域深度，仅用于编辑指引。"), Min(0.1f)]
        public float hiddenRegionDepth = 3f;

        private static readonly int ClipEnabled = Shader.PropertyToID("_SzaPortalClipEnabled");
        private static readonly int PlaneWS = Shader.PropertyToID("_SzaPortalPlaneWS");
        private Renderer[] renderers;
        private ParticleSystem[] particles;
        private MaterialPropertyBlock properties;
        private readonly List<SlotState> savedSlots = new List<SlotState>();
        private bool active;
        private double previousTime = -1d;

        private sealed class SlotState
        {
            public Renderer renderer;
            public int index;
            public MaterialPropertyBlock original;
        }

        public bool IsRevealing => active;
        public Vector4 WorldPlane
        {
            get
            {
                var plane = portalPlane != null ? portalPlane : transform;
                Vector3 n = plane.forward.normalized;
                return new Vector4(n.x, n.y, n.z, -Vector3.Dot(n, plane.position));
            }
        }

        public void Evaluate(double localTime, double duration, float openingSeconds, float closingSeconds)
        {
            if (actor == null || effectRoot == null || duration <= 0d) { Release(); return; }
            if (renderers == null) renderers = actor.GetComponentsInChildren<Renderer>(true);
            if (particles == null) particles = effectRoot.GetComponentsInChildren<ParticleSystem>(true);
            bool restart = !active || !effectRoot.activeSelf || localTime < previousTime || localTime - previousTime > 0.25d;
            if (!active) CaptureSlots();
            active = true;
            if (!effectRoot.activeSelf) effectRoot.SetActive(true);
            float t = Mathf.Max(0f, (float)localTime);
            float open = openingSeconds > 0f ? Mathf.Clamp01(t / openingSeconds) : 1f;
            float close = closingSeconds > 0f ? Mathf.Clamp01((float)(duration - localTime) / closingSeconds) : 1f;
            float envelope = Mathf.SmoothStep(0f, 1f, Mathf.Min(open, close));
            effectRoot.transform.localScale = effectScale * envelope;
            float step = restart ? t : Mathf.Max(0f, (float)(localTime - previousTime));
            if (restart || step > 0f)
                foreach (var system in particles)
                {
                    // Manual simulation freezes with Timeline, and reconstructs seeks/late joins.
                    system.Simulate(step, false, restart, false);
                    system.Pause(false);
                }
            previousTime = localTime;
            ApplyClipping(true);
        }

        private void LateUpdate()
        {
            if (active) ApplyClipping(true);
        }

        private void ApplyClipping(bool enabled)
        {
            if (renderers == null) return;
            if (properties == null) properties = new MaterialPropertyBlock();
            Vector4 plane = WorldPlane;
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    if (materials[slot] == null || !materials[slot].HasProperty(ClipEnabled)) continue;
                    renderer.GetPropertyBlock(properties, slot);
                    properties.SetFloat(ClipEnabled, enabled ? 1f : 0f);
                    properties.SetVector(PlaneWS, plane);
                    renderer.SetPropertyBlock(properties, slot);
                    properties.Clear();
                }
            }
        }

        private void CaptureSlots()
        {
            savedSlots.Clear();
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    if (materials[slot] == null || !materials[slot].HasProperty(ClipEnabled)) continue;
                    var original = new MaterialPropertyBlock(); renderer.GetPropertyBlock(original, slot);
                    savedSlots.Add(new SlotState { renderer = renderer, index = slot, original = original.isEmpty ? null : original });
                }
            }
        }

        public void Release()
        {
            bool wasActive = active;
            // Returning empty slots to null restores the ordinary SRP Batcher path.
            foreach (var slot in savedSlots)
                if (slot.renderer != null) slot.renderer.SetPropertyBlock(slot.original, slot.index);
            savedSlots.Clear();
            active = false;
            previousTime = -1d;
            if (wasActive && particles != null)
                foreach (var system in particles)
                    if (system != null) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (effectRoot != null && effectRoot.activeSelf) effectRoot.SetActive(false);
        }

        private void OnDisable() => Release();
        private void OnDestroy() => Release();

        private void OnDrawGizmosSelected()
        {
            var plane = portalPlane != null ? portalPlane : transform;
            Gizmos.matrix = Matrix4x4.TRS(plane.position, plane.rotation, Vector3.one);
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(2f, 2f, 0.015f));
            Gizmos.DrawRay(Vector3.zero, Vector3.forward);
            Gizmos.color = new Color(1f, 0.35f, 0.15f, 0.65f);
            Gizmos.DrawWireCube(Vector3.back * hiddenRegionDepth * 0.5f, new Vector3(2f, 2f, hiddenRegionDepth));
        }
    }
}
