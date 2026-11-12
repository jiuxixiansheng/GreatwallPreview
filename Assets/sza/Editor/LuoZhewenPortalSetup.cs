using System;
using System.Collections.Generic;
using Greatwall.Navigation.Timeline;
using Sza.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace Sza.Editor
{
    public static class LuoZhewenPortalSetup
    {
        public const string Folder = "Assets/sza/Characters/LuoZhewen/Portal";
        private const string Source = "Assets/Hovl Studio/Magic effects pack/Prefabs/Portals/Portal blue.prefab";
        private const string TrackName = "LZW · 传送门出场";

        [MenuItem("Tools/Sza/罗哲文/添加传送门出场")]
        public static void Configure()
        {
            if (Application.isPlaying || AnimationMode.InAnimationMode()) throw new InvalidOperationException("请先退出运行和 Timeline 预览。");
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/szaScene_Final.unity");
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("请先打开 szaScene_Final 场景。");
            TimelineCharacterActor actor = null; PlayableDirector director = null; GameObject chapter = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "LuoZhewen_root") actor = root.GetComponent<TimelineCharacterActor>();
                if (root.name == "part2-2") chapter = root;
                foreach (var d in root.GetComponentsInChildren<PlayableDirector>(true)) if (d.name == "timeline_02_2") director = d;
            }
            if (actor == null || actor.spawnPoint == null || director == null || chapter == null) throw new InvalidOperationException("罗哲文、出场点或时间线绑定缺失。");
            var timeline = director.playableAsset as TimelineAsset;
            GroupTrack group = null; NavMeshMoveTrack navigation = null; CharacterPortalTrack existing = null;
            foreach (var root in timeline.GetRootTracks())
            {
                var candidate = root as GroupTrack;
                if (candidate == null || !candidate.name.Contains("罗哲文")) continue;
                group = candidate;
                foreach (var track in group.GetChildTracks())
                {
                    if (track is NavMeshMoveTrack) navigation = (NavMeshMoveTrack)track;
                    if (track is CharacterPortalTrack) existing = (CharacterPortalTrack)track;
                }
            }
            if (group == null || navigation == null) throw new InvalidOperationException("罗哲文导航轨道不存在。");
            var moves = new List<TimelineClip>(navigation.GetClips()); moves.Sort((a,b) => a.start.CompareTo(b.start));
            if (moves.Count == 0) throw new InvalidOperationException("请先添加罗哲文出场导航片段。");
            var destination = ((NavMeshMoveClip)moves[0].asset).destination.Resolve(director);
            if (destination == null) throw new InvalidOperationException("出场导航终点未绑定。");
            Vector3 forward = Vector3.ProjectOnPlane(destination.transform.position - actor.spawnPoint.position, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) throw new InvalidOperationException("出场点与导航点距离过近。");
            forward.Normalize();
            EnsureFolder(Folder + "/Materials");
            CharacterPortalEntrance entrance = null;
            foreach (var item in chapter.GetComponentsInChildren<CharacterPortalEntrance>(true)) if (item.actor == actor) entrance = item;
            if (entrance == null)
            {
                var prefab = BuildPrefab();
                var portal = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(portal, "罗哲文传送门");
                portal.name = "LZW_PortalEntrance"; portal.transform.SetParent(chapter.transform, true);
                portal.transform.SetPositionAndRotation(actor.spawnPoint.position + forward * 0.9f + Vector3.up * 0.85f, Quaternion.LookRotation(forward));
                entrance = portal.GetComponent<CharacterPortalEntrance>(); entrance.actor = actor;
                PrefabUtility.RecordPrefabInstancePropertyModifications(entrance);
                PrefabUtility.RecordPrefabInstancePropertyModifications(portal.transform);
            }
            AddClippableEyeSurfaces(actor);
            if (existing == null)
            {
                Undo.RecordObject(timeline, "添加传送门轨道");
                existing = timeline.CreateTrack<CharacterPortalTrack>(group, TrackName);
                var clip = existing.CreateClip<CharacterPortalClip>();
                clip.displayName = "传送门打开 → 罗哲文走出 → 收起";
                clip.start = Math.Max(0d, moves[0].start - 0.6d); clip.duration = moves[0].end + 0.8d - clip.start;
            }
            director.SetGenericBinding(existing, entrance);
            EditorUtility.SetDirty(timeline); EditorUtility.SetDirty(existing); EditorUtility.SetDirty(director);
            AssetDatabase.SaveAssetIfDirty(timeline);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Debug.Log("[罗哲文] 传送门已接入。移动 LZW_PortalEntrance 可调整门面，蓝色 Z 轴为走出方向；拖动独立传送门轨道可调整时间。", entrance);
        }

        private static GameObject BuildPrefab()
        {
            string path = Folder + "/LZW_Portal.prefab";
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (saved != null) return saved;
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("LZW_PortalEntrance"); SceneManager.MoveGameObjectToScene(root, preview);
                var effect = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Source), preview);
                effect.name = "Portal blue · URP"; effect.transform.SetParent(root.transform, false);
                // The vendor prefab has a scene-placement offset; the wrapper owns its centre.
                effect.transform.localPosition = Vector3.zero; effect.transform.localRotation = Quaternion.identity;
                int seed = 7701;
                foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = ps.main; main.playOnAwake = false; main.loop = true; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    ps.useAutoRandomSeed = false; ps.randomSeed = (uint)seed++;
                    var renderer = ps.GetComponent<ParticleSystemRenderer>();
                    renderer.sharedMaterial = ParticleMaterial(renderer.sharedMaterial);
                    renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                }
                var entrance = root.AddComponent<CharacterPortalEntrance>();
                entrance.portalPlane = root.transform; entrance.effectRoot = effect;
                effect.transform.localScale = entrance.effectScale; effect.SetActive(false);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static Material ParticleMaterial(Material source)
        {
            string path = Folder + "/Materials/Portal_" + source.name + "_URP.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing != null) return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            material.name = "Portal_" + source.name + "_URP";
            material.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
            material.SetTextureScale("_BaseMap", source.GetTextureScale("_MainTex"));
            material.SetTextureOffset("_BaseMap", source.GetTextureOffset("_MainTex"));
            material.SetColor("_BaseColor", source.GetColor("_Color"));
            material.SetFloat("_Surface",1); material.SetFloat("_Blend",0); material.SetFloat("_ZWrite",0); material.SetFloat("_Cull",0);
            material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha",(float)BlendMode.One); material.SetFloat("_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_FlipbookBlending",0); material.SetFloat("_ColorMode",0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.SetOverrideTag("RenderType","Transparent"); material.renderQueue=3000;
            material.SetShaderPassEnabled("ShadowCaster",false); material.SetShaderPassEnabled("DepthOnly",false); material.SetShaderPassEnabled("DepthNormals",false);
            AssetDatabase.CreateAsset(material,path); return material;
        }

        private static void AddClippableEyeSurfaces(TimelineCharacterActor actor)
        {
            foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials; bool changed = false;
                for (int i=0;i<materials.Length;i++)
                {
                    var source = materials[i];
                    if (source == null || source.HasProperty("_SzaPortalClipEnabled") || !source.HasProperty("_BaseColor") || source.GetColor("_BaseColor").a<=0f) continue;
                    if (source.shader.name != "Universal Render Pipeline/Lit") throw new InvalidOperationException("尚未支持的罗哲文可见材质："+source.name);
                    string path = Folder+"/Materials/"+source.name+"_Portal.mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null)
                    {
                        material = new Material(Shader.Find("Sza/Portal/Eye Surface URP"));
                        material.CopyPropertiesFromMaterial(source); material.shaderKeywords = Array.Empty<string>(); material.renderQueue = source.renderQueue;
                        AssetDatabase.CreateAsset(material,path);
                    }
                    materials[i]=material; changed=true;
                }
                if (changed) {Undo.RecordObject(renderer,"支持眼部传送门裁切"); renderer.sharedMaterials=materials; PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);}
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent=System.IO.Path.GetDirectoryName(path).Replace('\\','/'); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent,System.IO.Path.GetFileName(path));
        }
    }
}
