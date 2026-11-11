using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Sza.Characters
{
    [Serializable]
    public sealed class TimelineFaceClip : PlayableAsset, ITimelineClipAsset
    {
        public AnimationClip referenceAnimationClip;
        public string statePath = "FaceLayer.Speech";
        [Min(0f)] public float crossFadeSeconds = 0.08f;
        public ClipCaps clipCaps => ClipCaps.ClipIn;
        public override double duration => referenceAnimationClip != null ? Math.Max(0.01d, referenceAnimationClip.length) : 0.5d;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<TimelineFaceBehaviour>.Create(graph);
            var data = playable.GetBehaviour();
            data.statePath = statePath;
            data.crossFadeSeconds = crossFadeSeconds;
            data.length = referenceAnimationClip != null ? referenceAnimationClip.length : 0f;
            data.loop = referenceAnimationClip != null && referenceAnimationClip.isLooping;
            return playable;
        }
    }

    [Serializable]
    public sealed class TimelineFaceBehaviour : PlayableBehaviour
    {
        public string statePath;
        public float crossFadeSeconds, length;
        public bool loop;
    }
}
