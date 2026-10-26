using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class XRButtonEvent : MonoBehaviour
{
    [Header("Input Action Asset")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("Action Map")]
    [SerializeField] private string actionMapName = "XRI Right Interaction";

    [Header("A 键")]
    [SerializeField] private string primaryButtonActionName = "Right Primary Button";
    public UnityEvent onPrimaryPressed;

    [Header("B 键")]
    [SerializeField] private string secondaryButtonActionName = "Right Secondary Button";
    [SerializeField] private GameObject secondaryButtonTarget;

    private InputAction primaryAction;
    private InputAction secondaryAction;

    private void OnEnable()
    {
        InputActionMap actionMap = FindActionMap();
        if (actionMap == null) return;

        primaryAction = BindAction(
            actionMap,
            primaryButtonActionName,
            HandlePrimaryPressed);

        secondaryAction = BindAction(
            actionMap,
            secondaryButtonActionName,
            HandleSecondaryPressed);
    }

    private void OnDisable()
    {
        UnbindAction(primaryAction, HandlePrimaryPressed);
        UnbindAction(secondaryAction, HandleSecondaryPressed);

        primaryAction = null;
        secondaryAction = null;
    }

    private InputActionMap FindActionMap()
    {
        if (inputActions == null)
        {
            Debug.LogWarning("XRButtonEvent: 没有绑定 Input Action Asset。", this);
            return null;
        }

        InputActionMap actionMap = inputActions.FindActionMap(actionMapName, false);
        if (actionMap == null)
        {
            Debug.LogWarning(
                $"XRButtonEvent: 找不到 Action Map: {actionMapName}",
                this);
        }

        return actionMap;
    }

    private InputAction BindAction(
        InputActionMap actionMap,
        string targetActionName,
        System.Action<InputAction.CallbackContext> callback)
    {
        InputAction targetAction = actionMap.FindAction(targetActionName, false);
        if (targetAction == null)
        {
            Debug.LogWarning(
                $"XRButtonEvent: 在 {actionMapName} 中找不到 Action: {targetActionName}",
                this);
            return null;
        }

        targetAction.performed += callback;
        targetAction.Enable();

        return targetAction;
    }

    private void UnbindAction(
        InputAction targetAction,
        System.Action<InputAction.CallbackContext> callback)
    {
        if (targetAction == null) return;

        targetAction.performed -= callback;
    }

    private void HandlePrimaryPressed(InputAction.CallbackContext context)
    {
        onPrimaryPressed?.Invoke();
    }

    private void HandleSecondaryPressed(InputAction.CallbackContext context)
    {
        if (secondaryButtonTarget == null)
        {
            Debug.LogWarning("XRButtonEvent: B 键没有绑定要显示/隐藏的目标物体。", this);
            return;
        }

        secondaryButtonTarget.SetActive(!secondaryButtonTarget.activeSelf);
    }
}