using UnityEngine;

namespace Greatwall.VRAnimation.Debugging
{
    /// <summary>
    /// 仅用于独立验证某个 Animator 状态能否播放。测试结束后可禁用或移除此组件。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnimatorStateTester : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private int layerIndex;
        [SerializeField] private string fullStatePath = "Base Layer.Idle";
        [SerializeField] private bool playOnStart = true;

        private void Reset()
        {
            animator = GetComponent<Animator>();
        }

        private void Start()
        {
            if (playOnStart) PlayState();
        }

        [ContextMenu("Play State")]
        public void PlayState()
        {
            if (animator == null)
            {
                Debug.LogError("AnimatorStateTester: 没有绑定 Animator。", this);
                return;
            }

            if (animator.runtimeAnimatorController == null)
            {
                Debug.LogError("AnimatorStateTester: 绑定的 Animator 没有 Animator Controller。", this);
                return;
            }

            if (layerIndex < 0 || layerIndex >= animator.layerCount)
            {
                Debug.LogError(
                    $"AnimatorStateTester: Layer Index {layerIndex} 无效，当前 Animator 共有 {animator.layerCount} 层。",
                    this);
                return;
            }

            if (string.IsNullOrWhiteSpace(fullStatePath))
            {
                Debug.LogError("AnimatorStateTester: Full State Path 为空。", this);
                return;
            }

            int stateHash = Animator.StringToHash(fullStatePath);
            if (!animator.HasState(layerIndex, stateHash))
            {
                Debug.LogError(
                    $"AnimatorStateTester: 在第 {layerIndex} 层找不到状态 '{fullStatePath}'。",
                    this);
                return;
            }

            animator.Play(stateHash, layerIndex, 0f);
            animator.Update(0f);
            Debug.Log($"AnimatorStateTester: 已播放 '{fullStatePath}'。", this);
        }
    }
}
