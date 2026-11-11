using Greatwall.VRAnimation.Timeline;
using Mirror;
using UnityEngine;

namespace Sza.Prologue
{
    [DisallowMultipleComponent]
    public sealed class PrologueWallBreathingGlow : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public Renderer targetRenderer;
        public StoryTimelineManager story;
        [Min(0)] public int materialIndex;
        [Header("石壁呼吸发光")]
        [ColorUsage(false, true)] public Color glowColor = new Color(1f, 0.65f, 0.16f, 1f);
        [Tooltip("一次由暗到亮再变暗的时长，单位为秒。")]
        [Min(0.1f)] public float cycleSeconds = 2.8f;
        [Min(0f)] public float minimumIntensity = 0.08f;
        [Min(0f)] public float maximumIntensity = 2f;

        public float CurrentIntensity { get; private set; }

        private MaterialPropertyBlock block;
        private Color originalEmission;
        private bool hasOriginalOverride;
        private MaterialPropertyBlock originalBlock;
        private Renderer boundRenderer;
        private int boundIndex;
        private bool applied;

        private void OnEnable()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
            if (targetRenderer == null) return;
            var materials = targetRenderer.sharedMaterials;
            if (materialIndex < 0 || materialIndex >= materials.Length ||
                materials[materialIndex] == null || !materials[materialIndex].HasProperty(EmissionColorId)) return;

            boundRenderer = targetRenderer;
            boundIndex = materialIndex;
            block = new MaterialPropertyBlock();
            originalBlock = new MaterialPropertyBlock();
            boundRenderer.GetPropertyBlock(originalBlock, boundIndex);
            hasOriginalOverride = originalBlock.HasColor(EmissionColorId);
            originalEmission = hasOriginalOverride ? originalBlock.GetColor(EmissionColorId) :
                materials[boundIndex].GetColor(EmissionColorId);
        }

        private void Update()
        {
            if (boundRenderer == null) return;
            if (story != null && (story.CurrentChapterIndex != 0 || story.IsTransitionPending))
            {
                RestoreEmission();
                return;
            }

            // Every peer uses the shared clock, without sending per-frame material updates.
            double time = NetworkServer.active || NetworkClient.active ? NetworkTime.time : Time.unscaledTimeAsDouble;
            double phase = time % Mathf.Max(0.1f, cycleSeconds) / Mathf.Max(0.1f, cycleSeconds);
            float pulse = 0.5f - 0.5f * Mathf.Cos((float)phase * Mathf.PI * 2f);
            float low = Mathf.Max(0f, minimumIntensity);
            CurrentIntensity = Mathf.Lerp(low, Mathf.Max(low, maximumIntensity), pulse);
            boundRenderer.GetPropertyBlock(block, boundIndex);
            block.SetColor(EmissionColorId, glowColor * CurrentIntensity);
            boundRenderer.SetPropertyBlock(block, boundIndex);
            applied = true;
        }

        private void RestoreEmission()
        {
            CurrentIntensity = 0f;
            if (!applied || boundRenderer == null) return;
            // Preserve any other per-renderer properties written while the pulse was running.
            boundRenderer.GetPropertyBlock(block, boundIndex);
            block.SetColor(EmissionColorId, originalEmission);
            boundRenderer.SetPropertyBlock(block, boundIndex);
            applied = false;
        }

        private void OnDisable() => RestoreEmission();
    }
}
