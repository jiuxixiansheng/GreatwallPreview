using System;
using Greatwall.VRAnimation.Timeline;
using Sza.Prologue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Xcy.Audio;

namespace Sza.Editor
{
    public static class ProloguePresentationSetup
    {
        private const string NarrationTrackName = "VO 画外音（1×2 → 2）";
        private const string MaterialPath = "Assets/sza/mat/Prologue/M_PrologueWall_Breathing.mat";

        [MenuItem("Tools/Sza/配置第零章配音与石壁呼吸发光")]
        public static void Configure()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || AnimationMode.InAnimationMode() ||
                scene.path != "Assets/Scenes/szaScene_Final.unity")
                throw new InvalidOperationException("请在非运行状态打开 Final 场景，并退出 Timeline 预览。");

            InteractionAudioPlayer audio = null;
            PlayableDirector director = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var candidate in root.GetComponentsInChildren<InteractionAudioPlayer>(true)) audio = candidate;
                foreach (var candidate in root.GetComponentsInChildren<PlayableDirector>(true))
                    if (candidate.name == "timeline_00") director = candidate;
            }
            if (audio == null || audio.story == null || audio.prologueTarget == null || director == null)
                throw new InvalidOperationException("缺少序章石壁、StoryManager 或 timeline_00。");
            var timeline = director.playableAsset as TimelineAsset;
            var voice1 = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/sza/voice/画外音1.WAV");
            var voice2 = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/sza/voice/画外音2.WAV");
            var renderer = audio.prologueTarget.GetComponent<Renderer>();
            if (timeline == null || voice1 == null || voice2 == null || renderer == null)
                throw new InvalidOperationException("缺少配音文件、Timeline 资源或石壁 Renderer。");

            Undo.RecordObjects(new UnityEngine.Object[] { director, timeline, renderer, audio.story }, "Configure prologue presentation");
            AudioTrack track = null;
            foreach (var candidate in timeline.GetOutputTracks())
                if (candidate.name == NarrationTrackName) track = candidate as AudioTrack;
            if (track == null) track = timeline.CreateTrack<AudioTrack>(null, NarrationTrackName);
            foreach (var clip in new System.Collections.Generic.List<TimelineClip>(track.GetClips()))
                timeline.DeleteClip(clip);

            double length1 = (double)voice1.samples / voice1.frequency;
            double length2 = (double)voice2.samples / voice2.frequency;
            AddVoice(track, voice1, 0d, length1, "画外音1 · 第1遍");
            AddVoice(track, voice1, length1, length1, "画外音1 · 第2遍");
            AddVoice(track, voice2, length1 * 2d, length2, "画外音2");
            var trackSettings = new SerializedObject(track);
            trackSettings.FindProperty("m_TrackProperties.volume").floatValue = 1f;
            trackSettings.FindProperty("m_TrackProperties.spatialBlend").floatValue = 0f;
            trackSettings.ApplyModifiedProperties();
            double end = length1 * 2d + length2;

            foreach (var candidate in timeline.GetOutputTracks())
            {
                if (candidate == track || !(candidate is AudioTrack)) continue;
                foreach (var clip in candidate.GetClips())
                {
                    var asset = clip.asset as AudioPlayableAsset;
                    if (asset != null && asset.loop && clip.start == 0d && clip.duration < end)
                    {
                        clip.duration = end;
                        clip.displayName = "序章环境 · 配音期间循环";
                    }
                }
            }

            var sourceTransform = director.transform.Find("VO 画外音");
            if (sourceTransform == null)
            {
                var go = new GameObject("VO 画外音", typeof(AudioSource));
                Undo.RegisterCreatedObjectUndo(go, "Create prologue narration source");
                go.transform.SetParent(director.transform, false);
                sourceTransform = go.transform;
            }
            var source = sourceTransform.GetComponent<AudioSource>();
            if (source == null) source = Undo.AddComponent<AudioSource>(sourceTransform.gameObject);
            Undo.RecordObject(source, "Configure narration source");
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 1f;
            director.SetGenericBinding(track, source);
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.Hold;

            var story = new SerializedObject(audio.story);
            var chapter = story.FindProperty("chapters").GetArrayElementAtIndex(0);
            chapter.FindPropertyRelative("useTimeline").boolValue = true;
            chapter.FindPropertyRelative("director").objectReferenceValue = director;
            chapter.FindPropertyRelative("switchWhenTimelineCompleted").boolValue = false;
            story.ApplyModifiedProperties();

            if (!AssetDatabase.IsValidFolder("Assets/sza/mat/Prologue"))
                AssetDatabase.CreateFolder("Assets/sza/mat", "Prologue");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(renderer.sharedMaterial) { name = "M_PrologueWall_Breathing" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            Undo.RecordObject(material, "Configure wall emission");
            var pulse = renderer.GetComponent<PrologueWallBreathingGlow>();
            if (pulse == null) pulse = Undo.AddComponent<PrologueWallBreathingGlow>(renderer.gameObject);
            Undo.RecordObject(pulse, "Bind wall pulse");
            pulse.targetRenderer = renderer;
            pulse.story = audio.story;
            pulse.materialIndex = 0;
            material.SetColor("_EmissionColor", pulse.glowColor * pulse.minimumIntensity);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_EmissionMap", material.GetTexture("_BaseMap"));
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.EnableKeyword("_EMISSION");
            var materials = renderer.sharedMaterials;
            materials[0] = material;
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);

            foreach (var asset in new UnityEngine.Object[] { director, timeline, track, source, renderer, material, pulse, audio.story })
                EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(timeline);
            AssetDatabase.SaveAssetIfDirty(material);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("场景保存失败。");
            Debug.Log("[序章] 画外音1播放两遍后接画外音2，石壁呼吸发光已配置。", renderer);
        }

        private static void AddVoice(AudioTrack track, AudioClip audio, double start, double duration, string name)
        {
            var clip = track.CreateClip<AudioPlayableAsset>();
            var asset = (AudioPlayableAsset)clip.asset;
            asset.clip = audio;
            asset.loop = false;
            var clipSettings = new SerializedObject(asset);
            clipSettings.FindProperty("m_ClipProperties.volume").floatValue = 1f;
            clipSettings.ApplyModifiedProperties();
            clip.displayName = name;
            clip.start = start;
            clip.duration = duration;
            clip.clipIn = 0d;
            clip.timeScale = 1d;
            clip.easeInDuration = clip.easeOutDuration = 0d;
            EditorUtility.SetDirty(asset);
        }
    }
}
