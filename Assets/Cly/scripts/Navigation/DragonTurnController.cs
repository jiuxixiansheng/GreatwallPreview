using UnityEngine;

namespace Greatwall.Navigation
{
    [DisallowMultipleComponent]
    public sealed class DragonTurnController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform rotationRoot;

        public Transform RotationRoot => rotationRoot;

        private Quaternion startLocalRotation;
        private float activeTurnAngleDegrees;
        private bool isTurning;

        private void Reset()
        {
            rotationRoot = transform;
        }

        private void Awake()
        {
            if (rotationRoot == null) rotationRoot = transform;
        }

        public void BeginTurn(float angleDegrees)
        {
            if (rotationRoot == null)
            {
                Debug.LogError("DragonTurnController: 没有设置 Rotation Root。", this);
                return;
            }

            if (isTurning) FinishTurnControl();

            startLocalRotation = rotationRoot.localRotation;
            activeTurnAngleDegrees = angleDegrees;
            isTurning = true;
        }

        public void EvaluateTurn(float normalizedTime)
        {
            if (!isTurning || rotationRoot == null) return;

            float t = Mathf.Clamp01(normalizedTime);
            float easedTime = t * t * (3f - 2f * t);
            rotationRoot.localRotation =
                startLocalRotation *
                Quaternion.AngleAxis(activeTurnAngleDegrees * easedTime, Vector3.up);
        }

        public void CompleteTurn()
        {
            if (!isTurning) return;

            if (rotationRoot != null)
            {
                rotationRoot.localRotation =
                    startLocalRotation *
                    Quaternion.AngleAxis(activeTurnAngleDegrees, Vector3.up);
            }
            FinishTurnControl();
        }

        public void CancelTurn()
        {
            if (!isTurning) return;
            FinishTurnControl();
        }

        public void ResetRotationOffset()
        {
            if (isTurning) FinishTurnControl();
            if (rotationRoot != null) rotationRoot.localRotation = Quaternion.identity;
        }

        private void OnDisable()
        {
            if (isTurning) FinishTurnControl();
        }

        private void FinishTurnControl()
        {
            isTurning = false;
        }
    }
}
