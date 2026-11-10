using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Xcy.Rendering
{
    /// <summary>Attach to the Part2 chapter root. Its activation selects a runtime fog preset.</summary>
    [DisallowMultipleComponent]
    public sealed class Part2OutdoorFog : MonoBehaviour
    {
        [Serializable]
        public struct FogParameters
        {
            public Color fogColor;
            [Range(0, .05f)] public float density;
            [Min(0)] public float startDistance;
            [Min(1)] public float maxDistance;
            public float baseHeight;
            [Range(0, .2f)] public float heightFalloff;
            [Range(0, 1)] public float skyStrength;
            [Range(0, 20)] public float horizonFalloff;
            [Range(0, 1)] public float maxOpacity;

            public static FogParameters Read(Material material)
            {
                return new FogParameters {
                    fogColor = material.GetColor("_FogColor"), density = material.GetFloat("_Density"),
                    startDistance = material.GetFloat("_StartDistance"), maxDistance = material.GetFloat("_MaxDistance"),
                    baseHeight = material.GetFloat("_BaseHeight"), heightFalloff = material.GetFloat("_HeightFalloff"),
                    skyStrength = material.GetFloat("_SkyStrength"), horizonFalloff = material.GetFloat("_HorizonFalloff"),
                    maxOpacity = material.GetFloat("_MaxOpacity")
                };
            }
            public void Apply(Material material, FogParameters from, float t)
            {
                material.SetColor("_FogColor", Color.Lerp(from.fogColor, fogColor, t));
                material.SetFloat("_Density", Mathf.Max(0, Mathf.Lerp(from.density, density, t)));
                material.SetFloat("_StartDistance", Mathf.Max(0, Mathf.Lerp(from.startDistance, startDistance, t)));
                material.SetFloat("_MaxDistance", Mathf.Max(1, Mathf.Lerp(from.maxDistance, maxDistance, t)));
                material.SetFloat("_BaseHeight", Mathf.Lerp(from.baseHeight, baseHeight, t));
                material.SetFloat("_HeightFalloff", Mathf.Max(0, Mathf.Lerp(from.heightFalloff, heightFalloff, t)));
                material.SetFloat("_SkyStrength", Mathf.Clamp01(Mathf.Lerp(from.skyStrength, skyStrength, t)));
                material.SetFloat("_HorizonFalloff", Mathf.Max(0, Mathf.Lerp(from.horizonFalloff, horizonFalloff, t)));
                material.SetFloat("_MaxOpacity", Mathf.Clamp01(Mathf.Lerp(from.maxOpacity, maxOpacity, t)));
            }
        }

        [Header("Current renderer fog feature")]
        public FullScreenPassRendererFeature fogFeature;
        [Header("Part2 fog values")]
        public FogParameters part2;
        [Tooltip("Seconds to blend from the default material values on entering Part2. Zero changes immediately.")]
        [Min(0)] public float transitionSeconds = 2f;
        private Material originalMaterial;
        private Material runtimeMaterial;
        private FogParameters initial;
        private float blend;
        public Material RuntimeMaterial => runtimeMaterial;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (fogFeature == null || fogFeature.passMaterial == null || fogFeature.passMaterial.shader.name != "Xcy/Outdoor Fog")
            {
                Debug.LogWarning("Part2OutdoorFog requires the renderer feature using Xcy/Outdoor Fog.", this);
                return;
            }
            originalMaterial = fogFeature.passMaterial;
            initial = FogParameters.Read(originalMaterial);
            runtimeMaterial = new Material(originalMaterial) {
                name = originalMaterial.name + " (Part2 runtime)", hideFlags = HideFlags.HideAndDontSave
            };
            fogFeature.passMaterial = runtimeMaterial;
            blend = transitionSeconds <= 0 ? 1 : 0;
            part2.Apply(runtimeMaterial, initial, blend);
        }
        private void LateUpdate()
        {
            if (runtimeMaterial == null) return;
            blend = transitionSeconds <= 0 ? 1 : Mathf.Clamp01(blend + Time.unscaledDeltaTime / transitionSeconds);
            part2.Apply(runtimeMaterial, initial, Mathf.SmoothStep(0, 1, blend));
        }
        private void OnDisable()
        {
            // Also runs on exiting Play mode. Never write runtime settings into the source .mat.
            if (runtimeMaterial == null) return;
            if (fogFeature != null && fogFeature.passMaterial == runtimeMaterial)
                fogFeature.passMaterial = originalMaterial;
            if (Application.isPlaying) Destroy(runtimeMaterial); else DestroyImmediate(runtimeMaterial);
            runtimeMaterial = null;
            originalMaterial = null;
        }
    }
}
