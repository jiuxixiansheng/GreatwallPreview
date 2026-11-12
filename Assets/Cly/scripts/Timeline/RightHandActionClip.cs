using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Greatwall.VRAnimation.Timeline
{
    [Serializable]
    public sealed class RightHandActionClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("Animator 状态使用的动画。用于读取 Timeline 片段初始时长。")]
        public AnimationClip referenceAnimationClip;
        [Tooltip("完整状态路径，例如 RightHandLayer.Grip。")]
        public string statePath;
        [Min(0f)] public float crossFadeSeconds = 0.1f;
        [Tooltip("右手层从原动作淡入、淡出的时间；两个右手动作之间的过渡仍由 Cross Fade Seconds 控制。")]
        [Min(0f)] public float layerFadeSeconds = 0.35f;

        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.ClipIn;
        public override double duration =>
            referenceAnimationClip != null ? Math.Max(0.01d, referenceAnimationClip.length) : 0.5d;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<RightHandActionBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour.statePath = statePath;
            behaviour.crossFadeSeconds = crossFadeSeconds;
            behaviour.layerFadeSeconds = layerFadeSeconds;
            behaviour.referenceAnimationLength = referenceAnimationClip != null ? referenceAnimationClip.length : 0f;
            behaviour.loop = referenceAnimationClip != null && referenceAnimationClip.isLooping;
            return playable;
        }
    }
}
