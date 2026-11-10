using System;
using System.Collections.Generic;
using UnityEngine;

namespace Greatwall.VRAnimation
{
    public enum SequenceAdvanceMode
    {
        BodyAnimation,
        Voice,
        BodyAndVoice,
        Manual
    }

    [Serializable]
    public sealed class AnimationSequenceStep
    {
        [Tooltip("仅用于阅读和调试，例如 PatWall、Lookout、Explain。")]
        public string stepId;

        [Tooltip("身体 Animator 中的状态名。建议放在 Body Layer 的 Action 子状态机中。")]
        public string bodyStateName;

        [Tooltip("可选。状态在子状态机中时填写完整路径，例如 Action.GreatWallActions.你的动作名。填写后优先使用此字段。")]
        public string bodyStatePath;

        [Min(0f)]
        public float crossFadeSeconds = 0.2f;

        [Range(0.05f, 1.2f)]
        [Tooltip("身体动画播放到这个归一化时间后，允许进入下一步。仅 BodyAnimation/BodyAndVoice 模式使用。")]
        public float bodyExitNormalizedTime = 0.9f;

        [Tooltip("该步骤的讲解语音。播放语音时会自动打开面部说话层。")]
        public AudioClip voice;

        [Tooltip("该步骤使用的完整面部状态路径，例如 Face.GreatWallFaces.你的面部动画名。留空时可以使用通用 Face_Talking。")]
        public string faceStatePath;

        [Min(0f)]
        [Tooltip("切换到该面部动画时的淡入时间。")]
        public float faceCrossFadeSeconds = 0.1f;

        [TextArea(1, 3)]
        public string subtitle;

        [Tooltip("BodyAnimation：看身体进度；Voice：看语音；BodyAndVoice：两者都结束；Manual：由外部调用 SkipCurrentStep 或 CompleteManualStep。")]
        public SequenceAdvanceMode advanceMode = SequenceAdvanceMode.BodyAndVoice;

        [Tooltip("没有填写 Face State Path 时，是否使用通用的 Face_Talking 参数。")]
        public bool faceTalkWhileVoicePlays = true;

        public string GetBodyStatePath()
        {
            return string.IsNullOrWhiteSpace(bodyStatePath) ? bodyStateName : bodyStatePath;
        }
    }

    [CreateAssetMenu(menuName = "Greatwall VR/Animation Sequence", fileName = "SEQ_NewSequence")]
    public sealed class AnimationSequenceAsset : ScriptableObject
    {
        [Tooltip("按剧情顺序排列步骤。一个 Sequence 可以代表一个讲解点或一小段演出。")]
        public List<AnimationSequenceStep> steps = new List<AnimationSequenceStep>();
    }
}
