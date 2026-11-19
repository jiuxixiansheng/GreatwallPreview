using System;
using System.Linq;
using Sza.Menu;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

namespace Sza.Editor
{
    public static class MenuVisualSetup
    {
        private const string ImagePath = "Assets/sza/tex/主视觉.jpg";
        private const string TeaserPath = "Assets/sza/video/先导片.mp4";
        private const string MaterialPath = "Assets/sza/Rendering/Menu/MainVisual_Unlit.mat";
        private const string RigPath = "Assets/sza/prefab/Menu_VR_Viewer.prefab";
        private const string StarterRig = "Assets/Samples/XR Interaction Toolkit/3.0.10/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

        [MenuItem("Tools/Sza/配置主视觉菜单与VR等候空间")]
        public static void Configure()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || scene.path != "Assets/Scenes/Menu.unity")
                throw new InvalidOperationException("请先在非运行状态打开 Menu 场景。");

            var importer = (TextureImporter)AssetImporter.GetAtPath(ImagePath);
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            var image = AssetDatabase.LoadAssetAtPath<Texture2D>(ImagePath);
            if (image == null) throw new InvalidOperationException("缺少主视觉图片。");

            Selection.activeObject = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "Menu Visual Presentation" || root.name == "Menu VR Canvas" || root.name == "Menu VR Viewer")
                    Object.DestroyImmediate(root);
            var desktop = scene.GetRootGameObjects().Select(g => g.GetComponent<Canvas>()).First(c => c != null);
            var background = desktop.transform.Find("Main Visual Background");
            if (background != null) Object.DestroyImmediate(background.gameObject);
            var scaler = desktop.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            StyleButtons(desktop);

            var bg = new GameObject("Main Visual Background", typeof(RectTransform), typeof(RawImage));
            bg.transform.SetParent(desktop.transform, false);
            bg.transform.SetAsFirstSibling();
            var rect = bg.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var raw = bg.GetComponent<RawImage>();
            raw.texture = image; raw.color = Color.white; raw.raycastTarget = false;
            foreach (var child in desktop.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;

            var presentation = new GameObject("Menu Visual Presentation").AddComponent<MenuPresentation>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("缺少 URP Unlit Shader。");
            if (!AssetDatabase.IsValidFolder("Assets/sza/Rendering/Menu")) AssetDatabase.CreateFolder("Assets/sza/Rendering", "Menu");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.SetTexture("_BaseMap", image); material.SetColor("_BaseColor", Color.white);
            material.renderQueue = 2000;
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up, Vector3.down };
            string[] names = { "Front", "Back", "Left", "Right", "Ceiling", "Floor" };
            for (int i = 0; i < directions.Length; i++)
            {
                var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
                panel.name = "Main Visual - " + names[i];
                Object.DestroyImmediate(panel.GetComponent<Collider>());
                panel.transform.SetParent(presentation.transform, false);
                panel.transform.position = Vector3.up * 1.6f + directions[i] * 10f;
                panel.transform.rotation = Quaternion.LookRotation(directions[i], i >= 4 ? Vector3.forward : Vector3.up);
                panel.transform.localScale = new Vector3(20.02f, 20.02f, 1f);
                var renderer = panel.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            var interactionManager = Object.FindFirstObjectByType<XRInteractionManager>();
            if (interactionManager == null) interactionManager = new GameObject("Menu XR Interaction Manager").AddComponent<XRInteractionManager>();
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(CreateRig());
            rig.name = "Menu VR Viewer"; rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var origin = rig.GetComponent<XROrigin>();
            foreach (var interactor in rig.GetComponentsInChildren<XRBaseInteractor>(true)) interactor.interactionManager = interactionManager;
            origin.Camera.enabled = false; origin.Camera.tag = "Untagged";
            var xrAudio = origin.Camera.GetComponent<AudioListener>(); if (xrAudio != null) xrAudio.enabled = false;
            presentation.desktopCamera = scene.GetRootGameObjects().Select(g => g.GetComponent<Camera>()).First(c => c != null);
            presentation.vrOrigin = origin;
            ConfigureMenuPlayback(presentation);
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) light.enabled = false;
            RenderSettings.skybox = null; RenderSettings.fog = false;
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = presentation.gameObject;
        }

        public static void ConfigureMenuPlayback(MenuPresentation presentation)
        {
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(TeaserPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (clip == null || material == null)
                throw new InvalidOperationException("缺少菜单先导片或主视觉材质。");

            var roots = presentation.gameObject.scene.GetRootGameObjects();
            var desktop = roots.First(go => go.GetComponent<NetworkMainMenu>() != null).GetComponent<Canvas>();
            var events = roots.Select(go => go.GetComponent<EventSystem>()).First(system => system != null);
            var desktopCamera = presentation.desktopCamera;
            var headsetCamera = presentation.vrOrigin.Camera;
            foreach (var root in roots)
                if (root.name == "Menu VR Canvas") Object.DestroyImmediate(root);

            // URP blits the XR mirror after the headset camera and then renders the monitor UI.
            desktopCamera.depth = Mathf.Max(100f, headsetCamera.depth + 1f);
            desktopCamera.enabled = true;
            desktopCamera.stereoTargetEye = StereoTargetEyeMask.None;
            desktopCamera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            desktopCamera.clearFlags = CameraClearFlags.SolidColor;
            desktopCamera.backgroundColor = Color.black;
            int desktopLayer = 1 << desktop.gameObject.layer;
            desktopCamera.cullingMask = desktopLayer;
            headsetCamera.cullingMask &= ~desktopLayer;
            desktop.renderMode = RenderMode.ScreenSpaceCamera;
            desktop.worldCamera = desktopCamera;
            desktop.planeDistance = 1f;

            var module = events.GetComponent<XRUIInputModule>();
            if (module == null) module = events.gameObject.AddComponent<XRUIInputModule>();
            foreach (var old in events.GetComponents<BaseInputModule>())
                if (old != module) Object.DestroyImmediate(old);
            events.sendNavigationEvents = false;
            module.enableXRInput = false;
            module.enableGamepadInput = false;
            module.enableJoystickInput = false;
            module.enableMouseInput = true;
            module.enableBuiltinActionsAsFallback = true;

            var screen = presentation.vrIntroScreen;
            if (screen == null)
            {
                var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
                panel.name = "先导片屏幕";
                panel.transform.SetParent(presentation.transform, false);
                panel.transform.localPosition = new Vector3(0f, 1.65f, 3.5f);
                const float width = 3.6f;
                panel.transform.localScale = new Vector3(width, width * clip.height / clip.width, 1f);
                Object.DestroyImmediate(panel.GetComponent<Collider>());
                screen = panel.AddComponent<MenuIntroScreen>();
            }

            var renderer = screen.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            var video = screen.GetComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.source = VideoSource.VideoClip;
            video.clip = clip;
            video.isLooping = true;
            var audio = screen.GetComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            screen.gameObject.SetActive(false);
            presentation.vrIntroScreen = screen;
        }

        private static GameObject CreateRig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            if (existing != null) return existing;
            var root = PrefabUtility.LoadPrefabContents(StarterRig);
            try
            {
                foreach (var provider in root.GetComponentsInChildren<LocomotionProvider>(true)) provider.enabled = false;
                foreach (var controller in root.GetComponentsInChildren<CharacterController>(true)) controller.enabled = false;
                foreach (var actionManager in root.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.ControllerInputActionManager>(true))
                {
                    var so = new SerializedObject(actionManager);
                    foreach (string name in new[] { "m_Move", "m_Turn", "m_SnapTurn", "m_TeleportMode", "m_TeleportModeCancel" })
                        so.FindProperty(name).objectReferenceValue = null;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                root.GetComponent<XROrigin>().CameraYOffset = 1.6f;
                return PrefabUtility.SaveAsPrefabAsset(root, RigPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void StyleButtons(Canvas canvas)
        {
            foreach (var button in canvas.GetComponentsInChildren<Button>(true))
            {
                Color normal = new Color32(59, 41, 26, 255);
                var colors = button.colors;
                colors.normalColor = normal; colors.highlightedColor = new Color32(94, 63, 36, 255);
                colors.pressedColor = new Color32(41, 28, 18, 255); colors.selectedColor = new Color32(121, 85, 46, 255);
                colors.disabledColor = new Color32(73, 65, 56, 180); colors.colorMultiplier = 1;
                button.colors = colors; button.image.color = Color.white;
                foreach (var label in button.GetComponentsInChildren<Text>(true)) label.color = new Color32(255, 242, 212, 255);
                var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color32(211, 170, 106, 255); outline.effectDistance = new Vector2(2, -2);
                var shadow = button.GetComponents<Shadow>().FirstOrDefault(s => !(s is Outline)) ?? button.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0, 0, 0, 0.4f); shadow.effectDistance = new Vector2(0, -4);
            }
        }
    }
}
