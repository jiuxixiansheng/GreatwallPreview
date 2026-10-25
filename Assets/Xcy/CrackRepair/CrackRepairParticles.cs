using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Xcy.CrackRepair
{
    /// <summary>Small, short-lived particles emitted at the moving repair front.</summary>
    [DisallowMultipleComponent]
    public sealed class CrackRepairParticles : MonoBehaviour
    {
        private const float DustPerSecond = 18f;
        private const float ChipsPerSecond = 4f;
        private static readonly RaycastHit[] WallHits = new RaycastHit[16];

        private static Material particleMaterial;
        private static Texture2D softParticleTexture;

        private DecalProjector projector;
        private ParticleSystem dust;
        private ParticleSystem chips;
        private Vector3 fullSize;
        private float dustRemainder;
        private float chipRemainder;

        public void Initialize(DecalProjector target, Vector3 initialSize)
        {
            projector = target;
            fullSize = initialSize;
            if (projector == null) return;

            EnsureMaterial();
            if (particleMaterial == null) return;
            dust = CreateSystem("Repair Dust", 48, 0.09f);
            chips = CreateSystem("Repair Chips", 16, 0.65f);
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
            if (projector == null || dust == null || chips == null) return;

            float lowerFront = 0.5f * (1f - progress);
            float upperFront = 0.5f * (1f + progress);
            Vector3 lowerPoint = FindWallPoint(lowerFront, out Vector3 lowerNormal);
            Vector3 upperPoint = FindWallPoint(upperFront, out Vector3 upperNormal);
            dustRemainder += DustPerSecond * deltaTime;
            chipRemainder += ChipsPerSecond * deltaTime;

            while (dustRemainder >= 1f)
            {
                dustRemainder -= 1f;
                bool upper = Random.value < 0.5f;
                EmitDust(upper ? upperPoint : lowerPoint, upper ? upperNormal : lowerNormal);
            }

            while (chipRemainder >= 1f)
            {
                chipRemainder -= 1f;
                bool upper = Random.value < 0.5f;
                EmitChip(upper ? upperPoint : lowerPoint, upper ? upperNormal : lowerNormal);
            }
        }

        private Vector3 FindWallPoint(float progress, out Vector3 normal)
        {
            // Approximate the main branch in 1_alpha.png at this vertical position.
            float x = Mathf.Lerp(0.65f, 0.42f, progress) + 0.025f * Mathf.Sin(progress * 10f);
            Vector3 localPoint = new Vector3((x - 0.5f) * fullSize.x,
                (progress - 0.5f) * fullSize.y, 0f);
            Transform decalTransform = projector.transform;
            Vector3 projectedPoint = decalTransform.TransformPoint(localPoint);
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

            return wallPoint + normal * 0.012f;
        }

        private void EmitDust(Vector3 point, Vector3 normal)
        {
            var particle = new ParticleSystem.EmitParams
            {
                position = point + Random.insideUnitSphere * 0.018f,
                velocity = normal * Random.Range(0.035f, 0.11f) +
                    Vector3.up * Random.Range(0.02f, 0.09f) + Random.insideUnitSphere * 0.025f,
                startLifetime = Random.Range(0.35f, 0.65f),
                startSize = Random.Range(0.025f, 0.055f),
                startColor = new Color(0.72f, 0.68f, 0.59f, Random.Range(0.18f, 0.35f))
            };
            dust.Emit(particle, 1);
        }

        private void EmitChip(Vector3 point, Vector3 normal)
        {
            var particle = new ParticleSystem.EmitParams
            {
                position = point + Random.insideUnitSphere * 0.012f,
                velocity = normal * Random.Range(0.07f, 0.16f) +
                    Vector3.up * Random.Range(0.02f, 0.08f) + Random.insideUnitSphere * 0.045f,
                startLifetime = Random.Range(0.4f, 0.75f),
                startSize = Random.Range(0.006f, 0.015f),
                startColor = new Color(0.42f, 0.39f, 0.34f, 0.8f)
            };
            chips.Emit(particle, 1);
        }

        private ParticleSystem CreateSystem(string systemName, int maxParticles, float gravity)
        {
            var child = new GameObject(systemName);
            child.transform.SetParent(transform, false);
            var system = child.AddComponent<ParticleSystem>();
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
