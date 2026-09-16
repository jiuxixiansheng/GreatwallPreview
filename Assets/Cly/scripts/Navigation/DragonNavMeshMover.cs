using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace Greatwall.Navigation
{
    /// <summary>
    /// 小龙的通用 NavMesh 移动组件。移动目标由 Timeline Clip 或剧情系统在调用时传入。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class DragonNavMeshMover : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NavMeshAgent agent;

        [Header("Rotation")]
        [Tooltip("关闭后 NavMeshAgent 只控制位置，不再自动旋转角色。")]
        [SerializeField] private bool agentControlsRotation;

        [Header("Arrival")]
        [SerializeField, Min(0f)] private float arrivalTolerance = 0.05f;
        public UnityEvent onMoveStarted = new UnityEvent();
        public UnityEvent onArrived = new UnityEvent();

        public bool IsMoving { get; private set; }

        private float initialAgentSpeed;
        private bool restoreInitialSpeedWhenStopped;

        private void Reset()
        {
            agent = GetComponent<NavMeshAgent>();
        }

        private void Awake()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                initialAgentSpeed = agent.speed;
                agent.updateRotation = agentControlsRotation;
            }
        }

        private void OnValidate()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            if (agent != null) agent.updateRotation = agentControlsRotation;
        }

        private void Update()
        {
            if (!IsMoving || agent == null || !agent.isOnNavMesh || agent.pathPending) return;

            float arrivalDistance = agent.stoppingDistance + arrivalTolerance;
            if (agent.remainingDistance > arrivalDistance) return;
            if (agent.hasPath && agent.velocity.sqrMagnitude > 0.01f) return;

            IsMoving = false;
            agent.isStopped = true;
            RestoreInitialAgentSpeed();
            onArrived?.Invoke();
        }

        public void MoveToPosition(Vector3 position)
        {
            if (agent == null)
            {
                Debug.LogError("DragonNavMeshMover: 没有找到 NavMeshAgent。", this);
                return;
            }

            if (!agent.enabled)
                agent.enabled = true;

            if (!agent.isOnNavMesh)
            {
                Debug.LogError("DragonNavMeshMover: 小龙当前没有站在已烘焙的 NavMesh 上。", this);
                return;
            }

            agent.isStopped = false;
            if (!agent.SetDestination(position))
            {
                Debug.LogError("DragonNavMeshMover: NavMeshAgent 无法设置目标位置。", this);
                return;
            }

            IsMoving = true;
            onMoveStarted?.Invoke();
        }

        public void MoveToPositionInDuration(Vector3 position, float durationSeconds)
        {
            if (agent == null)
            {
                Debug.LogError("DragonNavMeshMover: 没有找到 NavMeshAgent。", this);
                return;
            }

            if (!agent.enabled)
                agent.enabled = true;

            if (!agent.isOnNavMesh)
            {
                Debug.LogError("DragonNavMeshMover: 小龙当前没有站在已烘焙的 NavMesh 上。", this);
                return;
            }

            if (durationSeconds <= 0.01f)
            {
                Debug.LogError("DragonNavMeshMover: 移动时间必须大于 0。", this);
                return;
            }

            NavMeshPath path = new NavMeshPath();
            if (!agent.CalculatePath(position, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                Debug.LogError("DragonNavMeshMover: 无法计算到目标点的完整 NavMesh 路径。", this);
                return;
            }

            float pathLength = CalculatePathLength(path);
            float movementDistance = Mathf.Max(0f, pathLength - agent.stoppingDistance);
            agent.speed = Mathf.Max(0.01f, movementDistance / durationSeconds);
            restoreInitialSpeedWhenStopped = true;

            MoveToPosition(position);
        }

        public void StopMoving()
        {
            if (agent == null || !agent.isOnNavMesh) return;

            agent.isStopped = true;
            agent.ResetPath();
            IsMoving = false;
            RestoreInitialAgentSpeed();
        }

        private static float CalculatePathLength(NavMeshPath path)
        {
            float length = 0f;
            Vector3[] corners = path.corners;

            for (int i = 1; i < corners.Length; i++)
                length += Vector3.Distance(corners[i - 1], corners[i]);

            return length;
        }

        private void RestoreInitialAgentSpeed()
        {
            if (!restoreInitialSpeedWhenStopped || agent == null) return;

            agent.speed = initialAgentSpeed;
            restoreInitialSpeedWhenStopped = false;
        }

    }
}
