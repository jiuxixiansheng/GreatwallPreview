using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Sza.Bow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Timeline;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Sza.Editor
{
    public static class MagicBowSetup
    {
        public const string Folder = "Assets/sza/Rendering/Bow";
        public const string PrefabPath = "Assets/sza/prefab/MagicBow_Network.prefab";
        private const string ModelPath = "Assets/sza/model/弓箭/shortbowexport.fbx";
        private const string Effects = "Assets/Hovl Studio/Magic effects pack/Prefabs/Hits and explosions/";

        [MenuItem("Tools/Sza/配置第一章魔法弓箭")]
        public static void Configure()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (Application.isPlaying || AnimationMode.InAnimationMode() || scene.path != "Assets/Scenes/szaScene_Final.unity")
                throw new InvalidOperationException("请在非运行、非预览状态打开 szaScene_Final。");
            var objects = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
            var director = objects.Single(x => x.name == "timeline_01").GetComponent<PlayableDirector>();
            var manager = objects.Select(x => x.GetComponent<NetworkManager>()).First(x => x != null);
            var chapter = objects.Single(x => x.name == "part1");
            EnsureFolder(Folder + "/Materials");
            BuildPrefab();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!manager.spawnPrefabs.Contains(prefab)) manager.spawnPrefabs.Add(prefab);
            EditorUtility.SetDirty(manager);

            var controller = chapter.GetComponentsInChildren<MagicBowDirector>(true).FirstOrDefault();
            if (controller == null)
            {
                var go = new GameObject("MagicBowSequence · 第9675帧");
                go.transform.SetParent(chapter, false);
                controller = go.AddComponent<MagicBowDirector>();
                // Near NavTarget_1_4, facing out from the walkway. All anchors are editable.
                controller.spawnPoint = Point(go.transform, "BowSpawn · 弓箭出场点", new Vector3(-6.25f, 7f, -3f));
                controller.aimPoint = Point(go.transform, "BowAim · 朝城下瞄准", new Vector3(3.35f, 0.1f, -24.6f));
                controller.dropPoint = Point(go.transform, "BowDrop · 落地释放点", new Vector3(-5.5f, 6.15f, -3.2f));
            }
            controller.bowPrefab = prefab.GetComponent<NetworkMagicBow>();
            var timeline = (TimelineAsset)director.playableAsset;
            var track = timeline.GetOutputTracks().OfType<MagicBowTrack>().FirstOrDefault();
            if (track == null) track = timeline.CreateTrack<MagicBowTrack>(null, "弓箭 · 出场、射箭与拾取");
            var clips = track.GetClips().ToArray();
            var clip = clips.Length == 0 ? track.CreateClip<MagicBowClip>() : clips[0];
            clip.start = 9675d / timeline.editorSettings.frameRate;
            clip.duration = 8d;
            clip.displayName = "Explosion → 开弓 → 溶解箭 → Love hit → 掉落拾取";
            director.SetGenericBinding(track, controller);
            EditorUtility.SetDirty(track); EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(director); EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("场景保存失败。");
            Selection.activeGameObject = controller.gameObject;
            Debug.Log("[魔法弓箭] 已配置 timeline_01 第9675帧、射箭演示、首次拾取提示与网络抓取。", controller);
        }

        private static void BuildPrefab()
        {
            var root = new GameObject("MagicBow_Network");
            try
            {
                root.AddComponent<NetworkIdentity>();
                var body = root.AddComponent<Rigidbody>();
                body.mass = 0.35f; body.useGravity = true; body.isKinematic = false;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                var collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0.03f, 0f, 0f);
                collider.size = new Vector3(0.25f, 1.18f, 0.14f);
                var grab = root.AddComponent<XRGrabInteractable>();
                grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
                grab.useDynamicAttach = true;
                grab.throwOnDetach = true;
                var sync = root.AddComponent<NetworkTransformUnreliable>();
                sync.syncDirection = SyncDirection.ServerToClient;
                sync.coordinateSpace = CoordinateSpace.World;
                var networkGrab = root.AddComponent<NetworkGrabbable>();
                var serialized = new SerializedObject(networkGrab);
                serialized.FindProperty("scriptedMotion").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                var bow = root.AddComponent<NetworkMagicBow>();
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                model.transform.SetParent(root.transform, false);
                model.name = "BowVisual";
                bow.model = model;
                bow.drawAndFire = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
                    .Single(x => x.name == "Shortbow|Draw and Fire");
                bow.drawAndFire.SampleAnimation(model, 0f);
                bow.arrow = model.GetComponentsInChildren<Transform>().Single(x => x.name == "BowArrow");
                bow.arrowRenderer = bow.arrow.GetComponent<Renderer>();
                bow.bowRenderers = model.GetComponentsInChildren<Renderer>().Where(x => x != bow.arrowRenderer).ToArray();
                var stringBone = model.GetComponentsInChildren<Transform>().Single(x => x.name == "String");
                // Extend the shaft before fitting the nock, so a full draw leaves the tip beyond the bow.
                bow.arrow.localScale = Vector3.Scale(bow.arrow.localScale, new Vector3(1.6f, 1f, 1f));
                var arrowMesh = bow.arrow.GetComponent<MeshFilter>().sharedMesh;
                Vector3 nock = new Vector3(arrowMesh.bounds.min.x, arrowMesh.bounds.center.y, arrowMesh.bounds.center.z);
                Vector3 restNock = model.transform.InverseTransformPoint(bow.arrow.TransformPoint(nock));
                bow.drawAndFire.SampleAnimation(model, 1f);
                bow.arrowDrawOffset = model.transform.InverseTransformPoint(stringBone.position) - restNock;
                bow.drawAndFire.SampleAnimation(model, 0f);
                foreach (var animator in model.GetComponentsInChildren<Animator>()) animator.enabled = false;
                var shader = Shader.Find("SZA/Bow/MagicBow");
                if (shader == null) throw new InvalidOperationException("MagicBow Shader 缺失。");
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => BowMaterial(m, shader)).ToArray();
                bow.appearanceEffect = Effect(root.transform, "Explosion", "Appearance · 同款爆发");
                bow.appearanceEffect.transform.localScale = Vector3.one * 0.4f;
                bow.hitEffect = Effect(root.transform, "Love hit", "Love hit · 命中");
                bow.hitEffect.transform.localScale = Vector3.one * 0.8f;
                bow.pickupPrompt = Prompt(root.transform);
                ConfigureInteraction(root);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                var identity = saved.GetComponent<NetworkIdentity>();
                // Mirror's editor getter initializes a new prefab ID; persist it for player builds.
                if (identity.assetId == 0) throw new InvalidOperationException("弓箭网络预制体 assetId 无效。");
                EditorUtility.SetDirty(identity);
                AssetDatabase.SaveAssetIfDirty(identity);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [MenuItem("Tools/Sza/更新弓箭双手射箭交互")]
        public static void UpgradeInteraction()
        {
            if (Application.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ConfigureInteraction(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var identity = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<NetworkIdentity>();
            if (identity.assetId == 0) throw new InvalidOperationException("弓箭网络预制体 assetId 无效。");
            EditorUtility.SetDirty(identity); AssetDatabase.SaveAssetIfDirty(identity);
            Debug.Log("[魔法弓箭] 已更新固定中央握点、双手拉弓、生成箭及网络射击。");
        }

        private static void ConfigureInteraction(GameObject root)
        {
            var bow = root.GetComponent<NetworkMagicBow>();
            var grip = root.GetComponent<XRGrabInteractable>();
            var attach = root.transform.Find("GripAttach · 中央握点");
            if (attach == null) attach = new GameObject("GripAttach · 中央握点").transform;
            attach.SetParent(root.transform, false);
            attach.localPosition = new Vector3(0.1f, 0f, 0f);
            attach.localRotation = Quaternion.Euler(0f, 90f, 0f);
            grip.attachTransform = attach;
            grip.useDynamicAttach = false;
            grip.attachEaseInTime = 0.08f;
            grip.smoothPosition = grip.smoothRotation = false;
            var facing = root.GetComponent<BowFacingGrabTransformer>();
            if (facing == null) facing = root.AddComponent<BowFacingGrabTransformer>();
            grip.addDefaultGrabTransformers = false;
            grip.startingSingleGrabTransformers.Clear();
            grip.startingSingleGrabTransformers.Add(facing);
            grip.startingMultipleGrabTransformers.Clear();
            foreach (var other in root.GetComponents<UnityEngine.XR.Interaction.Toolkit.Transformers.XRGeneralGrabTransformer>())
                UnityEngine.Object.DestroyImmediate(other);
            grip.colliders.Clear(); grip.colliders.Add(root.GetComponent<BoxCollider>());
            var weapon = root.GetComponent<NetworkMagicBowWeapon>();
            if (weapon == null) weapon = root.AddComponent<NetworkMagicBowWeapon>();
            if (Mathf.Approximately(weapon.maximumLateralPull, 0.25f)) weapon.maximumLateralPull = 0.75f;
            weapon.bow = bow;
            weapon.stringBone = bow.model.GetComponentsInChildren<Transform>().Single(x => x.name == "String");
            bow.drawAndFire.SampleAnimation(bow.model, 0f);
            weapon.relaxedNock = bow.model.transform.InverseTransformPoint(weapon.stringBone.position);
            var handle = root.GetComponentInChildren<BowStringHandle>(true);
            if (handle == null)
            {
                var go = new GameObject("BowString · 另一只手抓取");
                go.transform.SetParent(root.transform, false);
                var collider = go.AddComponent<SphereCollider>(); collider.radius = 0.085f; collider.isTrigger = true;
                handle = go.AddComponent<BowStringHandle>();
            }
            handle.transform.localPosition = weapon.relaxedNock;
            handle.weapon = weapon;
            handle.colliders.Clear(); handle.colliders.Add(handle.GetComponent<SphereCollider>());
            weapon.stringHandle = handle;
            handle.gameObject.SetActive(false);
        }

        private static Material BowMaterial(Material source, Shader shader)
        {
            string path = Folder + "/Materials/MagicBow_" + source.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            foreach (var property in new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" })
                if (source.HasProperty(property)) material.SetTexture(property, source.GetTexture(property));
            foreach (var property in new[] { "_BumpScale", "_Smoothness", "_Metallic", "_OcclusionStrength" })
                if (source.HasProperty(property)) material.SetFloat(property, source.GetFloat(property));
            material.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            material.SetFloat("_UseMetallicMap", 1f);
            material.SetFloat("_Reveal", 1f);
            material.SetFloat("_Highlight", 0f);
            material.SetFloat("_BreathSpeed", 0f);
            material.SetTexture("_DissolveMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/sza/tex/T_SandStorm_FineGrain_Seamless.png"));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static ParticleSystem Effect(Transform parent, string assetName, string name)
        {
            var fx = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Effects + assetName + ".prefab"));
            fx.transform.SetParent(parent, false); fx.name = name;
            foreach (var p in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = p.main; main.loop = false; main.playOnAwake = false;
                p.useAutoRandomSeed = false; p.randomSeed = (uint)(29 + p.transform.GetSiblingIndex());
                p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            foreach (var renderer in fx.GetComponentsInChildren<ParticleSystemRenderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(FxMaterial).ToArray();
            fx.SetActive(false);
            return fx.GetComponent<ParticleSystem>();
        }

        private static Material FxMaterial(Material source)
        {
            if (source == null) return null;
            string path = Folder + "/Materials/FX_" + source.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(material, path); }
            material.SetTexture("_BaseMap", source.mainTexture);
            material.SetColor("_BaseColor", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
            bool additive = source.HasProperty("_DstBlend") && source.GetFloat("_DstBlend") == (float)BlendMode.One;
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", additive ? 2 : 0);
            material.SetFloat("_ZWrite", 0); material.SetFloat("_Cull", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Transform Prompt(Transform parent)
        {
            var go = new GameObject("首次提示 · 请拾取弓箭", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.001f;
            var rect = (RectTransform)go.transform; rect.sizeDelta = new Vector2(650, 100);
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 80;
            var group = go.GetComponent<CanvasGroup>(); group.interactable = group.blocksRaycasts = false;
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Outline));
            label.transform.SetParent(go.transform, false);
            var text = label.GetComponent<Text>();
            text.font = Resources.Load<Font>("NotoSansSC-Guide");
            text.text = "请拾取弓箭"; text.fontSize = 58; text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 0.95f, 0.12f); text.raycastTarget = false;
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            label.GetComponent<Outline>().effectColor = Color.black;
            go.SetActive(false);
            return go.transform;
        }

        private static Transform Point(Transform parent, string name, Vector3 position)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = position;
            return go.transform;
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
