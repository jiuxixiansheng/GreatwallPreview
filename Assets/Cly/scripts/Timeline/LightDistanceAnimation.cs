using UnityEngine;

namespace Greatwall.VRAnimation.Timeline
{
    [DisallowMultipleComponent]
    public sealed class LightDistanceAnimation : MonoBehaviour
    {
        private static readonly int DistanceId = Shader.PropertyToID("_distance");

        [SerializeField, Min(0f)] private float duration = 1f;
        [Tooltip("灯完全显现后立即启用的火焰物体。")]
        [SerializeField] private GameObject flame;

        private Renderer targetRenderer;
        private MaterialPropertyBlock properties;
        private float elapsed;
        private float originalDistance;
        private bool hasOverride;
        private bool animating;

        // 由 Timeline 的 Signal Receiver 调用。
        public void Play()
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;

            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
            Material material = targetRenderer != null ? targetRenderer.sharedMaterial : null;
            if (material == null || !material.HasProperty(DistanceId))
            {
                Debug.LogWarning("请把 Light Distance Animation 挂到使用 _distance 材质的 Renderer 物体上。", this);
                return;
            }

            if (properties == null) properties = new MaterialPropertyBlock();
            if (!hasOverride)
            {
                targetRenderer.GetPropertyBlock(properties);
                originalDistance = properties.HasFloat(DistanceId)
                    ? properties.GetFloat(DistanceId) : material.GetFloat(DistanceId);
                hasOverride = true;
            }

            elapsed = 0f;
            if (flame != null) flame.SetActive(false);
            animating = duration > 0f;
            if (animating) SetDistance(1f);
            else FinishReveal();
        }

        private void Update()
        {
            if (!animating) return;

            elapsed += Time.deltaTime;
            float progress = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
            if (progress >= 1f) FinishReveal();
            else SetDistance(1f - progress);
        }

        private void FinishReveal()
        {
            SetDistance(0f);
            animating = false;
            if (flame != null) flame.SetActive(true);
        }

        private void OnDisable()
        {
            animating = false;
            if (flame != null) flame.SetActive(false);
            if (hasOverride && targetRenderer != null) SetDistance(originalDistance);
            hasOverride = false;
        }

        private void SetDistance(float value)
        {
            targetRenderer.GetPropertyBlock(properties);
            properties.SetFloat(DistanceId, value);
            targetRenderer.SetPropertyBlock(properties);
        }
    }
}
