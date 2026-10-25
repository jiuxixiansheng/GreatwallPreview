using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drives a hand Animator's Grip and Trigger parameters from the selected controller.
/// The XR rig's Input Action Manager enables the shared input action asset.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class HandGripAnimator : MonoBehaviour
{
    private enum ControllerHand
    {
        Left,
        Right
    }

    private static readonly int GripParameter = Animator.StringToHash("Grip");
    private static readonly int TriggerParameter = Animator.StringToHash("Trigger");

    [SerializeField] private ControllerHand hand = ControllerHand.Left;
    [SerializeField] private InputActionAsset inputActions;

    private Animator handAnimator;
    private InputAction gripAction;
    private InputAction triggerAction;

    private void Awake()
    {
        handAnimator = GetComponent<Animator>();

        if (inputActions == null)
        {
            Debug.LogWarning("Assign XRI Default Input Actions to Hand Grip Animator.", this);
            return;
        }

        string mapName = hand == ControllerHand.Left
            ? "XRI Left Interaction"
            : "XRI Right Interaction";
        InputActionMap actionMap = inputActions.FindActionMap(mapName);
        gripAction = actionMap?.FindAction("Select Value");
        triggerAction = actionMap?.FindAction("Activate Value");

        if (gripAction == null)
            Debug.LogWarning($"Could not find {mapName}/Select Value.", this);
        if (triggerAction == null)
            Debug.LogWarning($"Could not find {mapName}/Activate Value.", this);
    }

    private void Update()
    {
        if (gripAction != null)
            handAnimator.SetFloat(GripParameter, Mathf.Clamp01(gripAction.ReadValue<float>()));

        if (triggerAction != null)
            handAnimator.SetFloat(TriggerParameter, Mathf.Clamp01(triggerAction.ReadValue<float>()));
    }
}
