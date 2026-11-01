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

    [Header("Automatic scene binding")]
    [SerializeField] private bool autoBindSceneTargets = true;
    [SerializeField] private string primaryTargetName = "LevelBrain";
    [SerializeField] private string secondaryTargetName = "LBESpace";

    private CommandTerminal boundTerminal;
    private bool ownsPrimaryListener;

    private bool CanHandleInput()
    {
        if (!isActiveAndEnabled) return false;
        var identity = GetComponentInParent<Mirror.NetworkIdentity>();
        return identity == null || identity.isLocalPlayer;
    }

    // Resources also finds inactive scene objects; never bind prefab assets.
    private GameObject FindSceneTarget(string targetName)
    {
        GameObject match = null;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (candidate.name != targetName || !candidate.scene.IsValid() ||
                !candidate.scene.isLoaded) continue;
            if (candidate.scene == gameObject.scene) return candidate;
            if (match == null) match = candidate;
        }
        return match;
    }

    private void ClearPrimaryBinding()
    {
        if (ownsPrimaryListener && boundTerminal != null)
            onPrimaryPressed.RemoveListener(boundTerminal.NextChapter);
        ownsPrimaryListener = false;
        boundTerminal = null;
    }

    private void BindSceneTargets()
    {
        if (!autoBindSceneTargets) return;
        var target = FindSceneTarget(primaryTargetName);
        var terminal = target == null ? null : target.GetComponent<CommandTerminal>();
        if (terminal != boundTerminal)
        {
            ClearPrimaryBinding();
            boundTerminal = terminal;
            if (terminal != null)
            {
                if (onPrimaryPressed == null) onPrimaryPressed = new UnityEvent();
                bool alreadyBound = false;
                for (int i = 0; i < onPrimaryPressed.GetPersistentEventCount(); i++)
                {
                    if (onPrimaryPressed.GetPersistentTarget(i) == terminal &&
                        onPrimaryPressed.GetPersistentMethodName(i) == nameof(CommandTerminal.NextChapter) &&
                        onPrimaryPressed.GetPersistentListenerState(i) != UnityEventCallState.Off)
                        alreadyBound = true;
                }
                if (!alreadyBound)
                {
                    onPrimaryPressed.AddListener(terminal.NextChapter);
                    ownsPrimaryListener = true;
                }
            }
        }
        if (secondaryButtonTarget == null)
            secondaryButtonTarget = FindSceneTarget(secondaryTargetName);
    }

    private void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene,
        UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (CanHandleInput()) BindSceneTargets();
    }

    private InputAction primaryAction;
    private InputAction secondaryAction;

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
        BindSceneTargets();
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
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;
        ClearPrimaryBinding();
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
        if (!CanHandleInput()) return;
        BindSceneTargets();
        if (autoBindSceneTargets && boundTerminal == null)
            Debug.LogWarning("XRButtonEvent: Cannot find CommandTerminal on " + primaryTargetName, this);
        onPrimaryPressed?.Invoke();
    }

    private void HandleSecondaryPressed(InputAction.CallbackContext context)
    {
        if (!CanHandleInput()) return;
        BindSceneTargets();
        if (secondaryButtonTarget == null)
        {
            Debug.LogWarning("XRButtonEvent: B 键没有绑定要显示/隐藏的目标物体。", this);
            return;
        }

        secondaryButtonTarget.SetActive(!secondaryButtonTarget.activeSelf);
    }
}