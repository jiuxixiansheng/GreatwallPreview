using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Xcy.CrackRepair
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class CrackRepairDecal : MonoBehaviour
    {
        [SerializeField] private DecalProjector projector;
        [SerializeField] private MeshRenderer originalPlaneRenderer;
        [SerializeField] private XRSimpleInteractable interactable;
        [SerializeField] private Part2CrackRepairCoordinator coordinator;
        [SerializeField] private Texture2D repairShapeMask;
        [SerializeField, Min(0.1f)] private float repairDuration = 2.5f;

        private Vector3 fullSize;
        private CrackRepairParticles particles;
        private CrackRepairMaskAnimator maskAnimator;
        private bool unlocked;
        private bool repairing;
        private bool repaired;

        public bool IsRepaired => repaired;
        public bool IsRepairing => repairing;
        public bool CanStartRepair => isActiveAndEnabled && unlocked && !repairing && !repaired && projector != null;
        public double RepairStartedAt { get; private set; }
        public event System.Action<CrackRepairDecal> RepairStarted;
        public event System.Action<CrackRepairDecal> RepairStopped;
        private bool reportCompletion;
        private float initialElapsed;

        private void Awake()
        {
            if (interactable == null) interactable = GetComponent<XRSimpleInteractable>();
            if (originalPlaneRenderer == null) originalPlaneRenderer = GetComponent<MeshRenderer>();
            if (projector == null) projector = GetComponentInChildren<DecalProjector>(true);
            CaptureFullProjectorState();
            if (repairShapeMask == null) repairShapeMask = Resources.Load<Texture2D>("CrackRepairShape");
            maskAnimator = GetComponent<CrackRepairMaskAnimator>();
            if (maskAnimator == null) maskAnimator = gameObject.AddComponent<CrackRepairMaskAnimator>();
            maskAnimator.Initialize(projector, repairShapeMask);
            particles = GetComponent<CrackRepairParticles>();
            if (particles == null) particles = gameObject.AddComponent<CrackRepairParticles>();
            particles.Initialize(projector, fullSize);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (interactable != null) interactable.selectEntered.AddListener(OnSelected);
            ResetRepair();
        }

        private void OnDisable()
        {
            if (interactable != null) interactable.selectEntered.RemoveListener(OnSelected);
            StopAllCoroutines();
            if (repairing) RepairStopped?.Invoke(this);
            repairing = false;
            if (particles != null) particles.Clear();
        }

        private void CaptureFullProjectorState()
        {
            if (projector == null) return;
            fullSize = projector.size;
        }

        public void SetCoordinator(Part2CrackRepairCoordinator value) => coordinator = value;

        public void ResetRepair()
        {
            StopAllCoroutines();
            if (repairing) RepairStopped?.Invoke(this);
            if (particles != null) particles.Clear();
            unlocked = false;
            repairing = false;
            repaired = false;
            if (originalPlaneRenderer != null) originalPlaneRenderer.enabled = false;
            if (interactable != null) interactable.enabled = false;
            if (projector != null)
            {
                projector.enabled = true;
                maskAnimator?.SetProgress(0f);
            }
        }

        public void Unlock()
        {
            if (repaired || repairing) return;
            unlocked = true;
            if (interactable != null) interactable.enabled = true;
        }

        private void OnSelected(SelectEnterEventArgs _) => StartRepair();

        [ContextMenu("Start Repair")]
        public void StartRepair()
        {
            if (!Application.isPlaying || !CanStartRepair) return;
            if (Xcy.Audio.NetworkInteractionAudioRelay.RouteCrackRepair(this)) return;
            StartRepairFromAuthority();
        }

        public void StartRepairFromAuthority()
        {
            if (!Application.isPlaying || !CanStartRepair || NetworkGameplayCoordinator.IsFollower) return;
            BeginRepair(0f, true);
        }

        private void BeginRepair(float elapsed, bool notifyCoordinator)
        {
            repairing = true;
            unlocked = false;
            initialElapsed = Mathf.Clamp(elapsed, 0f, repairDuration);
            RepairStartedAt = Mirror.NetworkTime.time - initialElapsed;
            reportCompletion = notifyCoordinator;
            if (interactable != null) interactable.enabled = false;
            if (particles != null) particles.Begin();
            RepairStarted?.Invoke(this);
            StartCoroutine(AnimateRepair());
        }

        public void ApplyNetworkRepairState(byte state, double startedAt, bool repairUnlocked)
        {
            if (!NetworkGameplayCoordinator.IsFollower || !isActiveAndEnabled) return;
            if (state == 2)
            {
                StopAllCoroutines();
                if (repairing) RepairStopped?.Invoke(this);
                repairing = false;
                repaired = true;
                unlocked = false;
                if (particles != null) particles.Clear();
                maskAnimator?.SetProgress(1f);
                if (projector != null) projector.enabled = false;
                if (interactable != null) interactable.enabled = false;
            }
            else if (state == 1 && !repairing && !repaired)
            {
                if (projector != null) BeginRepair((float)System.Math.Max(0d, Mirror.NetworkTime.time - startedAt), false);
            }
            else if (state == 0)
            {
                if (repairing || repaired) ResetRepair();
                if (repairUnlocked) Unlock();
            }
        }

        private IEnumerator AnimateRepair()
        {
            float elapsed = initialElapsed;
            while (elapsed < repairDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / repairDuration);
                float progress = t * t * (3f - 2f * t);
                maskAnimator?.SetProgress(progress);
                if (particles != null) particles.EmitAt(progress, Time.deltaTime);
                yield return null;
            }

            maskAnimator?.SetProgress(1f);
            projector.enabled = false;
            repairing = false;
            repaired = true;
            RepairStopped?.Invoke(this);
            if (reportCompletion) coordinator?.NotifyRepairCompleted(this);
        }

    }
}
