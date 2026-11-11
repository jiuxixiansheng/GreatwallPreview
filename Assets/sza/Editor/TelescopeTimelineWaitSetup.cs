using System;
using Sza.Telescope;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Sza.Editor
{
    public static class TelescopeTimelineWaitSetup
    {
        private const string SignalPath = "Assets/sza/timeline/Telescope_02_2_Wait.signal";
        private const string TrackName = "Telescope - Wait at Frame 8330";

        [MenuItem("Tools/Sza/配置望远镜剧情等待（8330帧）")]
        public static void Configure()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Application.isPlaying || scene.path != "Assets/Scenes/szaScene_Final.unity" ||
                AnimationMode.InAnimationMode())
                throw new InvalidOperationException("请在非运行状态打开 Final 场景，并退出 Timeline 预览。");

            TelescopeVideoManager manager = null;
            PlayableDirector director = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var candidate in root.GetComponentsInChildren<TelescopeVideoManager>(true))
                    manager = candidate;
                foreach (var candidate in root.GetComponentsInChildren<PlayableDirector>(true))
                    if (candidate.name == "timeline_02_2") director = candidate;
            }
            if (manager == null || director == null || !(director.playableAsset is TimelineAsset))
                throw new InvalidOperationException("缺少 TelescopeVideoManager 或 timeline_02_2。");

            var timeline = (TimelineAsset)director.playableAsset;
            var signal = AssetDatabase.LoadAssetAtPath<SignalAsset>(SignalPath);
            if (signal == null)
            {
                signal = ScriptableObject.CreateInstance<SignalAsset>();
                AssetDatabase.CreateAsset(signal, SignalPath);
            }
            var receiver = manager.GetComponent<SignalReceiver>();
            if (receiver == null) receiver = Undo.AddComponent<SignalReceiver>(manager.gameObject);
            Undo.RecordObjects(new UnityEngine.Object[] { manager, receiver, director, timeline },
                "Configure telescope Timeline wait");
            manager.waitingTimeline = director;

            // This signal owns its reaction; leave all other NetworkManager signals untouched.
            var reaction = receiver.GetReaction(signal);
            if (reaction == null)
            {
                reaction = new UnityEvent();
                receiver.AddReaction(signal, reaction);
            }
            for (int i = reaction.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(reaction, i);
            UnityEventTools.AddPersistentListener(reaction, manager.WaitForVideo);

            SignalTrack waitTrack = null;
            foreach (var track in timeline.GetOutputTracks())
                if (track.name == TrackName) waitTrack = track as SignalTrack;
            if (waitTrack == null) waitTrack = timeline.CreateTrack<SignalTrack>(null, TrackName);
            SignalEmitter emitter = null;
            foreach (var marker in waitTrack.GetMarkers())
            {
                var candidate = marker as SignalEmitter;
                if (candidate != null && candidate.asset == signal) emitter = candidate;
            }
            double time = 8330d / timeline.editorSettings.frameRate;
            if (emitter == null) emitter = waitTrack.CreateMarker<SignalEmitter>(time);
            emitter.time = time;
            emitter.asset = signal;
            emitter.emitOnce = true;
            emitter.retroactive = true;
            director.SetGenericBinding(waitTrack, receiver);

            foreach (var asset in new UnityEngine.Object[] { manager, receiver, director, timeline, waitTrack, emitter })
                EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("场景保存失败。");
            Debug.Log("[望远镜] timeline_02_2 在 8330 帧等待，触发视频时由 Host 同步继续。", manager);
        }
    }
}
