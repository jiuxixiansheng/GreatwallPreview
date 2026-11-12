using System.Collections.Generic;
using Sza.Ending;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using Unity.XR.CoreUtils;

namespace Sza.Editor
{
    public static class EndingSequenceSetup
    {
        [MenuItem("Tools/Sza/片尾/配置全景片尾场景")]
        public static void Configure()
        {
            if (Application.isPlaying || AnimationMode.InAnimationMode()) throw new System.InvalidOperationException("请退出运行和 Timeline 预览。");
            const string path = "Assets/Scenes/szaScene_end.unity";
            EnsureFolder("Assets/sza/Resources"); EnsureFolder("Assets/sza/Rendering/Ending");
            var config = AssetDatabase.LoadAssetAtPath<EndingSequenceSettings>("Assets/sza/Resources/EndingSequenceSettings.asset");
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<EndingSequenceSettings>();
                config.video = AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/sza/video/大空间片尾V2.mp4");
                AssetDatabase.CreateAsset(config, "Assets/sza/Resources/EndingSequenceSettings.asset");
            }
            if (config.video == null) throw new System.InvalidOperationException("缺少大空间片尾V2.mp4");
            ConfigureRenderer(config);
            var scene = SceneManager.GetSceneByPath(path);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponent<Light>() != null || root.name == "Main Camera" || root.name == "Ending Presentation" || root.name == "Ending Viewer")
                    Undo.DestroyObjectImmediate(root);
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers"); int layer = LayerMask.NameToLayer("EndingPresentation");
            if (layer < 0)
            {
                for (int i = 31; i >= 8; i--) if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) { layer = i; break; }
                if (layer < 0) throw new System.InvalidOperationException("没有可用的 Layer。");
                layers.GetArrayElementAtIndex(layer).stringValue = "EndingPresentation"; tags.ApplyModifiedProperties();
            }
            var rootObject = new GameObject("Ending Presentation");
            var player = rootObject.AddComponent<PanoramicEndingPlayer>(); player.enabled = false;
            player.settings = config; player.presentationLayer = layer;
            var center = new GameObject("Sphere Center"); center.transform.SetParent(rootObject.transform, false); center.transform.position = Vector3.up * 1.6f;
            player.viewCenter = center.transform;
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.name = "360 Ending Video Sphere";
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(rootObject.transform, false); sphere.transform.position = center.transform.position; sphere.transform.localScale = Vector3.one * 100f;
            sphere.layer = layer;
            var renderer = sphere.GetComponent<Renderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            player.panorama = renderer;
            player.panoramaMaterial = Material("EndingPanorama", "Sza/Ending/Panorama");
            player.blackoutMaterial = Material("EndingBlackout", "Sza/Ending/Blackout");
            renderer.sharedMaterial = player.panoramaMaterial;
            player.videoPlayer = rootObject.AddComponent<VideoPlayer>(); player.videoPlayer.playOnAwake = false; player.videoPlayer.clip = config.video; player.videoPlayer.isLooping = false;
            var audio = rootObject.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 0f;
            var rig = new GameObject("Ending Viewer"); var origin = rig.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset"); offset.transform.SetParent(rig.transform, false);
            var cameraObject = new GameObject("Ending Camera"); cameraObject.transform.SetParent(offset.transform, false); cameraObject.tag = "MainCamera";
            cameraObject.transform.localPosition = Vector3.up * 1.6f;
            var camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 1 << layer; camera.nearClipPlane = 0.05f; camera.farClipPlane = 150f;
            cameraObject.AddComponent<AudioListener>(); var additional = cameraObject.AddComponent<UniversalAdditionalCameraData>(); additional.renderPostProcessing = false;
            additional.SetRenderer(config.PresentationRendererIndex());
            origin.Camera = camera; origin.CameraFloorOffsetObject = offset; origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            var tracking = cameraObject.AddComponent<TrackedPoseDriver>();
            tracking.positionInput = new InputActionProperty(new InputAction("Ending Head Position", InputActionType.Value, "<XRHMD>/centerEyePosition"));
            tracking.rotationInput = new InputActionProperty(new InputAction("Ending Head Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation"));
            player.fallbackCamera = camera; player.enabled = true;
            ConfigurePhotoInScene(player, config);
            RenderSettings.skybox = null; RenderSettings.sun = null; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black; RenderSettings.ambientIntensity = 0f; RenderSettings.reflectionIntensity = 0f; RenderSettings.fog = false;
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int found = scenes.FindIndex(s => s.path == path);
            if (found < 0) scenes.Add(new EditorBuildSettingsScene(path, true)); else scenes[found].enabled = true;
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[Ending] Scene configured: 360 sphere, black environment, local XR view and full-video return flow.");
        }

        private static Material Material(string name, string shader)
        {
            string path = "Assets/sza/Rendering/Ending/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(material, path); }
            return material;
        }

        public static void ConfigureRenderer(EndingSequenceSettings config)
        {
            const string path = "Assets/sza/Rendering/Ending/EndingRenderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "EndingRenderer";
                AssetDatabase.CreateAsset(renderer, path);
            }
            config.presentationRenderer = renderer;
            EditorUtility.SetDirty(config); AssetDatabase.SaveAssetIfDirty(config);
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets" }))
            {
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                var data = new SerializedObject(pipeline); var list = data.FindProperty("m_RendererDataList");
                bool found = false;
                for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) found = true;
                if (found) continue;
                int index = list.arraySize; list.InsertArrayElementAtIndex(index); list.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
                data.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(pipeline);
            }
        }

        [MenuItem("Tools/Sza/片尾/配置老法师照片")]
        public static void ConfigurePhoto()
        {
            if (Application.isPlaying || AnimationMode.InAnimationMode()) throw new System.InvalidOperationException("请退出运行和 Timeline 预览。");
            var active = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/szaScene_end.unity");
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/szaScene_end.unity", OpenSceneMode.Additive);
            try
            {
                PanoramicEndingPlayer player = null;
                foreach (var root in scene.GetRootGameObjects())
                    if (root.GetComponent<PanoramicEndingPlayer>() != null) player = root.GetComponent<PanoramicEndingPlayer>();
                if (player == null) throw new System.InvalidOperationException("请先配置全景片尾场景。");
                ConfigurePhotoInScene(player, player.settings);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(active); }
        }

        private static void ConfigurePhotoInScene(PanoramicEndingPlayer player, EndingSequenceSettings config)
        {
            const string path = "Assets/sza/tex/老法师.jpg";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new System.InvalidOperationException("缺少老法师.jpg");
            if (importer.npotScale != TextureImporterNPOTScale.None || importer.maxTextureSize < 4096)
            {
                importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = Mathf.Max(4096, importer.maxTextureSize);
                importer.SaveAndReimport();
            }
            config.introductionPhoto = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var material = Material("EndingPhotoDissolve", "Sza/Ending/Photo Dissolve");
            material.SetTexture("_MainTex", config.introductionPhoto); material.SetFloat("_Progress", 1f);
            if (player.introductionPhoto == null)
            {
                var photo = GameObject.CreatePrimitive(PrimitiveType.Quad); photo.name = "老法师 · 溶解照片";
                SceneManager.MoveGameObjectToScene(photo, player.gameObject.scene);
                Object.DestroyImmediate(photo.GetComponent<Collider>()); photo.transform.SetParent(player.transform, false);
                photo.layer = player.presentationLayer; player.introductionPhoto = photo.GetComponent<Renderer>();
            }
            var renderer = player.introductionPhoto;
            renderer.transform.position = player.viewCenter.position + Vector3.forward * config.photoDistance;
            renderer.transform.rotation = Quaternion.identity;
            renderer.transform.localScale = new Vector3(config.photoHeight * config.introductionPhoto.width / config.introductionPhoto.height, config.photoHeight, 1f);
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            player.photoDissolveMaterial = material;
            EditorUtility.SetDirty(config); EditorUtility.SetDirty(material); EditorUtility.SetDirty(player);
            AssetDatabase.SaveAssetIfDirty(config); AssetDatabase.SaveAssetIfDirty(material);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
