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
        [SerializeField, Min(0.1f)] private float repairDuration = 2.5f;

        private Vector3 fullSize;
        private Vector3 fullPivot;
        private Vector2 fullUVScale;
        private Vector2 fullUVBias;
        private bool unlocked;
        private bool repairing;
        private bool repaired;

        public bool IsRepaired => repaired;

        private void Awake()
        {
            if (interactable == null) interactable = GetComponent<XRSimpleInteractable>();
            if (originalPlaneRenderer == null) originalPlaneRenderer = GetComponent<MeshRenderer>();
            if (projector == null) projector = GetComponentInChildren<DecalProjector>(true);
            CaptureFullProjectorState();
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
        }

        private void CaptureFullProjectorState()
        {
            if (projector == null) return;
            fullSize = projector.size;
            fullPivot = projector.pivot;
            fullUVScale = projector.uvScale;
            fullUVBias = projector.uvBias;
        }

        public void SetCoordinator(Part2CrackRepairCoordinator value) => coordinator = value;

        public void ResetRepair()
        {
            StopAllCoroutines();
            unlocked = false;
            repairing = false;
            repaired = false;
            if (originalPlaneRenderer != null) originalPlaneRenderer.enabled = false;
            if (interactable != null) interactable.enabled = false;
            if (projector != null)
            {
                projector.enabled = true;
                ApplyProgress(0f);
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
            if (!Application.isPlaying || !unlocked || repairing || repaired || projector == null) return;
            repairing = true;
            unlocked = false;
            if (interactable != null) interactable.enabled = false;
            StartCoroutine(AnimateRepair());
        }

        private IEnumerator AnimateRepair()
        {
            float elapsed = 0f;
            while (elapsed < repairDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / repairDuration);
                ApplyProgress(t * t * (3f - 2f * t));
                yield return null;
            }

            ApplyProgress(1f);
            projector.enabled = false;
            repairing = false;
            repaired = true;
            coordinator?.NotifyRepairCompleted(this);
        }

        private void ApplyProgress(float progress)
        {
            if (projector == null) return;
            progress = Mathf.Clamp01(progress);
            float remaining = Mathf.Max(0.001f, 1f - progress);

            // Crop away the lower part while preserving the texture's original scale.
            projector.size = new Vector3(fullSize.x, fullSize.y * remaining, fullSize.z);
            projector.pivot = fullPivot + Vector3.up * (fullSize.y * progress * 0.5f);
            projector.uvScale = new Vector2(fullUVScale.x, fullUVScale.y * remaining);
            projector.uvBias = fullUVBias + new Vector2(0f, fullUVScale.y * progress);
            projector.fadeFactor = progress >= 0.999f ? 0f : 1f;
        }
    }
}
