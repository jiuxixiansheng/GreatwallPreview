using System;
using Sza.Telescope;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Sza.Editor
{
    public static class TelescopeVideoSetup
    {
        [MenuItem("Tools/Sza/配置望远镜视频交互")]
        public static void Configure()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || scene.path != "Assets/Scenes/szaScene_Final.unity")
                throw new InvalidOperationException("请在非运行状态打开 szaScene_Final。");
            GameObject telescope = null, networkManager = null;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "wangyuanjing" && t.parent != null && t.parent.name == "part2-2")
                        telescope = t.gameObject;
                    if (t.name == "NetworkManager") networkManager = t.gameObject;
                }
            if (telescope == null || networkManager == null) throw new InvalidOperationException("缺少望远镜或 NetworkManager。");
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/sza/video/春.mp4");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/sza/Rendering/Telescope/TelescopeVideo.shader");
            var font = Resources.Load<Font>("NotoSansSC-Guide");
            if (clip == null || shader == null || font == null) throw new InvalidOperationException("缺少春.mp4、遮罩 Shader 或中文字体。");

            var manager = networkManager.GetComponent<TelescopeVideoManager>();
            if (manager == null) manager = Undo.AddComponent<TelescopeVideoManager>(networkManager);
            var interaction = telescope.GetComponent<TelescopeVideoInteraction>();
            if (interaction == null) interaction = Undo.AddComponent<TelescopeVideoInteraction>(telescope);
            var collider = telescope.GetComponent<BoxCollider>();
            if (collider == null) collider = Undo.AddComponent<BoxCollider>(telescope);
            Undo.RecordObjects(new UnityEngine.Object[] { manager, interaction, collider }, "Configure telescope video");
            var mesh = telescope.GetComponent<MeshFilter>();
            if (mesh != null && mesh.sharedMesh != null)
            {
                collider.center = mesh.sharedMesh.bounds.center;
                collider.size = mesh.sharedMesh.bounds.size;
            }
            collider.isTrigger = true;
            manager.interaction = interaction;
            manager.videoClip = clip;
            manager.maskShader = shader;
            manager.playOnce = true;
            interaction.manager = manager;
            interaction.touchVolume = collider;

            var existing = telescope.transform.Find("TelescopeTouchPrompt");
            if (existing == null)
            {
                var prompt = new GameObject("TelescopeTouchPrompt", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
                Undo.RegisterCreatedObjectUndo(prompt, "Create telescope prompt");
                prompt.transform.SetParent(telescope.transform, false);
                var bounds = new Bounds(telescope.transform.position, Vector3.zero);
                if (mesh != null && mesh.sharedMesh != null)
                {
                    var local = mesh.sharedMesh.bounds;
                    bounds = new Bounds(telescope.transform.TransformPoint(local.center), Vector3.zero);
                    for (int i = 0; i < 8; i++)
                        bounds.Encapsulate(telescope.transform.TransformPoint(new Vector3(
                            (i & 1) == 0 ? local.min.x : local.max.x,
                            (i & 2) == 0 ? local.min.y : local.max.y,
                            (i & 4) == 0 ? local.min.z : local.max.z)));
                }
                Vector3 anchor = bounds.center + Vector3.up * (bounds.extents.y + 0.22f);
                prompt.transform.position = anchor;
                Vector3 parentScale = telescope.transform.lossyScale;
                prompt.transform.localScale = new Vector3(0.001f / Mathf.Abs(parentScale.x),
                    0.001f / Mathf.Abs(parentScale.y), 0.001f / Mathf.Abs(parentScale.z));
                ((RectTransform)prompt.transform).sizeDelta = new Vector2(600f, 110f);
                var canvas = prompt.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.sortingOrder = 80;
                var group = prompt.GetComponent<CanvasGroup>();
                group.blocksRaycasts = group.interactable = false;
                var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
                background.transform.SetParent(prompt.transform, false);
                Stretch((RectTransform)background.transform);
                var image = background.GetComponent<Image>();
                image.color = new Color(0.025f, 0.055f, 0.07f, 0.82f);
                image.raycastTarget = false;
                var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
                label.transform.SetParent(prompt.transform, false);
                Stretch((RectTransform)label.transform);
                var text = label.GetComponent<Text>();
                text.font = font;
                text.text = "请触碰望远镜";
                text.fontSize = 55;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = new Color(1f, 0.91f, 0.65f);
                text.raycastTarget = false;
                var outline = label.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
                outline.effectDistance = new Vector2(1.2f, -1.2f);
                existing = prompt.transform;
            }
            interaction.promptRoot = existing;
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(interaction);
            EditorUtility.SetDirty(collider);
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("场景保存失败。");
            Selection.activeGameObject = telescope;
            Debug.Log("[望远镜] 已接入触碰提示、全员同步视频与淡入淡出，剧情继续运行。", telescope);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
