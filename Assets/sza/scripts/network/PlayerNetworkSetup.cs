using UnityEngine;
using System;
using System.Collections.Generic;
using Mirror;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity))]
[DefaultExecutionOrder(-32000)]
public class PlayerNetworkSetup : NetworkBehaviour
{
    [Header("相机和声音：留空时自动查找")]
    public Camera localCamera;
    public AudioListener localAudioListener;

    [Header("额外本机脚本：已有 XR 组件会自动识别")]
    public MonoBehaviour[] movementScripts = Array.Empty<MonoBehaviour>();
    public MonoBehaviour[] handTrackingScripts = Array.Empty<MonoBehaviour>();
    public Behaviour[] additionalLocalOnlyScripts = Array.Empty<Behaviour>();

    [Header("自动隔离远端碰撞体")]
    public Collider playerCollider;

    [Header("隐藏自己的头部模型，不隐藏手")]
    public Renderer[] localAvatarMeshes = Array.Empty<Renderer>();

    [Header("仅本机显示的辅助物体，例如黑屏 Cube")]
    public GameObject[] localOnlyObjects = Array.Empty<GameObject>();

    private struct BehaviourState
    {
        public Behaviour component;
        public bool enabled;
    }

    private struct ColliderState
    {
        public Collider component;
        public bool enabled;
    }

    private struct RendererState
    {
        public Renderer component;
        public bool enabled;
    }

    private struct ObjectState
    {
        public GameObject gameObject;
        public bool active;
    }

    private struct InputManagerState
    {
        public InputActionManager component;
        public bool enabled;
        public List<InputActionAsset> assets;
    }

    private readonly List<BehaviourState> localBehaviours = new List<BehaviourState>();
    private readonly List<ColliderState> colliders = new List<ColliderState>();
    private readonly List<RendererState> hiddenLocalRenderers = new List<RendererState>();
    private readonly List<ObjectState> localObjects = new List<ObjectState>();
    private readonly List<InputManagerState> inputManagers = new List<InputManagerState>();
    private bool initialized;
    private bool IsLocalRig => netIdentity != null && isLocalPlayer;

    private void Awake()
    {
        CacheComponents();
        initialized = true;
        // Ownership is unknown in Awake. Isolate until Mirror identifies its local player.
        ApplyRole(false);
    }

    private void OnEnable()
    {
        if (initialized) ApplyRole(IsLocalRig);
    }

    public override void OnStartClient()
    {
        ApplyRole(IsLocalRig);
    }

    public override void OnStartLocalPlayer()
    {
        ApplyRole(true);
    }

    private void Start()
    {
        ApplyRole(IsLocalRig);
        Debug.Log($"PlayerNetworkSetup: netId={netId}, local={isLocalPlayer}, " +
                  $"server={isServer}, isolatedComponents={localBehaviours.Count}", this);
    }

    public override void OnStopLocalPlayer()
    {
        ApplyRole(false);
    }

    public override void OnStopClient()
    {
        ApplyRole(false);
    }

    private void OnDisable()
    {
        if (initialized) ApplyRole(false);
    }

