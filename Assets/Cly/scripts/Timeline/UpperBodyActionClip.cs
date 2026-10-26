using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Greatwall.VRAnimation.Timeline
{
    [Serializable]
    public sealed class UpperBodyActionClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("仅用于自动读取片段初始长度。实际动作来自 Animator State Path 指定的状态。")]
        public AnimationClip referenceAnimationClip;

        [Tooltip("完整路径，例如 UpperBody.chapter1.Talk。")]
        public string statePath;
        [Min(0f)] public float stateCrossFadeSeconds = 0.2f;
        [Min(0f)] public float layerFadeSeconds = 0.25f;

        // 只发出播放命令，不逐帧采样，不跟随 NavMesh 速度。
        // Clip In 作为 CrossFade 的起始秒数；不逐帧 Play，保留正常的动画过渡。
        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.ClipIn;
        public override double duration =>
            referenceAnimationClip != null ? Math.Max(0.01d, referenceAnimationClip.length) : 0.5d;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<UpperBodyActionBehaviour>.Create(graph);
            UpperBodyActionBehaviour behaviour = playable.GetBehaviour();
            behaviour.statePath = statePath;
            behaviour.stateCrossFadeSeconds = stateCrossFadeSeconds;
            behaviour.layerFadeSeconds = layerFadeSeconds;
            return playable;
        }
    }
}
