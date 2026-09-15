using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Greatwall.VRAnimation.Timeline
{
    [Serializable]
    public sealed class BodyActionClip : PlayableAsset, ITimelineClipAsset
    {
        public BodyActionCommand command = BodyActionCommand.PlayState;

        [Tooltip("拖入这个 Animator 状态实际使用的 AnimationClip。它只用于让 Timeline 自动读取动画时长，不会直接接管 Animator。")]
        public AnimationClip referenceAnimationClip;

        public string statePath;
        [Min(0f)] public float stateCrossFadeSeconds = 0.2f;
        [Tooltip("填写负数时使用 CharacterTimelineAnimator 的默认值。")]
        public float layerFadeSeconds = 0.25f;

        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.ClipIn;
        public override double duration =>
            referenceAnimationClip != null ? Math.Max(0.01d, referenceAnimationClip.length) : 0.5d;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<BodyActionBehaviour> playable =
                ScriptPlayable<BodyActionBehaviour>.Create(graph);
            BodyActionBehaviour behaviour = playable.GetBehaviour();
            behaviour.command = command;
            behaviour.statePath = statePath;
            behaviour.stateCrossFadeSeconds = stateCrossFadeSeconds;
            behaviour.layerFadeSeconds = layerFadeSeconds;
            return playable;
        }
    }
}
