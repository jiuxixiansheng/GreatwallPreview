using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkIdentity), typeof(XRGrabInteractable), typeof(Rigidbody))]
public class NetworkGrabbable : NetworkBehaviour, IXRSelectFilter
{
    [Header("Network grab")]
    [SerializeField, Range(10f, 60f)] private float poseSendRate = 30f;
    [SerializeField] private bool logGrabEvents = true;
    [Header("Release safety limits")]
    [SerializeField, Min(0f)] private float maximumReleaseSpeed = 8f;
    [SerializeField, Min(0f)] private float maximumReleaseAngularSpeed = 20f;
    [SyncVar] private uint holderNetId;
    private NetworkConnectionToClient serverHolder;
    private uint serverRequest, lastPoseSequence;
    private XRGrabInteractable grab;
    private Rigidbody body;
    private NetworkTransformBase[] networkTransforms;
    private bool[] transformEnabledStates;
    private bool originalKinematic, originalGravity;
    private bool localSession, requestPending, releasePending, cancelingGrab, transformsSuspended;
    private uint localRequest, poseSequence;
    private float responseDeadline, nextPoseTime;
    private Coroutine releaseRoutine;
    public bool canProcess => isActiveAndEnabled;
    private uint LocalPlayerId => NetworkClient.localPlayer != null ? NetworkClient.localPlayer.netId : 0;

