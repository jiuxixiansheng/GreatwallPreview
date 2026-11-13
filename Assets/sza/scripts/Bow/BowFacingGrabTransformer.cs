using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace Sza.Bow
{
    /// <summary>Keep the grip in the hand and the string on the player's side, independently of wrist yaw.</summary>
    public sealed class BowFacingGrabTransformer : XRBaseGrabTransformer
    {
        private Camera playerCamera;

        public override void Process(XRGrabInteractable grab, XRInteractionUpdateOrder.UpdatePhase phase,
            ref Pose targetPose, ref Vector3 localScale)
        {
            if (grab.interactorsSelecting.Count == 0) return;
            var interactor = grab.interactorsSelecting[0];
            Transform hand = interactor.GetAttachTransform(grab);
            Transform attach = grab.GetAttachTransform(interactor);
            if (playerCamera == null || !playerCamera.gameObject.activeInHierarchy)
            {
                playerCamera = Sza.Telescope.TelescopeVideoManager.LocalCamera;
                if (playerCamera == null) playerCamera = Camera.main;
            }
            Vector3 outward = playerCamera != null ?
                OutwardDirection(hand.position, playerCamera.transform.position, playerCamera.transform.forward) : hand.forward;
            Quaternion localAttachRotation = Quaternion.Inverse(grab.transform.rotation) * attach.rotation;
            Vector3 localAttachPosition = grab.transform.InverseTransformPoint(attach.position);
            targetPose.rotation = Quaternion.LookRotation(outward, Vector3.up) * Quaternion.Inverse(localAttachRotation);
            targetPose.position = hand.position - targetPose.rotation *
                Vector3.Scale(localAttachPosition, grab.transform.lossyScale);
        }

        public static Vector3 OutwardDirection(Vector3 hand, Vector3 head, Vector3 fallbackForward)
        {
            Vector3 delta = hand - head;
            Vector3 horizontal = Vector3.ProjectOnPlane(delta, Vector3.up);
            if (horizontal.sqrMagnitude < 0.0025f)
                horizontal = Vector3.ProjectOnPlane(fallbackForward, Vector3.up);
            if (horizontal.sqrMagnitude < 0.0025f) horizontal = Vector3.forward;
            float length = Mathf.Max(0.05f, horizontal.magnitude);
            // Limit pitch near overhead poses so the bow keeps a stable upright direction.
            return (horizontal.normalized * length + Vector3.up * Mathf.Clamp(delta.y, -length * 1.732f, length * 1.732f)).normalized;
        }
    }
}
