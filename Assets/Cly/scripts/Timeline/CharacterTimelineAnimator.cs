using System.Collections;
using UnityEngine;

namespace Greatwall.VRAnimation.Timeline
{
    public enum BodyLayerControlMode
    {
        SingleBodyLayer,
        SeparateActionLayer
    }

    /// <summary>
    /// Timeline 与 Animator 之间的统一接口。
    /// 支持所有身体状态位于同一层，也支持基础移动与剧情动作分层。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterTimelineAnimator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator animator;

        [Header("Animator Layers")]
        [Tooltip("Single Body Layer：所有身体状态都在同一层；Separate Action Layer：基础移动和剧情动作分层。")]
        [SerializeField] private BodyLayerControlMode bodyLayerControlMode = BodyLayerControlMode.SeparateActionLayer;
        [SerializeField] private int bodyActionLayerIndex = 1;
        [SerializeField] private int faceLayerIndex = 2;
        [Tooltip("单身体层模式下，Release To Base Layer 命令要返回的完整状态路径。")]
        [SerializeField] private string singleLayerIdleStatePath = "Base Layer.Idle";
        [SerializeField] private bool resetBodyActionLayerOnAwake = true;

        [Header("Default Blending")]
        [SerializeField, Min(0f)] private float defaultBodyLayerFadeSeconds = 0.25f;

        private Coroutine bodyLayerFadeRoutine;

        private void Reset()
        {
            animator = GetComponent<Animator>();
        }

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();

            if (bodyLayerControlMode == BodyLayerControlMode.SeparateActionLayer &&
                resetBodyActionLayerOnAwake &&
                IsValidLayer(bodyActionLayerIndex))
            {
                animator.SetLayerWeight(bodyActionLayerIndex, 0f);
            }
        }

        public void PlayBodyState(
            string fullStatePath,
            float crossFadeSeconds,
            float layerFadeSeconds,
            float stateStartTimeSeconds = 0f)
        {
            if (!TryGetStateHash(bodyActionLayerIndex, fullStatePath, out int stateHash)) return;

            if (bodyLayerControlMode == BodyLayerControlMode.SeparateActionLayer)
                FadeBodyActionLayer(1f, ResolveFadeSeconds(layerFadeSeconds));

            animator.CrossFadeInFixedTime(
                stateHash,
                Mathf.Max(0f, crossFadeSeconds),
                bodyActionLayerIndex,
                Mathf.Max(0f, stateStartTimeSeconds));
        }

        public void ReleaseBodyAction(float layerFadeSeconds)
        {
            float fadeSeconds = ResolveFadeSeconds(layerFadeSeconds);

            if (bodyLayerControlMode == BodyLayerControlMode.SingleBodyLayer)
            {
                if (!TryGetStateHash(bodyActionLayerIndex, singleLayerIdleStatePath, out int idleStateHash))
                    return;

                animator.CrossFadeInFixedTime(
                    idleStateHash,
                    fadeSeconds,
                    bodyActionLayerIndex,
                    0f);
                return;
            }

            FadeBodyActionLayer(0f, fadeSeconds);
        }

        public void PlayFaceState(string fullStatePath, float crossFadeSeconds)
        {
            if (!TryGetStateHash(faceLayerIndex, fullStatePath, out int stateHash)) return;

            animator.CrossFadeInFixedTime(
                stateHash,
                Mathf.Max(0f, crossFadeSeconds),
                faceLayerIndex,
                0f);
        }

        private void FadeBodyActionLayer(float targetWeight, float seconds)
        {
            if (!IsValidLayer(bodyActionLayerIndex)) return;

            if (bodyLayerFadeRoutine != null)
                StopCoroutine(bodyLayerFadeRoutine);

            if (seconds <= 0f)
            {
                animator.SetLayerWeight(bodyActionLayerIndex, targetWeight);
                bodyLayerFadeRoutine = null;
                return;
            }

            bodyLayerFadeRoutine = StartCoroutine(FadeLayerWeightRoutine(targetWeight, seconds));
        }

        private IEnumerator FadeLayerWeightRoutine(float targetWeight, float seconds)
        {
            float startWeight = animator.GetLayerWeight(bodyActionLayerIndex);
            float elapsed = 0f;

            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / seconds);
                animator.SetLayerWeight(
                    bodyActionLayerIndex,
                    Mathf.Lerp(startWeight, targetWeight, progress));
                yield return null;
            }

            animator.SetLayerWeight(bodyActionLayerIndex, targetWeight);
            bodyLayerFadeRoutine = null;
        }

        private bool TryGetStateHash(int layerIndex, string fullStatePath, out int stateHash)
        {
            stateHash = 0;

            if (animator == null)
            {
                Debug.LogError("CharacterTimelineAnimator: 没有绑定 Animator。", this);
                return false;
            }

            if (!IsValidLayer(layerIndex))
            {
                Debug.LogError($"CharacterTimelineAnimator: Animator 中不存在第 {layerIndex} 层。", this);
                return false;
            }

            if (string.IsNullOrWhiteSpace(fullStatePath))
            {
                Debug.LogError("CharacterTimelineAnimator: 动画状态路径为空。", this);
                return false;
            }

            stateHash = Animator.StringToHash(fullStatePath);
            if (animator.HasState(layerIndex, stateHash)) return true;

            Debug.LogError(
                $"CharacterTimelineAnimator: 在第 {layerIndex} 层找不到状态 '{fullStatePath}'。",
                this);
            return false;
        }

        private bool IsValidLayer(int layerIndex)
        {
            return animator != null && layerIndex >= 0 && layerIndex < animator.layerCount;
        }

        private float ResolveFadeSeconds(float requestedSeconds)
        {
            return requestedSeconds < 0f ? defaultBodyLayerFadeSeconds : requestedSeconds;
        }
    }
}
