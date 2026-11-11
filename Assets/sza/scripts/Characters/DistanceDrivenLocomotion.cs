using Mirror;
using UnityEngine;

namespace Sza.Characters
{
    [DisallowMultipleComponent]
    public sealed class DistanceDrivenLocomotion : MonoBehaviour
    {
        public Animator animator;
        public TimelineCharacterActor actor;
        [Tooltip("走路动画以1倍速播放时对应的移动速度，单位米/秒。")]
        [Min(0.01f)] public float referenceMovementSpeed = 0.6f;
        [InspectorName("步速匹配倍率"), Min(0f)] public float animationSpeedMultiplier = 1f;
        [Min(0f)] public float movingThreshold = 0.025f;
        [Min(0f)] public float dampSeconds = 0.08f;
        [Tooltip("超过此距离视为传送，不把它算成一次走路。")]
        [Min(0.1f)] public float teleportDistance = 2f;

        public float ActualSpeed { get; private set; }
        private Vector3 previousPosition;
        private bool initialized;
        private static readonly int MovingId = Animator.StringToHash("IsMoving");
        private static readonly int SpeedId = Animator.StringToHash("MoveSpeed");
        private static readonly int AnimationSpeedId = Animator.StringToHash("MoveAnimationSpeed");

        private void OnEnable() { initialized = false; ActualSpeed = 0f; }

        private void Update()
        {
            Vector3 position = transform.position;
            if (!initialized) { previousPosition = position; initialized = true; return; }
            float distance = Vector3.ProjectOnPlane(position - previousPosition, Vector3.up).magnitude;
            previousPosition = position;
            if (animator == null || NetworkClient.active && !NetworkServer.active) return;
            if (actor != null && actor.IsPaused) return;
            ActualSpeed = distance <= teleportDistance ? distance / Mathf.Max(Time.deltaTime, 0.00001f) : 0f;
            bool moving = ActualSpeed > movingThreshold;
            float multiplier = moving ? ActualSpeed / Mathf.Max(0.01f, referenceMovementSpeed) *
                Mathf.Max(0f, animationSpeedMultiplier) : 1f;
            animator.SetBool(MovingId, moving);
            animator.SetFloat(SpeedId, ActualSpeed, dampSeconds, Time.deltaTime);
            // Only BaseLayer.Walk consumes this parameter. Upper-body and face states stay at1x.
            animator.SetFloat(AnimationSpeedId, multiplier, dampSeconds, Time.deltaTime);
        }
    }
}
