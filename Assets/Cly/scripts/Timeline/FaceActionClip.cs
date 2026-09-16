using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Greatwall.VRAnimation.Timeline
{
    [Serializable]
    public sealed class FaceActionClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("拖入这个面部状态实际使用的 AnimationClip，用于让 Timeline 读取动画时长。")]
        public AnimationClip referenceAnimationClip;

        public string statePath;
        [Min(0f)] public float crossFadeSeconds = 0.1f;

        public ClipCaps clipCaps => ClipCaps.Blending;
        public override double duration =>
            referenceAnimationClip != null ? Math.Max(0.01d, referenceAnimationClip.length) : 0.5d;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<FaceActionBehaviour> playable =
                ScriptPlayable<FaceActionBehaviour>.Create(graph);
            FaceActionBehaviour behaviour = playable.GetBehaviour();
            behaviour.statePath = statePath;
            behaviour.crossFadeSeconds = crossFadeSeconds;
            return playable;
        }
    }
}
