using System;
using System.Linq;
using Greatwall.VRAnimation.Timeline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Xcy.CrackRepair.Editor
{
    public static class Part2CrackRepairSetup
    {
        private const string ScenePath = "Assets/Scenes/szaScene0921.unity";
        private const string MaterialPath = "Assets/Xcy/CrackRepair/M_CrackRepairDecal.mat";
        private const string TexturePath = "Assets/texture/1_alpha.png";
        private const string RendererPath = "Assets/Xcy/Rendering/Xcy_PC_Renderer.asset";
        private static readonly string[] CrackNames = { "crack1", "crack1 (1)", "crack1 (2)", "crack1 (3)" };

        [MenuItem("Xcy/Part2/Install Crack Repair")]
        public static void Install()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                throw new InvalidOperationException("Open szaScene0921 before installing Part 2 crack repair.");

            var part2 = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Part2");
            var group = part2 != null ? part2.transform.Find("GameObject") : null;
            if (group == null) throw new InvalidOperationException("Part2/GameObject was not found.");

            var manager = UnityEngine.Object.FindFirstObjectByType<StoryTimelineManager>(FindObjectsInactive.Include);
            if (manager == null) throw new InvalidOperationException("StoryTimelineManager was not found.");
            var managerSerialized = new SerializedObject(manager);
            var chapters = managerSerialized.FindProperty("chapters");
            if (chapters == null || chapters.arraySize <= 2)
                throw new InvalidOperationException("The chapter2 entry was not found.");
            var chapter2 = chapters.GetArrayElementAtIndex(2);
            if (chapter2.FindPropertyRelative("chapterName").stringValue != "chapter2")
                throw new InvalidOperationException("Chapter index 2 is not chapter2.");
            var director = chapter2.FindPropertyRelative("director").objectReferenceValue as PlayableDirector;
            var relay = director != null ? director.GetComponent<TimelineCompletionRelay>() : null;
            if (relay == null) throw new InvalidOperationException("timeline_02 has no TimelineCompletionRelay.");

            var sources = CrackNames.Select(name => group.Find(name)).ToArray();
            if (sources.Any(source => source == null))
                throw new InvalidOperationException("One of the four crack objects is missing under Part2/GameObject.");

            InstallRendererFeature();
            var material = GetOrCreateMaterial();
            var coordinator = group.GetComponent<Part2CrackRepairCoordinator>();
            if (coordinator == null) coordinator = Undo.AddComponent<Part2CrackRepairCoordinator>(group.gameObject);
            var repairs = new CrackRepairDecal[sources.Length];

            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                var meshRenderer = source.GetComponent<MeshRenderer>();
                var meshFilter = source.GetComponent<MeshFilter>();
                var meshCollider = source.GetComponent<MeshCollider>();
                if (meshRenderer == null || meshFilter == null || meshCollider == null)
                    throw new InvalidOperationException(source.name + " is missing its Plane renderer or collider.");

                var interactable = source.GetComponent<XRSimpleInteractable>();
                if (interactable == null) interactable = Undo.AddComponent<XRSimpleInteractable>(source.gameObject);
                if (!interactable.colliders.Contains(meshCollider)) interactable.colliders.Add(meshCollider);

                var repair = source.GetComponent<CrackRepairDecal>();
                if (repair == null) repair = Undo.AddComponent<CrackRepairDecal>(source.gameObject);
                repairs[i] = repair;

                var projectorTransform = source.Find("Crack Decal");
                if (projectorTransform == null)
                {
                    var decalObject = new GameObject("Crack Decal");
                    Undo.RegisterCreatedObjectUndo(decalObject, "Create crack decal");
                    projectorTransform = decalObject.transform;
                    projectorTransform.SetParent(source, true);
                }

                // The Plane's UV axes are reversed relative to its local X and Z axes.
                // This orientation keeps the existing texture aligned while projecting into the wall.
                projectorTransform.SetPositionAndRotation(source.position + source.up * 0.12f,
                    Quaternion.LookRotation(-source.up, -source.forward));
                projectorTransform.localScale = Vector3.one;

                var projector = projectorTransform.GetComponent<DecalProjector>();
                if (projector == null) projector = Undo.AddComponent<DecalProjector>(projectorTransform.gameObject);
                var meshBounds = meshFilter.sharedMesh.bounds.size;
                float width = meshBounds.x * Mathf.Abs(source.lossyScale.x) * 1.02f;
                float height = meshBounds.z * Mathf.Abs(source.lossyScale.z) * 1.02f;
                projector.scaleMode = DecalScaleMode.ScaleInvariant;
                projector.material = material;
                projector.size = new Vector3(width, height, 0.35f);
                projector.pivot = new Vector3(0f, 0f, 0.175f);
                projector.uvScale = Vector2.one;
                projector.uvBias = Vector2.zero;
                projector.fadeFactor = 1f;
                projector.drawDistance = 20f;
                projector.startAngleFade = 55f;
                projector.endAngleFade = 80f;

                var repairSerialized = new SerializedObject(repair);
                repairSerialized.FindProperty("projector").objectReferenceValue = projector;
                repairSerialized.FindProperty("originalPlaneRenderer").objectReferenceValue = meshRenderer;
                repairSerialized.FindProperty("interactable").objectReferenceValue = interactable;
                repairSerialized.FindProperty("coordinator").objectReferenceValue = coordinator;
                repairSerialized.ApplyModifiedProperties();

                Undo.RecordObject(meshRenderer, "Use crack decal instead of Plane visual");
                meshRenderer.enabled = false;
                EditorUtility.SetDirty(meshRenderer);
            }

            var coordinatorSerialized = new SerializedObject(coordinator);
            coordinatorSerialized.FindProperty("storyManager").objectReferenceValue = manager;
            coordinatorSerialized.FindProperty("part2TimelineRelay").objectReferenceValue = relay;
            var crackArray = coordinatorSerialized.FindProperty("cracks");
            crackArray.arraySize = repairs.Length;
            for (int i = 0; i < repairs.Length; i++)
                crackArray.GetArrayElementAtIndex(i).objectReferenceValue = repairs[i];
            coordinatorSerialized.ApplyModifiedProperties();

            Undo.RecordObject(manager, "Wait for four cracks before leaving Part 2");
            chapter2.FindPropertyRelative("switchWhenTimelineCompleted").boolValue = false;
            managerSerialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Part 2 crack repair installed: four decals, independent repair interactions, and chapter completion gate.");
        }

        private static Material GetOrCreateMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;

            var shader = Shader.Find("Shader Graphs/Decal");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (shader == null || texture == null)
                throw new InvalidOperationException("The URP Decal shader or 1_alpha.png is unavailable.");

            material = new Material(shader) { name = "M_CrackRepairDecal", enableInstancing = true };
            material.SetTexture("Base_Map", texture);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static void InstallRendererFeature()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null) throw new InvalidOperationException("Xcy_PC_Renderer is missing.");
            if (rendererData.rendererFeatures.OfType<DecalRendererFeature>().Any()) return;

            var feature = ScriptableObject.CreateInstance<DecalRendererFeature>();
            feature.name = "Part2 Decals";
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            var featureSettings = new SerializedObject(feature);
            var settings = featureSettings.FindProperty("m_Settings");
            settings.FindPropertyRelative("technique").enumValueIndex = 2; // Screen Space
            settings.FindPropertyRelative("maxDrawDistance").floatValue = 20f;
            settings.FindPropertyRelative("decalLayers").boolValue = false;
            featureSettings.ApplyModifiedPropertiesWithoutUndo();
            feature.Create();

            var rendererSerialized = new SerializedObject(rendererData);
            var features = rendererSerialized.FindProperty("m_RendererFeatures");
            var map = rendererSerialized.FindProperty("m_RendererFeatureMap");
            int index = features.arraySize;
            features.arraySize = index + 1;
            features.GetArrayElementAtIndex(index).objectReferenceValue = feature;
            map.arraySize = index + 1;
            map.GetArrayElementAtIndex(index).longValue = localId;
            rendererSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
        }
    }
}
