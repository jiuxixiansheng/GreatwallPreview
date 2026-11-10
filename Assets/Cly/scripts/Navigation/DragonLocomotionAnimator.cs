using UnityEngine;
using UnityEngine.AI;

namespace Greatwall.Navigation
{
    /// <summary>
    /// 把 NavMeshAgent 的实际移动状态传给 Animator。
    /// 默认只负责 Idle/Walk 切换；动画速度同步可按需开启。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DragonLocomotionAnimator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private Animator animator;

        [Header("Animator Parameters")]
        [SerializeField] private string isMovingBool = "IsMoving";
        [SerializeField] private string moveSpeedFloat = "MoveSpeed";
        [SerializeField, Min(0f)] private float movingThreshold = 0.05f;
        [SerializeField, Min(0f)] private float parameterDampSeconds = 0.1f;

        [Header("Optional Animation Speed Sync")]
        [SerializeField] private bool synchronizeAnimationSpeed;
        [SerializeField] private string animationSpeedFloat = "MoveAnimationSpeed";
        [SerializeField] private bool useInitialAgentSpeedAsReference = true;
        [Tooltip("动画以 1 倍速度播放时，视觉上对应的角色移动速度。")]
        [SerializeField, Min(0.01f)] private float referenceMovementSpeed = 1.5f;
        [Tooltip("1 保持原来的步速匹配比例；大于 1 时相同移动速度下脚步更快，小于 1 时更慢。0 暂停走路动画，不改变移动速度或独立上半身动作。")]
        [SerializeField, Min(0f), InspectorName("步速匹配倍率")]
        private float animationSpeedMultiplier = 1f;

        private float runtimeReferenceMovementSpeed;

        private void Reset()
        {
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponentInChildren<Animator>();
        }

        private void Awake()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            runtimeReferenceMovementSpeed = referenceMovementSpeed;
            if (useInitialAgentSpeedAsReference && agent != null)
            {
                runtimeReferenceMovementSpeed = Mathf.Max(0.01f, agent.speed);
            }
        }

        private void Update()
        {
            if (agent == null || animator == null) return;

            bool agentReady = agent.enabled && agent.isOnNavMesh;
            float actualSpeed = agentReady ? agent.velocity.magnitude : 0f;
            bool isMoving = agentReady && !agent.isStopped && actualSpeed > movingThreshold;

            animator.SetBool(isMovingBool, isMoving);

            // 如果两个字段使用同一个 Animator 参数，不能在同一帧写入两种不同含义的值。
            // 此时把该参数专门作为动画播放倍率使用。
            bool sharesAnimationSpeedParameter =
                synchronizeAnimationSpeed &&
                !string.IsNullOrWhiteSpace(moveSpeedFloat) &&
                moveSpeedFloat == animationSpeedFloat;

            if (!sharesAnimationSpeedParameter && !string.IsNullOrWhiteSpace(moveSpeedFloat))
            {
                animator.SetFloat(
                    moveSpeedFloat,
                    actualSpeed,
                    parameterDampSeconds,
                    Time.deltaTime);
            }

            if (!synchronizeAnimationSpeed) return;

            // 只缩放移动状态的步频，不再把结果限制在固定倍率范围内。
            // 静止时保持 1 倍，避免影响共用此参数的非移动状态。
            float multiplier = isMoving
                ? actualSpeed / runtimeReferenceMovementSpeed * animationSpeedMultiplier
                : 1f;

            animator.SetFloat(
                animationSpeedFloat,
                multiplier,
                parameterDampSeconds,
                Time.deltaTime);
        }
    }
}
