using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Greatwall.CannonGame
{
    // 特效运行时生成；仅烟花贴图使用 Kenney Particle Pack（CC0）资源。
    internal sealed class CannonVfx : MonoBehaviour
    {
        private const float Gravity = 9.81f;

        private Vector3 _muzzle;
        private Vector3 _fireDirection;
        private Mesh _ballMesh;
        private Material[] _ballMaterials;
        private Bounds _ballBounds;
        private Vector3 _ballVisualScale;
        private Vector3 _slotPosition;
        private Quaternion _slotRotation;

        private readonly List<Object> _runtimeObjects = new List<Object>();

        private Texture2D _radialTexture;
        private Texture2D _ribbonTexture;
        private Material _flashMaterial;
        private Material _fireworkFlashMaterial;
        private Material _sparkMaterial;
        private Material _emberMaterial;
        private Material _glitterMaterial;
        private Material _glowMaterial;
        private Material _smokeMaterial;
        private Material _ghostMaterial;
        private Material _trailMaterial;

        private GameObject _ghost;
        private Light _flashLight;
        private Coroutine _lightRoutine;
        private Light _fireworkLight;
        private Coroutine _fireworkLightRoutine;

        public void Initialize(Vector3 muzzle, Vector3 fireDirection, Mesh ballMesh, Material[] ballMaterials,
            Bounds ballBounds, Vector3 ballVisualScale, Vector3 slotPosition, Quaternion slotRotation)
        {
            _muzzle = muzzle;
            _fireDirection = fireDirection;
            _ballMesh = ballMesh;
            _ballMaterials = ballMaterials;
            _ballBounds = ballBounds;
            _ballVisualScale = ballVisualScale;
            _slotPosition = slotPosition;
            _slotRotation = slotRotation;

            BuildResources();
            BuildGhost();
        }

        private void BuildResources()
        {
            _radialTexture = CreateRadialTexture(64);
            _ribbonTexture = CreateRibbonTexture(8, 64);

            // 烟花的星芒/光晕/爆闪/烟雾贴图来自 Kenney Particle Pack（CC0，见 Resources/CannonVfx/LICENSE-Kenney-CC0.txt）。
            var starTexture = LoadParticleTexture("CannonVfx/star_09", _radialTexture);
            var glitterTexture = LoadParticleTexture("CannonVfx/star_06", _radialTexture);
            var flareTexture = LoadParticleTexture("CannonVfx/flare_01", _radialTexture);
            var glowTexture = LoadParticleTexture("CannonVfx/circle_01", _radialTexture);
            var smokeTexture = LoadParticleTexture("CannonVfx/smoke_06", _radialTexture);

            _flashMaterial = CreateParticleMaterial("CannonFlash", _radialTexture, new Color(1.6f, 1.05f, 0.45f, 1f), true);
            _sparkMaterial = CreateParticleMaterial("CannonSpark", starTexture, Color.white, false);
            _emberMaterial = CreateParticleMaterial("CannonEmber", glowTexture, new Color(1f, 0.85f, 0.7f, 1f), false);
            _glitterMaterial = CreateParticleMaterial("CannonGlitter", glitterTexture, new Color(1.1f, 1.08f, 1.02f, 1f), true);
            _glowMaterial = CreateParticleMaterial("CannonGlow", glowTexture, new Color(0.95f, 0.85f, 0.72f, 1f), true);
            _fireworkFlashMaterial = CreateParticleMaterial("CannonFireworkFlash", flareTexture, new Color(1.55f, 1.32f, 1f, 1f), true);
            _smokeMaterial = CreateParticleMaterial("CannonSmoke", smokeTexture, new Color(0.55f, 0.53f, 0.5f, 0.35f), false);
            _ghostMaterial = CreateGhostMaterial();
            _trailMaterial = CreateTrailMaterial("CannonTrail");

            var lightObject = new GameObject("MuzzleLight");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.position = _muzzle;
            _flashLight = lightObject.AddComponent<Light>();
            _flashLight.type = LightType.Point;
            _flashLight.color = new Color(1f, 0.72f, 0.36f);
            _flashLight.range = 12f;
            _flashLight.intensity = 0f;
            _flashLight.shadows = LightShadows.None;

            var fireworkLightObject = new GameObject("FireworkLight");
            fireworkLightObject.transform.SetParent(transform, false);
            _fireworkLight = fireworkLightObject.AddComponent<Light>();
            _fireworkLight.type = LightType.Point;
            _fireworkLight.color = Color.white;
            _fireworkLight.range = 70f;
            _fireworkLight.intensity = 0f;
            _fireworkLight.shadows = LightShadows.None;
        }

        private Texture2D CreateRadialTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "CannonRadial";
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[size * size];
            var half = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x - half) / half;
                    var dy = (y - half) / half;
                    var r = Mathf.Sqrt(dx * dx + dy * dy);
                    var falloff = r >= 1f ? 0f : (1f - r) * (1f - r);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, falloff);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            _runtimeObjects.Add(texture);
            return texture;
        }

        private Texture2D CreateRibbonTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.name = "CannonRibbon";
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[width * height];
            var centre = (width - 1) * 0.5f;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var dx = Mathf.Abs(x - centre) / centre;
                    var alpha = 1f - dx * dx;
                    var fade = Mathf.Sin((y / (float)(height - 1)) * Mathf.PI);
                    alpha *= 0.35f + 0.65f * fade;
                    pixels[y * width + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            _runtimeObjects.Add(texture);
            return texture;
        }

        private static Texture2D LoadParticleTexture(string path, Texture2D fallback)
        {
            var texture = Resources.Load<Texture2D>(path);
            return texture != null ? texture : fallback;
        }

        private Material CreateParticleMaterial(string name, Texture2D texture, Color color, bool additive)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", additive ? 2f : 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", additive
                    ? (float)UnityEngine.Rendering.BlendMode.One
                    : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            if (material.HasProperty("_ColorMode")) material.SetFloat("_ColorMode", 1f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _runtimeObjects.Add(material);
            return material;
        }

        private Material CreateGhostMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = "CannonGhost" };
            var color = new Color(1f, 0.78f, 0.32f, 0.45f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _runtimeObjects.Add(material);
            return material;
        }

        private Material CreateTrailMaterial(string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = name };
            var texture = _ribbonTexture;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 2f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _runtimeObjects.Add(material);
            return material;
        }

        private void BuildGhost()
        {
            _ghost = new GameObject("CannonSlotGhost");
            _ghost.transform.SetParent(transform, false);
            _ghost.transform.SetPositionAndRotation(_slotPosition, _slotRotation);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(_ghost.transform, false);
            visual.transform.localPosition = -Vector3.Scale(_ballBounds.center, _ballVisualScale);
            visual.transform.localScale = _ballVisualScale;
            var filter = visual.AddComponent<MeshFilter>();
            filter.sharedMesh = _ballMesh;
            var renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _ghostMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void SetGhostVisible(bool visible)
        {
            if (_ghost != null) _ghost.SetActive(visible);
        }

        private void Update()
        {
            if (_ghost == null || !_ghost.activeSelf) return;
            float pulse = Mathf.Sin(Time.time * 3.2f);
            var color = new Color(1f, 0.78f, 0.32f, 0.42f + pulse * 0.16f);
            if (_ghostMaterial.HasProperty("_BaseColor")) _ghostMaterial.SetColor("_BaseColor", color);
            _ghostMaterial.SetColor("_Color", color);
            _ghost.transform.localScale = Vector3.one * (1f + pulse * 0.04f);
        }

        public void PlayMuzzleFlash()
        {
            var rotation = Quaternion.LookRotation(_fireDirection == Vector3.zero ? Vector3.forward : _fireDirection);
            var effect = CreateParticleSystem("MuzzleFlash", _flashMaterial, _muzzle + _fireDirection * 0.1f, rotation);
            ConfigureRenderer(effect);
            var main = effect.main;
            main.duration = 0.25f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(2.2f, 1.6f, 0.8f, 1f), new Color(2.6f, 0.9f, 0.25f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = effect.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 34) });
            var shape = effect.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.06f;
            effect.Play();

            StartCoroutine(SmokePuff());
            PulseLight(4.5f, 12f, 0.22f);
        }

        private IEnumerator SmokePuff()
        {
            var rotation = Quaternion.LookRotation(_fireDirection == Vector3.zero ? Vector3.forward : _fireDirection);
            var position = _muzzle + _fireDirection * 0.35f;
            for (var i = 0; i < 3; i++)
            {
                var effect = CreateParticleSystem("MuzzleSmoke" + i, _smokeMaterial, position, rotation);
                ConfigureRenderer(effect);
                var main = effect.main;
                main.duration = 0.5f;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(0.7f, 0.68f, 0.65f, 0.3f), new Color(0.5f, 0.49f, 0.47f, 0.16f));
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.stopAction = ParticleSystemStopAction.Destroy;
                var emission = effect.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });
                var shape = effect.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.1f;
                effect.Play();
                yield return new WaitForSeconds(0.06f);
            }
        }

        public void LaunchProjectile(Vector3 position, Vector3 velocity, float flightTime)
        {
            StartCoroutine(ProjectileRoutine(position, velocity, flightTime));
        }

        private IEnumerator ProjectileRoutine(Vector3 position, Vector3 velocity, float flightTime)
        {
            var projectile = new GameObject("CannonProjectile");
            projectile.transform.position = position;
            projectile.transform.rotation = _slotRotation;

            // 炮弹网格原点不在球心，必须像预置弹药一样补偿，否则弹丸会偏离炮口并绕逻辑点打转。
            var visual = new GameObject("Visual");
            visual.transform.SetParent(projectile.transform, false);
            visual.transform.localPosition = -Vector3.Scale(_ballBounds.center, _ballVisualScale);
            visual.transform.localScale = _ballVisualScale;
            var filter = visual.AddComponent<MeshFilter>();
            filter.sharedMesh = _ballMesh;
            var renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = _ballMaterials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var trailObject = new GameObject("Trail");
            trailObject.transform.SetParent(projectile.transform, false);
            var trail = trailObject.AddComponent<TrailRenderer>();
            trail.material = _trailMaterial;
            trail.time = 1.8f;
            trail.startWidth = 0.45f;
            trail.endWidth = 0.07f;
            trail.textureMode = LineTextureMode.Stretch;
            trail.minVertexDistance = 0.05f;
            trail.numCapVertices = 4;
            trail.numCornerVertices = 4;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(2.4f, 1.5f, 0.5f), 0f),
                    new GradientColorKey(new Color(2.0f, 0.7f, 0.2f), 0.35f),
                    new GradientColorKey(new Color(0.9f, 0.35f, 0.12f), 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;

            // 首帧先停在炮口，避免第一个 deltaTime 积分把弹丸直接甩离炮口。
            yield return null;

            var elapsed = 0f;
            while (elapsed < flightTime)
            {
                var step = Time.deltaTime;
                velocity += Vector3.down * (Gravity * step);
                projectile.transform.position += velocity * step;
                projectile.transform.Rotate(Vector3.forward, 220f * step, Space.Self);
                elapsed += step;
                yield return null;
            }

            trail.emitting = false;
            renderer.enabled = false;

            var fadeStart = Time.time;
            while (Time.time - fadeStart < 1.5f) yield return null;
            if (trailObject != null) Destroy(trailObject);
            Destroy(projectile);
        }

        public void PlayFirework(Vector3 apex)
        {
            StartCoroutine(FireworkRoutine(apex));
        }

        private IEnumerator FireworkRoutine(Vector3 apex)
        {
            // 真实烟花的配色：绯红 + 暖金 + 米白，绿色只作少量副爆；不用荧光洋红/紫/青。
            var redA = new Color(0.82f, 0.18f, 0.10f);
            var redB = new Color(0.60f, 0.07f, 0.03f);
            var goldA = new Color(1f, 0.72f, 0.34f);
            var goldB = new Color(0.82f, 0.45f, 0.14f);
            var ivoryA = new Color(1f, 0.92f, 0.76f);
            var ivoryB = new Color(0.92f, 0.74f, 0.46f);
            var greenA = new Color(0.42f, 0.68f, 0.32f);
            var greenB = new Color(0.18f, 0.45f, 0.22f);
            var silverA = new Color(0.92f, 0.9f, 0.82f);
            var silverB = new Color(0.74f, 0.7f, 0.6f);

            PulseFireworkLight(apex, new Color(1f, 0.82f, 0.58f), 4.5f, 70f, 1.4f);
            SpawnFlash(apex);

            var fade = FadeGradient();
            SpawnBurst("FireworkShellOuter", apex, redA, redB, 300,
                12f, 16f, 1.0f, 1.4f, 0.75f, 1.05f, 0.28f, true, 0.55f, 0.12f, fade, true);
            SpawnBurst("FireworkShellMid", apex, goldA, goldB, 220,
                8f, 11.5f, 1.15f, 1.55f, 0.8f, 1.15f, 0.32f, true, 0.5f, 0.12f, fade, true);
            SpawnBurst("FireworkCore", apex, ivoryA, ivoryB, 80,
                3.5f, 6f, 1.3f, 1.8f, 0.9f, 1.3f, 0.4f, true, 0.5f, 0.1f, fade);
            SpawnHotCore(apex, fade);

            yield return new WaitForSeconds(0.12f);
            SpawnSmokePuff(apex, fade);
            SpawnBurst("FireworkGlitter", apex, new Color(1f, 0.95f, 0.82f), new Color(0.85f, 0.68f, 0.4f), 140,
                1f, 3.5f, 2.4f, 3.2f, 0.2f, 0.4f, 0.04f, false, 0f, 0f, TwinkleGradient());
            SpawnBurst("FireworkEmbers", apex, new Color(0.95f, 0.42f, 0.1f), new Color(0.65f, 0.16f, 0.02f), 90,
                0.5f, 2.2f, 2.2f, 3f, 0.18f, 0.32f, 0.22f, false, 0f, 0f, fade);

            yield return new WaitForSeconds(0.32f);
            SpawnBurst("FireworkSideGreen", apex + new Vector3(7f, 3.4f, 1f), greenA, greenB, 55,
                5.5f, 8.5f, 1.2f, 1.7f, 0.8f, 1.1f, 0.25f, true, 0.5f, 0.1f, fade);

            yield return new WaitForSeconds(0.22f);
            SpawnBurst("FireworkSideSilver", apex + new Vector3(-6.5f, 4.2f, -1.1f), silverA, silverB, 60,
                4.5f, 7f, 1.2f, 1.7f, 0.8f, 1.1f, 0.25f, true, 0.5f, 0.1f, fade);
        }

        private void SpawnHotCore(Vector3 apex, Gradient gradient)
        {
            // 核心只保留轻微的暖色过曝，避免整团烟花变成霓虹灯。
            SpawnBurstPass("FireworkHotCoreGlow", apex, new Color(1.7f, 1.35f, 0.95f), new Color(1.2f, 0.65f, 0.25f), 60,
                1.2f, 4f, 0.8f, 1.3f, 1f, 1.8f, 0f, false, 0f, 0f, gradient);
        }

        private void SpawnFlash(Vector3 apex)
        {
            var flash = CreateParticleSystem("FireworkFlash", _fireworkFlashMaterial, apex, Quaternion.identity);
            ConfigureRenderer(flash);
            var main = flash.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.24f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(2.2f, 3.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.75f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = flash.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            flash.Play();
            Destroy(flash.gameObject, 1.2f);
        }

        private void SpawnSmokePuff(Vector3 position, Gradient gradient)
        {
            var effect = CreateParticleSystem("FireworkSmoke", _smokeMaterial, position, Quaternion.identity);
            ConfigureRenderer(effect);

            var main = effect.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(4.5f, 8.5f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.42f, 0.38f, 0.35f, 0.16f), new Color(0.3f, 0.28f, 0.26f, 0.1f));
            main.gravityModifier = -0.012f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = effect.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 34) });

            var shape = effect.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1.5f;

            var sizeOverLifetime = effect.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.55f);
            sizeCurve.AddKey(0.35f, 1f);
            sizeCurve.AddKey(1f, 1.5f);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var colorOverLifetime = effect.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            effect.Play();
            Destroy(effect.gameObject, 6f);
        }

        private void SpawnBurst(string name, Vector3 position, Color colorA, Color colorB, int count,
            float speedMin, float speedMax, float lifeMin, float lifeMax, float sizeMin, float sizeMax,
            float gravity, bool stretched, float lengthScale, float velocityScale, Gradient gradient, bool glow = false)
        {
            SpawnBurstPass(name, position, colorA, colorB, count,
                speedMin, speedMax, lifeMin, lifeMax, sizeMin, sizeMax,
                gravity, stretched, lengthScale, velocityScale, gradient);

            if (!glow) return;

            var glowA = new Color(colorA.r * 1.05f, colorA.g * 1.05f, colorA.b * 1.05f, 1f);
            var glowB = new Color(colorB.r * 1.05f, colorB.g * 1.05f, colorB.b * 1.05f, 1f);
            SpawnBurstPass(name + "Glow", position, glowA, glowB, Mathf.Max(1, count / 2),
                speedMin, speedMax, lifeMin, lifeMax, sizeMin, sizeMax,
                gravity, stretched, lengthScale, velocityScale, gradient);
        }

        private void SpawnBurstPass(string name, Vector3 position, Color colorA, Color colorB, int count,
            float speedMin, float speedMax, float lifeMin, float lifeMax, float sizeMin, float sizeMax,
            float gravity, bool stretched, float lengthScale, float velocityScale, Gradient gradient)
        {
            var effect = CreateParticleSystem(name, _sparkMaterial, position, Quaternion.identity);
            ConfigureRenderer(effect);

            var burstRenderer = effect.GetComponent<ParticleSystemRenderer>();
            if (stretched)
            {
                burstRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                burstRenderer.lengthScale = lengthScale;
                burstRenderer.velocityScale = velocityScale;
            }

            var main = effect.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = effect.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = effect.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;

            var sizeOverLifetime = effect.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            var sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 1f);
            sizeCurve.AddKey(0.65f, 0.8f);
            sizeCurve.AddKey(1f, 0f);
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var colorOverLifetime = effect.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            effect.Play();
            Destroy(effect.gameObject, 5f);
        }

        private static Gradient FadeGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.95f, 0.55f), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        private static Gradient TwinkleGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0.15f, 0f), new GradientAlphaKey(0.9f, 0.25f),
                    new GradientAlphaKey(0.2f, 0.45f), new GradientAlphaKey(1f, 0.65f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private ParticleSystem CreateParticleSystem(string effectName, Material material, Vector3 position, Quaternion rotation)
        {
            var effectObject = new GameObject(effectName);
            effectObject.transform.SetParent(transform, true);
            effectObject.transform.SetPositionAndRotation(position, rotation);
            var effect = effectObject.AddComponent<ParticleSystem>();
            effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return effect;
        }

        private void ConfigureRenderer(ParticleSystem effect)
        {
            var renderer = effect.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = effect.name == "FireworkFlash" ? _fireworkFlashMaterial
                : effect.name == "FireworkGlitter" ? _glitterMaterial
                : effect.name == "FireworkCore" ? _sparkMaterial
                : effect.name == "FireworkEmbers" ? _emberMaterial
                : effect.name == "FireworkSideSilver" ? _glitterMaterial
                : effect.name.EndsWith("Glow") ? _glowMaterial
                : effect.name.StartsWith("MuzzleSmoke") ? _smokeMaterial
                : effect.name.StartsWith("FireworkSmoke") ? _smokeMaterial
                : effect.name.StartsWith("Firework") ? _sparkMaterial
                : _flashMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 10;
        }

        private void PulseFireworkLight(Vector3 position, Color color, float intensity, float range, float duration)
        {
            if (_fireworkLight == null) return;
            if (_fireworkLightRoutine != null) StopCoroutine(_fireworkLightRoutine);
            _fireworkLight.transform.position = position;
            _fireworkLight.color = color;
            _fireworkLight.range = range;
            _fireworkLightRoutine = StartCoroutine(FireworkLightRoutine(intensity, duration));
        }

        private IEnumerator FireworkLightRoutine(float intensity, float duration)
        {
            if (_fireworkLight == null) yield break;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                _fireworkLight.intensity = Mathf.Lerp(intensity, 0f, t * t);
                yield return null;
            }
            _fireworkLight.intensity = 0f;
            _fireworkLightRoutine = null;
        }

        private void PulseLight(float intensity, float range, float duration)
        {
            if (_lightRoutine != null) StopCoroutine(_lightRoutine);
            _lightRoutine = StartCoroutine(LightRoutine(intensity, range, duration));
        }

        private IEnumerator LightRoutine(float intensity, float range, float duration)
        {
            if (_flashLight == null) yield break;
            _flashLight.range = range;
            _flashLight.transform.position = _muzzle;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _flashLight.intensity = Mathf.Lerp(intensity, 0f, elapsed / duration);
                yield return null;
            }
            _flashLight.intensity = 0f;
            _lightRoutine = null;
        }

        private void OnDestroy()
        {
            for (var i = 0; i < _runtimeObjects.Count; i++)
            {
                if (_runtimeObjects[i] != null) Destroy(_runtimeObjects[i]);
            }
            _runtimeObjects.Clear();
        }
    }
}