    private void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        body = GetComponent<Rigidbody>();
        networkTransforms = GetComponents<NetworkTransformBase>();
        transformEnabledStates = new bool[networkTransforms.Length];
        originalKinematic = body.isKinematic;
        originalGravity = body.useGravity;
        // Server always publishes Transform state. Grab locks do not change Mirror ownership.
        foreach (NetworkTransformBase sync in networkTransforms)
        {
            sync.syncDirection = SyncDirection.ServerToClient;
            // XRI reparents held objects. Peers can therefore have different parents.
            sync.coordinateSpace = CoordinateSpace.World;
        }
        grab.selectFilters.Add(this);
        grab.selectEntered.AddListener(OnGrab);
        grab.selectExited.AddListener(OnRelease);
    }

    public override void OnStartServer()
    {
        holderNetId = 0;
        serverHolder = null;
        ApplyPhysicsRole();
    }
    public override void OnStartClient() => ApplyPhysicsRole();
    public override void OnStopClient() => EndLocalSession(true);

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        // XRI Single mode permits another local interactor to take the selection.
        // Near-Far + Direct on one controller would otherwise create a false drop.
        if (grab.isSelected && grab.selectMode == InteractableSelectMode.Single)
            return grab.IsSelected(interactor);
        if (!NetworkClient.active && !NetworkServer.active) return true;
        if (!isClient || releasePending) return false;
        if (!localSession && holderNetId != 0 && holderNetId != LocalPlayerId) return false;
        // Prepare physics before XRI caches pre-grab Rigidbody settings.
        if (!grab.isSelected && interactor.isSelectActive)
        {
            body.isKinematic = originalKinematic;
            body.useGravity = originalGravity;
        }
        return true;
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        if (!isClient) return;
        if (localSession)
        {
            // A manual same-frame handoff is still one network grab session.
            if (releaseRoutine != null) { StopCoroutine(releaseRoutine); releaseRoutine = null; }
            releasePending = false;
            return;
        }
        localSession = requestPending = true;
        releasePending = false;
        if (++localRequest == 0) ++localRequest;
        poseSequence = 0;
        responseDeadline = Time.unscaledTime + 5f;
        nextPoseTime = 0f;
        // Old server poses must not fight a predicted local grab.
        if (!isServer)
        {
            transformsSuspended = true;
            for (int i = 0; i < networkTransforms.Length; ++i)
            {
                transformEnabledStates[i] = networkTransforms[i].enabled;
                networkTransforms[i].enabled = false;
            }
        }
        Trace("request grab " + localRequest);
        CmdRequestGrab(localRequest);
    }

    private void Update()
    {
        if (!isServer && !isClient) return;
        if (isServer && serverHolder != null)
        {
            NetworkConnectionToClient current;
            if (serverHolder.identity == null ||
                !NetworkServer.connections.TryGetValue(serverHolder.connectionId, out current) || current != serverHolder)
            {
                ClearServerHolder();
                ApplyPhysicsRole();
                StopBody();
                MarkPoseDirty();
                Trace("holder disconnected; returned to server physics");
            }
        }
        ApplyPhysicsRole();
        if (localSession && (requestPending || releasePending) && Time.unscaledTime > responseDeadline)
        {
            Trace("server reply timeout; canceling local grab");
            CmdCancelGrab(localRequest);
            EndLocalSession(true);
        }
    }

    private void LateUpdate()
    {
        if (!isClient || !localSession || releasePending || !grab.isSelected || Time.unscaledTime < nextPoseTime) return;
        nextPoseTime = Time.unscaledTime + 1f / Mathf.Max(1f, poseSendRate);
        CmdUpdateHeldPose(localRequest, ++poseSequence, transform.position, transform.rotation);
    }

    private void ApplyPhysicsRole()
    {
        if (body == null) return;
        bool simulate = (isClient && localSession) || (isServer && holderNetId == 0);
        if (simulate)
        {
            if (!grab.isSelected)
            {
                body.isKinematic = originalKinematic;
                body.useGravity = originalGravity;
            }
        }
        else
        {
            StopBody();
            body.isKinematic = true;
            body.useGravity = false;
        }
    }
    private void StopBody()
    {
        if (body.isKinematic) return;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        if (cancelingGrab || !isClient || !localSession || grab.isSelected || releasePending || releaseRoutine != null) return;
        Trace("XRI exit: canceled=" + args.isCanceled + ", interactor=" + args.interactorObject.transform.name);
        releaseRoutine = StartCoroutine(ReleaseAfterDetach(localRequest));
    }
    private IEnumerator ReleaseAfterDetach(uint request)
    {
        // XRI writes its throw velocity in late-frame Detach, after selectExited.
        yield return null;
        releaseRoutine = null;
        if (!isClient || !localSession || request != localRequest || grab.isSelected) yield break;
        releasePending = true;
        responseDeadline = Time.unscaledTime + 5f;
        Vector3 velocity = body.isKinematic ? Vector3.zero : body.linearVelocity;
        Vector3 angular = body.isKinematic ? Vector3.zero : body.angularVelocity;
        Trace("request release " + request);
        // Same reliable channel preserves grab/release order, even for a quick press.
        CmdReleaseGrab(request, transform.position, transform.rotation,
            Vector3.ClampMagnitude(velocity, maximumReleaseSpeed),
            Vector3.ClampMagnitude(angular, maximumReleaseAngularSpeed));
    }

    private void EndLocalSession(bool cancelSelection)
    {
        localSession = requestPending = releasePending = false;
        cancelingGrab = true;
        if (releaseRoutine != null) { StopCoroutine(releaseRoutine); releaseRoutine = null; }
        if (cancelSelection && grab != null && grab.isSelected && grab.interactionManager != null)
            grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
        cancelingGrab = false;
        if (transformsSuspended)
        {
            transformsSuspended = false;
            for (int i = 0; i < networkTransforms.Length; ++i)
            {
                networkTransforms[i].ResetState();
                networkTransforms[i].enabled = transformEnabledStates[i];
            }
        }
        ApplyPhysicsRole();
    }

    private bool TryClaim(NetworkConnectionToClient sender, uint request)
    {
        if (sender == null || sender.identity == null || sender.identity.netId == 0 || request == 0) return false;
        if (serverHolder != null) return serverHolder == sender && serverRequest == request;
        serverHolder = sender;
        serverRequest = request;
        lastPoseSequence = 0;
        holderNetId = sender.identity.netId;
        ApplyPhysicsRole();
        return true;
    }
    private bool IsHolder(NetworkConnectionToClient sender, uint request) =>
        sender != null && serverHolder == sender && serverRequest == request;
    private void ClearServerHolder()
    {
        serverHolder = null;
        serverRequest = lastPoseSequence = holderNetId = 0;
    }

    [Command(requiresAuthority = false)]
    private void CmdRequestGrab(uint request, NetworkConnectionToClient sender = null)
    {
        if (sender == null) return;
        bool accepted = TryClaim(sender, request);
        Trace("server grab " + request + " accepted=" + accepted + " connection=" + sender.connectionId);
        TargetGrabResult(sender, request, accepted, transform.position, transform.rotation);
    }
    [TargetRpc]
    private void TargetGrabResult(NetworkConnectionToClient target, uint request, bool accepted,
        Vector3 position, Quaternion rotation)
    {
        if (!localSession || localRequest != request) return;
        requestPending = false;
        Trace("grab result " + request + " accepted=" + accepted);
        if (!accepted) { EndLocalSession(true); SetPose(position, rotation); }
    }

    [Command(requiresAuthority = false, channel = Channels.Unreliable)]
    private void CmdUpdateHeldPose(uint request, uint sequence, Vector3 position, Quaternion rotation,
        NetworkConnectionToClient sender = null)
    {
        if (!IsHolder(sender, request) || sequence <= lastPoseSequence || !ValidPose(position, rotation)) return;
        lastPoseSequence = sequence;
        SetPose(position, rotation);
        MarkPoseDirty();
    }

    [Command(requiresAuthority = false)]
    private void CmdReleaseGrab(uint request, Vector3 position, Quaternion rotation, Vector3 velocity,
        Vector3 angularVelocity, NetworkConnectionToClient sender = null)
    {
        if (sender == null) return;
        bool accepted = TryRelease(sender, request, position, rotation, velocity, angularVelocity);
        Trace("server release " + request + " accepted=" + accepted);
        TargetReleaseResult(sender, request, transform.position, transform.rotation);
    }

    private bool TryRelease(NetworkConnectionToClient sender, uint request, Vector3 position,
        Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
    {
        if (!IsHolder(sender, request)) return false;
        if (ValidPose(position, rotation)) SetPose(position, rotation);
        ClearServerHolder();
        ApplyPhysicsRole();
        if (!body.isKinematic)
        {
            body.linearVelocity = Finite(velocity) ? Vector3.ClampMagnitude(velocity, maximumReleaseSpeed) : Vector3.zero;
            body.angularVelocity = Finite(angularVelocity) ? Vector3.ClampMagnitude(angularVelocity, maximumReleaseAngularSpeed) : Vector3.zero;
            body.WakeUp();
        }
        MarkPoseDirty();
        return true;
    }
    [TargetRpc]
    private void TargetReleaseResult(NetworkConnectionToClient target, uint request, Vector3 position, Quaternion rotation)
    {
        if (!localSession || localRequest != request) return;
        EndLocalSession(false);
        if (!isServer) SetPose(position, rotation);
        Trace("release confirmed " + request);
    }
    [Command(requiresAuthority = false)]
    private void CmdCancelGrab(uint request, NetworkConnectionToClient sender = null)
    {
        if (!IsHolder(sender, request)) return;
        ClearServerHolder();
        ApplyPhysicsRole();
        StopBody();
        MarkPoseDirty();
    }

    private void SetPose(Vector3 position, Quaternion rotation)
    {
        rotation = rotation.normalized;
        transform.SetPositionAndRotation(position, rotation);
        body.position = position;
        body.rotation = rotation;
    }
    private void MarkPoseDirty()
    {
        foreach (NetworkTransformBase sync in networkTransforms) sync.SetDirty();
    }
    private static bool ValidPose(Vector3 position, Quaternion rotation)
    {
        float squared = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w;
        return Finite(position) && Finite(new Vector3(rotation.x, rotation.y, rotation.z)) &&
            !float.IsNaN(rotation.w) && !float.IsInfinity(rotation.w) && !float.IsInfinity(squared) && squared > 0.000001f;
    }
    private static bool Finite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    private void Trace(string message)
    {
        if (logGrabEvents) Debug.Log("[NetworkGrab v5] " + name + ": " + message, this);
    }
    private void OnDestroy()
    {
        if (grab == null) return;
        grab.selectFilters.Remove(this);
        grab.selectEntered.RemoveListener(OnGrab);
        grab.selectExited.RemoveListener(OnRelease);
    }
}
