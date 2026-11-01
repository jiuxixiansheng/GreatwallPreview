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
        [Tooltip("新增的上半身 Override 层序号，从 0 开始计数。请按实际 Animator Layers 顺序填写，不要与身体或面部层重复。")]
        [SerializeField] private int upperBodyLayerIndex = -1;
        [Tooltip("单身体层模式下，Release To Base Layer 命令要返回的完整状态路径。")]
        [SerializeField] private string singleLayerIdleStatePath = "Base Layer.Idle";
        [SerializeField] private bool resetBodyActionLayerOnAwake = true;

        [Header("Default Blending")]
        [SerializeField, Min(0f)] private float defaultBodyLayerFadeSeconds = 0.25f;

        private Coroutine bodyLayerFadeRoutine;
        private Coroutine upperBodyLayerFadeRoutine;
        private int requestedBodyStateHash;
        private double bodyCrossFadeEndsAt;

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

            if (IsValidUpperBodyLayer())
                animator.SetLayerWeight(upperBodyLayerIndex, 0f);
        }

        // 上半身状态的 Speed 使用固定值 1，不绑定 MoveAnimationSpeed 等移动倍率参数。
        // 此接口不修改 Animator.speed，也不修改身体层的移动速度参数。
        public void PlayUpperBodyState(
            string fullStatePath, float crossFadeSeconds, float layerFadeSeconds,
            float stateStartTimeSeconds = 0f)
        {
            if (!ValidateUpperBodyLayer() ||
                !TryGetStateHash(upperBodyLayerIndex, fullStatePath, out int stateHash)) return;

            animator.CrossFadeInFixedTime(
                stateHash, Mathf.Max(0f, crossFadeSeconds), upperBodyLayerIndex,
                Mathf.Max(0f, stateStartTimeSeconds));
            LogUpperBodyStatus($"开始播放 {fullStatePath}，起播秒数={stateStartTimeSeconds:F2}");
            FadeUpperBodyLayer(1f, Mathf.Max(0f, layerFadeSeconds));
        }

        // 由上半身轨道混合器在空白、停止或结束时调用。
        public void ReleaseUpperBody(float layerFadeSeconds)
        {
            if (ValidateUpperBodyLayer())
                FadeUpperBodyLayer(0f, Mathf.Max(0f, layerFadeSeconds));
        }

        private bool IsValidUpperBodyLayer()
        {
            return IsValidLayer(upperBodyLayerIndex) &&
                   upperBodyLayerIndex > bodyActionLayerIndex &&
                   upperBodyLayerIndex != faceLayerIndex;
        }

        private bool ValidateUpperBodyLayer()
        {
            if (IsValidUpperBodyLayer()) return true;
            Debug.LogError(
                "CharacterTimelineAnimator: 请设置 Upper Body Layer Index。上半身层必须位于身体层之后，且不能与 Face Layer 重复。", this);
            return false;
        }

        private void FadeUpperBodyLayer(float targetWeight, float seconds)
        {
            if (upperBodyLayerFadeRoutine != null)
            {
                StopCoroutine(upperBodyLayerFadeRoutine);
                upperBodyLayerFadeRoutine = null;
            }

            if (seconds <= 0f || !isActiveAndEnabled)
            {
                animator.SetLayerWeight(upperBodyLayerIndex, targetWeight);
                LogUpperBodyStatus($"权重设置完成，目标={targetWeight:F2}");
                return;
            }

            upperBodyLayerFadeRoutine = StartCoroutine(FadeUpperBodyLayerRoutine(targetWeight, seconds));
        }

        private IEnumerator FadeUpperBodyLayerRoutine(float targetWeight, float seconds)
        {
            float startWeight = animator.GetLayerWeight(upperBodyLayerIndex);
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                animator.SetLayerWeight(upperBodyLayerIndex,
                    Mathf.Lerp(startWeight, targetWeight, Mathf.Clamp01(elapsed / seconds)));
                yield return null;
            }

            animator.SetLayerWeight(upperBodyLayerIndex, targetWeight);
            LogUpperBodyStatus($"淡入/淡出完成，目标={targetWeight:F2}");
            upperBodyLayerFadeRoutine = null;
        }

        // 临时诊断：只在编辑器记录开始和淡变结束，不逐帧刷日志，不进入设备构建。
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogUpperBodyStatus(string message)
        {
            if (!IsValidUpperBodyLayer()) return;
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(upperBodyLayerIndex);
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(upperBodyLayerIndex);
            Debug.Log(
                $"[UpperBody诊断] {message}；角色={name}；Animator={animator.name}；" +
                $"层={upperBodyLayerIndex}:{animator.GetLayerName(upperBodyLayerIndex)}；" +
                $"实际权重={animator.GetLayerWeight(upperBodyLayerIndex):F2}；" +
                $"Animator全局速度={animator.speed:F2}；状态Speed={state.speed:F2}；" +
                $"状态速度倍率={state.speedMultiplier:F2}；过渡中={animator.IsInTransition(upperBodyLayerIndex)}；" +
                $"下一状态速度倍率={next.speedMultiplier:F2}", this);
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

            requestedBodyStateHash = stateHash;
            // CrossFade 的请求帧中 IsInTransition 可能尚未更新，因此同时保护这段时间。
            bodyCrossFadeEndsAt = Time.timeAsDouble + Mathf.Max(0f, crossFadeSeconds);
            animator.CrossFadeInFixedTime(
                stateHash,
                Mathf.Max(0f, crossFadeSeconds),
                bodyActionLayerIndex,
                Mathf.Max(0f, stateStartTimeSeconds));
        }

        public void ReleaseBodyAction(float layerFadeSeconds, string expectedStatePath = null)
        {
            // 重叠片段中，上一段的结束回调不能释放已经由下一段接管的身体层。
            if (Application.isPlaying && expectedStatePath != null &&
                requestedBodyStateHash != Animator.StringToHash(expectedStatePath)) return;

            float fadeSeconds = ResolveFadeSeconds(layerFadeSeconds);

            if (bodyLayerControlMode == BodyLayerControlMode.SingleBodyLayer)
            {
                if (!TryGetStateHash(bodyActionLayerIndex, singleLayerIdleStatePath, out int idleStateHash))
                    return;

                requestedBodyStateHash = idleStateHash;
                bodyCrossFadeEndsAt = Time.timeAsDouble + fadeSeconds;
                animator.CrossFadeInFixedTime(
                    idleStateHash,
                    fadeSeconds,
                    bodyActionLayerIndex,
                    0f);
                return;
            }

            requestedBodyStateHash = 0;
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

        public void SampleBodyState(string fullStatePath, float normalizedTime)
        {
            if (!TryGetStateHash(bodyActionLayerIndex, fullStatePath, out int stateHash)) return;

            if (Application.isPlaying)
            {
                // Play 会直接打断 CrossFade。过渡结束前让 Animator 自己完成混合。
                // 同时排除重叠区内旧片段继续采样、把姿势拉回旧动作的情况。
                if (stateHash != requestedBodyStateHash ||
                    Time.timeAsDouble < bodyCrossFadeEndsAt ||
                    animator.IsInTransition(bodyActionLayerIndex)) return;
            }

            if (bodyLayerControlMode == BodyLayerControlMode.SeparateActionLayer)
            {
                animator.SetLayerWeight(bodyActionLayerIndex, 1f);
            }

            animator.Play(stateHash, bodyActionLayerIndex, Mathf.Repeat(normalizedTime, 1f));
            animator.Update(0f);
        }
    }
}
