using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Xcy.Boundary.Editor
{
    public static class PartBoundarySceneSetup
    {
        private const string Folder = "Assets/Xcy/Boundary";

        private struct ZoneDefault
        {
            public string rootName;
            public Vector3 center;
            public Vector3 size;
            public float guideHeight;

            public ZoneDefault(string rootName, Vector3 center, Vector3 size, float guideHeight)
            {
                this.rootName = rootName;
                this.center = center;
                this.size = size;
                this.guideHeight = guideHeight;
            }
        }

        private static readonly ZoneDefault[] Defaults =
        {
            new ZoneDefault("part0", new Vector3(-2f, 7.5f, -1.5f), new Vector3(22f, 15f, 20f), 0.05f),
            new ZoneDefault("part1", new Vector3(-10.4f, 20f, 3.45f), new Vector3(150f, 40f, 122f), 4.5f),
            new ZoneDefault("Part2", new Vector3(-12.84f, 20f, -10.1f), new Vector3(40f, 40f, 40f), 4.5f),
            new ZoneDefault("part2-2", new Vector3(-20.51f, 20f, -1.99f), new Vector3(65f, 40f, 65f), 4.5f)
        };

        [MenuItem("Tools/Xcy/Configure Part Boundaries")]
        public static void Configure()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != "Assets/Scenes/szaScene0921.unity")
                throw new InvalidOperationException("请先打开 szaScene0921 场景。");

            GameObject[] roots = scene.GetRootGameObjects();
            GameObject rig = Array.Find(roots, root => root.name == "sza_XR Origin (XR Rig)");
            Camera camera = rig != null ? rig.GetComponentInChildren<Camera>(true) : null;
            if (camera == null) throw new InvalidOperationException("找不到 sza_XR Origin 下的玩家相机。");

            Material blackout = GetOrCreateMaterial("BoundaryBlackout.mat", "Xcy/Boundary/Blackout");
            Material guide = GetOrCreateMaterial("BoundaryGuide.mat", "Xcy/Boundary/Guide");

            GameObject system = Array.Find(roots, root => root.name == "Xcy Boundary System");
            if (system == null) system = new GameObject("Xcy Boundary System");
            PartBoundaryEffect effect = system.GetComponent<PartBoundaryEffect>();
            if (effect == null) effect = system.AddComponent<PartBoundaryEffect>();

            var zones = new List<PartBoundaryZone>();
            foreach (ZoneDefault item in Defaults)
            {
                GameObject partRoot = Array.Find(roots, root => root.name == item.rootName);
                if (partRoot == null) throw new InvalidOperationException("缺少 Part 根物体：" + item.rootName);

                Transform existing = system.transform.Find("Boundary - " + item.rootName);
                GameObject zoneObject = existing != null ? existing.gameObject : new GameObject("Boundary - " + item.rootName);
                if (existing == null)
                {
                    zoneObject.transform.SetParent(system.transform, false);
                    zoneObject.transform.position = item.center;
                    BoxCollider box = zoneObject.AddComponent<BoxCollider>();
                    box.size = item.size;
                    box.isTrigger = true;
                }

                PartBoundaryZone zone = zoneObject.GetComponent<PartBoundaryZone>();
                if (zone == null) zone = zoneObject.AddComponent<PartBoundaryZone>();
                SerializedObject zoneData = new SerializedObject(zone);
                zoneData.FindProperty("sceneRoot").objectReferenceValue = partRoot;
                zoneData.FindProperty("volume").objectReferenceValue = zoneObject.GetComponent<BoxCollider>();
                if (existing == null) zoneData.FindProperty("guideHeightOffset").floatValue = item.guideHeight;
                zoneData.ApplyModifiedPropertiesWithoutUndo();
                zones.Add(zone);
            }

            SerializedObject effectData = new SerializedObject(effect);
            effectData.FindProperty("playerCamera").objectReferenceValue = camera;
            effectData.FindProperty("blackoutMaterial").objectReferenceValue = blackout;
            effectData.FindProperty("guideMaterial").objectReferenceValue = guide;
            SerializedProperty zoneArray = effectData.FindProperty("zones");
            zoneArray.arraySize = zones.Count;
            for (int i = 0; i < zones.Count; i++)
                zoneArray.GetArrayElementAtIndex(i).objectReferenceValue = zones[i];
            effectData.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Xcy Part 边界已配置：part0、part1、Part2、part2-2（chapter3 共用 part2-2）。", system);
        }

        private static Material GetOrCreateMaterial(string fileName, string shaderName)
        {
            string path = Folder + "/" + fileName;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("找不到 Shader：" + shaderName);
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