    private void CacheComponents()
    {
        if (localCamera == null) localCamera = GetComponentInChildren<Camera>(true);
        if (localAudioListener == null) localAudioListener = GetComponentInChildren<AudioListener>(true);
        if (playerCollider == null) playerCollider = GetComponent<CharacterController>();

        foreach (InputActionManager manager in GetComponentsInChildren<InputActionManager>(true))
        {
            inputManagers.Add(new InputManagerState
            {
                component = manager,
                enabled = manager.enabled,
                assets = manager.actionAssets == null
                    ? new List<InputActionAsset>()
                    : new List<InputActionAsset>(manager.actionAssets)
            });
        }

        foreach (Behaviour component in GetComponentsInChildren<Behaviour>(true))
        {
            if (IsLocalOnlyComponent(component)) RegisterLocalBehaviour(component);
        }

        RegisterLocalBehaviour(localCamera);
        RegisterLocalBehaviour(localAudioListener);
        RegisterAdditional(movementScripts);
        RegisterAdditional(handTrackingScripts);
        RegisterAdditional(additionalLocalOnlyScripts);

        foreach (Collider component in GetComponentsInChildren<Collider>(true))
            colliders.Add(new ColliderState { component = component, enabled = component.enabled });

        if (playerCollider != null && !colliders.Exists(state => state.component == playerCollider))
            colliders.Add(new ColliderState { component = playerCollider, enabled = playerCollider.enabled });

        if ((localAvatarMeshes == null || localAvatarMeshes.Length == 0) && localCamera != null)
        {
            Transform head = localCamera.transform.Find("head");
            if (head != null) localAvatarMeshes = head.GetComponentsInChildren<Renderer>(true);
        }

        if (localAvatarMeshes != null)
        {
            foreach (Renderer renderer in localAvatarMeshes)
            {
                if (renderer != null)
                    hiddenLocalRenderers.Add(new RendererState { component = renderer, enabled = renderer.enabled });
            }
        }

        if (localCamera != null)
        {
            Transform fadeCube = localCamera.transform.Find("Cube");
            if (fadeCube != null && fadeCube.GetComponent<FadeEffect>() != null)
                RegisterLocalObject(fadeCube.gameObject);
        }

        if (localOnlyObjects != null)
        {
            foreach (GameObject item in localOnlyObjects) RegisterLocalObject(item);
        }
    }

    private static bool IsLocalOnlyComponent(Behaviour component)
    {
        return component is Camera || component is AudioListener ||
               component is TrackedPoseDriver || component is XROrigin ||
               component is LocomotionProvider || component is LocomotionMediator ||
               component is XRBodyTransformer || component is XRBaseInteractor ||
               component is XRInteractionGroup || component is XRInputModalityManager ||
               component is ControllerInputActionManager || component is HapticImpulsePlayer ||
               component is XRButtonEvent || component is HeadCollisionHandler ||
               component is FadeEffect;
    }

    private void RegisterAdditional<T>(T[] components) where T : Behaviour
    {
        if (components == null) return;
        foreach (T component in components) RegisterLocalBehaviour(component);
    }

    private void RegisterLocalBehaviour(Behaviour component)
    {
        // Replication stays enabled on remote rigs. Shared input assets need separate handling.
        if (component == null || component is NetworkBehaviour || component is NetworkIdentity ||
            component is InputActionManager || localBehaviours.Exists(state => state.component == component))
            return;

        localBehaviours.Add(new BehaviourState { component = component, enabled = component.enabled });
    }

    private void RegisterLocalObject(GameObject item)
    {
        if (item == null || item == gameObject || !item.transform.IsChildOf(transform) ||
            localObjects.Exists(state => state.gameObject == item))
            return;

        localObjects.Add(new ObjectState { gameObject = item, active = item.activeSelf });
    }

    private void ApplyRole(bool local)
    {
        if (!initialized) return;

        foreach (InputManagerState state in inputManagers)
        {
            if (state.component == null) continue;
            // Detach shared assets before OnDisable so a remote rig cannot disable local input.
            if (!local)
            {
                if (state.component.actionAssets.Count > 0)
                    state.component.actionAssets = new List<InputActionAsset>();
                state.component.enabled = false;
            }
            else
            {
                state.component.actionAssets = state.assets;
                state.component.enabled = state.enabled;
            }
        }

        foreach (BehaviourState state in localBehaviours)
        {
            if (state.component != null) state.component.enabled = local && state.enabled;
        }

        foreach (ColliderState state in colliders)
        {
            if (state.component != null) state.component.enabled = local && state.enabled;
        }

        foreach (ObjectState state in localObjects)
        {
            if (state.gameObject != null) state.gameObject.SetActive(local && state.active);
        }

        foreach (RendererState state in hiddenLocalRenderers)
        {
            if (state.component != null) state.component.enabled = !local && state.enabled;
        }

        if (netIdentity != null && netId != 0)
            gameObject.name = (local ? "LocalPlayer_" : "RemotePlayer_") + netId;
    }
}
