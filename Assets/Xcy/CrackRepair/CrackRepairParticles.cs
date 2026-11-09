using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Xcy.CrackRepair
{
    /// <summary>Small, short-lived particles emitted at the moving repair front.</summary>
    [DisallowMultipleComponent]
    public sealed class CrackRepairParticles : MonoBehaviour
    {
        private const int FrontBucketCount = 64;
        private static readonly RaycastHit[] WallHits = new RaycastHit[16];

        [Header("Dust")]
        [SerializeField, Min(0f)] private float dustPerSecond = 18f;
        [SerializeField] private Color dustColor = new Color(0.72f, 0.68f, 0.59f, 1f);
        [SerializeField] private Vector2 dustAlphaRange = new Vector2(0.18f, 0.35f);
        [SerializeField] private Vector2 dustSizeRange = new Vector2(0.025f, 0.055f);
        [SerializeField] private Vector2 dustLifetimeRange = new Vector2(0.35f, 0.65f);
        [SerializeField] private Vector2 dustNormalSpeedRange = new Vector2(0.035f, 0.11f);
        [SerializeField] private Vector2 dustUpSpeedRange = new Vector2(0.02f, 0.09f);
        [SerializeField, Min(0f)] private float dustScatterSpeed = 0.025f;
        [SerializeField, Min(0f)] private float dustGravity = 0.09f;

        [Header("Chips")]
        [SerializeField, Min(0f)] private float chipsPerSecond = 4f;
        [SerializeField] private Color chipColor = new Color(0.42f, 0.39f, 0.34f, 0.8f);
        [SerializeField] private Vector2 chipSizeRange = new Vector2(0.006f, 0.015f);
        [SerializeField] private Vector2 chipLifetimeRange = new Vector2(0.4f, 0.75f);
        [SerializeField] private Vector2 chipNormalSpeedRange = new Vector2(0.07f, 0.16f);
        [SerializeField] private Vector2 chipUpSpeedRange = new Vector2(0.02f, 0.08f);
        [SerializeField, Min(0f)] private float chipScatterSpeed = 0.045f;
        [SerializeField, Min(0f)] private float chipGravity = 0.65f;

        [Header("Placement")]
        [SerializeField, Min(0f)] private float surfaceOffset = 0.012f;
        [SerializeField, Min(0f)] private float dustJitter = 0.018f;
        [SerializeField, Min(0f)] private float chipJitter = 0.012f;

        private static Material particleMaterial;
        private static Texture2D softParticleTexture;

        private DecalProjector projector;
        private ParticleSystem dust;
        private ParticleSystem chips;
        private Vector3 fullSize;
        private List<Vector2>[] frontBuckets;
        private float dustRemainder;
        private float chipRemainder;

        public void Initialize(DecalProjector target, Vector3 initialSize, Texture2D repairOrderMap)
        {
            projector = target;
            fullSize = initialSize;
            if (projector == null || repairOrderMap == null || !repairOrderMap.isReadable)
            {
                Debug.LogError("Crack repair particles need a readable repair order map.", this);
                return;
            }
            BuildFrontBuckets(repairOrderMap);

            EnsureMaterial();
            if (particleMaterial == null) return;
            dust = CreateSystem("Repair Dust", 96, dustGravity);
            chips = CreateSystem("Repair Chips", 32, chipGravity);
        }

        private void BuildFrontBuckets(Texture2D map)
        {
            frontBuckets = new List<Vector2>[FrontBucketCount];
            for (int i = 0; i < frontBuckets.Length; i++) frontBuckets[i] = new List<Vector2>();
            Color32[] pixels = map.GetPixels32();
            for (int y = 0; y < map.height; y += 2)
            for (int x = 0; x < map.width; x += 2)
            {
                Color32 pixel = pixels[y * map.width + x];
                if (pixel.g < 64) continue;
                int bucket = Mathf.Min(FrontBucketCount - 1, pixel.r * FrontBucketCount / 256);
                frontBuckets[bucket].Add(new Vector2((x + 0.5f) / map.width, (y + 0.5f) / map.height));
            }
        }

        public void Clear()
        {
            dustRemainder = 0f;
            chipRemainder = 0f;
            if (dust != null) dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (chips != null) chips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void Begin()
        {
            Clear();
            if (dust != null) dust.Play();
            if (chips != null) chips.Play();
        }

        public void EmitAt(float progress, float deltaTime)
        {
            if (projector == null || dust == null || chips == null || frontBuckets == null) return;
            dustRemainder += dustPerSecond * deltaTime;
            chipRemainder += chipsPerSecond * deltaTime;

            while (dustRemainder >= 1f)
            {
                dustRemainder -= 1f;
                if (TryGetFrontUV(progress, out Vector2 uv))
                    EmitDust(FindWallPoint(uv, out Vector3 normal), normal);
            }

            while (chipRemainder >= 1f)
            {
                chipRemainder -= 1f;
                if (TryGetFrontUV(progress, out Vector2 uv))
                    EmitChip(FindWallPoint(uv, out Vector3 normal), normal);
            }
        }

        private bool TryGetFrontUV(float progress, out Vector2 uv)
        {
            int center = Mathf.Clamp(Mathf.FloorToInt(progress * FrontBucketCount), 0, FrontBucketCount - 1);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int bucket = Mathf.Clamp(center + Random.Range(-1, 2), 0, FrontBucketCount - 1);
                List<Vector2> candidates = frontBuckets[bucket];
                if (candidates.Count == 0) continue;
                uv = candidates[Random.Range(0, candidates.Count)];
                return true;
            }
            uv = default;
            return false;
        }

        private Vector3 FindWallPoint(Vector2 uv, out Vector3 normal)
        {
            Vector3 localPoint = new Vector3((uv.x - 0.5f) * fullSize.x,
                (uv.y - 0.5f) * fullSize.y, 0f);
            Transform decalTransform = projector.transform;
            // ScaleInvariant decals ignore the Transform scale, so particle positions must too.
            Vector3 projectedPoint = projector.scaleMode == DecalScaleMode.ScaleInvariant
                ? decalTransform.position + decalTransform.rotation * localPoint
                : decalTransform.TransformPoint(localPoint);
            Vector3 intoWall = decalTransform.forward;
            normal = -intoWall;

            // The crack's interaction plane can sit in front of the wall; ignore it.
            var ray = new Ray(projectedPoint - intoWall * 0.2f, intoWall);
            int count = Physics.RaycastNonAlloc(ray, WallHits, 0.75f, ~0,
                QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            Vector3 wallPoint = projectedPoint + intoWall * 0.15f;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = WallHits[i];
                if (hit.collider.GetComponentInParent<CrackRepairDecal>() != null ||
                    Vector3.Dot(hit.normal, -intoWall) < 0.4f || hit.distance >= nearest)
                    continue;

                nearest = hit.distance;
                wallPoint = hit.point;
                normal = hit.normal;
            }

            return wallPoint + normal * surfaceOffset;
        }

        private static float RandomInRange(Vector2 range) =>
            Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));

        private void EmitDust(Vector3 point, Vector3 normal)
        {
            var particle = new ParticleSystem.EmitParams
            {
                position = point + Random.insideUnitSphere * dustJitter,
                velocity = normal * RandomInRange(dustNormalSpeedRange) +
                    Vector3.up * RandomInRange(dustUpSpeedRange) + Random.insideUnitSphere * dustScatterSpeed,
                startLifetime = Mathf.Max(0.01f, RandomInRange(dustLifetimeRange)),
                startSize = Mathf.Max(0.001f, RandomInRange(dustSizeRange)),
                startColor = new Color(dustColor.r, dustColor.g, dustColor.b,
                    dustColor.a * Mathf.Clamp01(RandomInRange(dustAlphaRange)))
            };
            dust.Emit(particle, 1);
        }

        private void EmitChip(Vector3 point, Vector3 normal)
        {
            var particle = new ParticleSystem.EmitParams
            {
                position = point + Random.insideUnitSphere * chipJitter,
                velocity = normal * RandomInRange(chipNormalSpeedRange) +
                    Vector3.up * RandomInRange(chipUpSpeedRange) + Random.insideUnitSphere * chipScatterSpeed,
                startLifetime = Mathf.Max(0.01f, RandomInRange(chipLifetimeRange)),
                startSize = Mathf.Max(0.001f, RandomInRange(chipSizeRange)),
                startColor = chipColor
            };
            chips.Emit(particle, 1);
        }

        private ParticleSystem CreateSystem(string systemName, int maxParticles, float gravity)
        {
            var child = new GameObject(systemName);
            child.transform.SetParent(transform, false);
            var system = child.AddComponent<ParticleSystem>();
            // A newly added system can already be playing before playOnAwake is changed.
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 4f;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.gravityModifier = gravity;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = particleMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private static void EnsureMaterial()
        {
            if (particleMaterial != null) return;
            Material template = Resources.Load<Material>("M_CrackRepairParticles");
            if (template == null)
            {
                Debug.LogWarning("Crack repair particle material is missing from Xcy/CrackRepair/Resources.");
                return;
            }

            softParticleTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
            {
                name = "Crack Repair Soft Particle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float radius = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 15.5f;
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - radius), 1.5f);
                pixels[y * 32 + x] = new Color(1f, 1f, 1f, alpha);
            }
            softParticleTexture.SetPixels(pixels);
            softParticleTexture.Apply(false, true);

            particleMaterial = new Material(template)
            {
                name = "Crack Repair Particle Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            particleMaterial.SetTexture("_BaseMap", softParticleTexture);
        }
    }
}
