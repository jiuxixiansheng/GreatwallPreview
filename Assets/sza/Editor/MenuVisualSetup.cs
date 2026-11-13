using System;
using System.Linq;
using Sza.Menu;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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

            // Copy only the controls: the headset sees the six world panels behind them.
            var vr = Object.Instantiate(desktop.gameObject).GetComponent<Canvas>();
            vr.name = "Menu VR Canvas";
            vr.renderMode = RenderMode.WorldSpace;
            vr.sortingOrder = 20;
            var vrRect = vr.GetComponent<RectTransform>();
            vrRect.anchorMin = vrRect.anchorMax = vrRect.pivot = new Vector2(0.5f, 0.5f);
            vrRect.sizeDelta = new Vector2(1920, 1080);
            vrRect.SetPositionAndRotation(new Vector3(0, 1.6f, 2.5f), Quaternion.identity);
            vrRect.localScale = Vector3.one * 0.0015f;
            vr.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            Object.DestroyImmediate(vr.GetComponent<GraphicRaycaster>());
            vr.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            // The desktop menu owns the connection logic; VR buttons forward to it.
            Object.DestroyImmediate(vr.GetComponent<NetworkMainMenu>());
            foreach (var child in vr.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 0;
            vr.gameObject.SetActive(false);

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
            const string materialPath = "Assets/sza/Rendering/Menu/MainVisual_Unlit.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
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
            origin.Camera.cullingMask &= ~(1 << 5);
            var xrAudio = origin.Camera.GetComponent<AudioListener>(); if (xrAudio != null) xrAudio.enabled = false;
            vr.worldCamera = origin.Camera;
            var desktopCamera = scene.GetRootGameObjects().Select(g => g.GetComponent<Camera>()).First(c => c != null);
            desktopCamera.stereoTargetEye = StereoTargetEyeMask.None;
            desktopCamera.clearFlags = CameraClearFlags.SolidColor; desktopCamera.backgroundColor = Color.black;
            desktopCamera.cullingMask = 0;
            desktop.renderMode = RenderMode.ScreenSpaceOverlay;
            desktop.worldCamera = null;
            presentation.desktopCamera = desktopCamera; presentation.desktopCanvas = desktop;
            presentation.vrCanvas = vr; presentation.vrOrigin = origin;
            var menu = new SerializedObject(desktop.GetComponent<NetworkMainMenu>());
            presentation.desktopHostButton = (Button)menu.FindProperty("serverButton").objectReferenceValue;
            presentation.desktopClientButton = (Button)menu.FindProperty("clientButton").objectReferenceValue;
            presentation.vrHostButton = vr.GetComponentsInChildren<Button>(true).First(b => b.name == presentation.desktopHostButton.name);
            presentation.vrClientButton = vr.GetComponentsInChildren<Button>(true).First(b => b.name == presentation.desktopClientButton.name);
            var events = Object.FindFirstObjectByType<EventSystem>();
            foreach (var old in events.GetComponents<BaseInputModule>()) Object.DestroyImmediate(old);
            var module = events.gameObject.AddComponent<XRUIInputModule>();
            module.enableXRInput = true; module.enableMouseInput = true; module.enableBuiltinActionsAsFallback = true;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) light.enabled = false;
            RenderSettings.skybox = null; RenderSettings.fog = false;
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = presentation.gameObject;
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
