using UnityEngine;

namespace Greatwall.VRAnimation
{
    /// <summary>
    /// 给导览点、按钮或触发器调用的薄封装。真正的播放逻辑仍然在 AnimationSequencePlayer 中。
    /// </summary>
    public sealed class AnimationSequenceTrigger : MonoBehaviour
    {
        [SerializeField] private AnimationSequencePlayer player;
        [SerializeField] private AnimationSequenceAsset sequence;
        [SerializeField] private bool playOnStart;

        private void Reset()
        {
            player = FindFirstObjectByType<AnimationSequencePlayer>();
        }

        private void Start()
        {
            if (playOnStart) PlaySequence();
        }

        public void PlaySequence()
        {
            if (player == null)
            {
                Debug.LogWarning("AnimationSequenceTrigger: 没有绑定 AnimationSequencePlayer。", this);
                return;
            }

            player.Play(sequence);
        }

        public void SkipCurrentStep()
        {
            player?.SkipCurrentStep();
        }
    }
}
