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
        [SerializeField] private Vector2 animationSpeedRange = new Vector2(0.75f, 1.25f);

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

            float multiplier = isMoving ? actualSpeed / runtimeReferenceMovementSpeed : 1f;
            multiplier = Mathf.Clamp(
                multiplier,
                Mathf.Min(animationSpeedRange.x, animationSpeedRange.y),
                Mathf.Max(animationSpeedRange.x, animationSpeedRange.y));

            animator.SetFloat(
                animationSpeedFloat,
                multiplier,
                parameterDampSeconds,
                Time.deltaTime);
        }
    }
}
