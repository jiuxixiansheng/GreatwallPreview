using System;
using Mirror;
using Sza.Characters;
using UnityEngine;

public struct TimelineActorQueryMessage : NetworkMessage { }

public struct TimelineActorLayerState
{
    public int hash;
    public float normalizedTime, length, speed, weight;
    public bool loop;
}

public struct TimelineActorState
{
    public bool active, paused;
    public Vector3 position;
    public Quaternion rotation, visualRotation;
    public GameplayAnimatorParameter[] parameters;
    public TimelineActorLayerState[] layers;
}

public struct TimelineActorsSnapshotMessage : NetworkMessage
{
    public uint configuration, sequence;
    public double serverTime;
    public int chapter;
    public TimelineActorState[] actors;
}

/// <summary>Additional Timeline actors. Attach to the scene NetworkManager.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(-9000)]
public sealed class NetworkTimelineActorCoordinator : MonoBehaviour
{
    public TimelineCharacterActor[] actors = Array.Empty<TimelineCharacterActor>();
    [Min(0.05f)] public float snapshotInterval = 0.1f;
    [Min(0.02f)] public float animationCorrectionSeconds = 0.2f;
    private bool previousServer, previousClient, previousConnected, hasSnapshot, mismatchReported;
    private uint configuration, sequence, acceptedSequence;
    private double nextSnapshot, nextQuery;
    private TimelineActorsSnapshotMessage latest;
    private bool[] initialized;
    private double[][] stateMismatchSince;
    private static bool Follower => NetworkClient.active && !NetworkServer.active;

    private void Awake()
    {
        configuration = 2166136261u;
        initialized = new bool[actors.Length];
        stateMismatchSince = new double[actors.Length][];
        var ids = new System.Collections.Generic.HashSet<string>();
        foreach (var actor in actors)
        {
            if (actor == null || string.IsNullOrEmpty(actor.actorId) || !ids.Add(actor.actorId))
            { Debug.LogError("[角色同步] 角色引用或 Actor Id 无效、重复。", this); enabled = false; return; }
            foreach (char c in actor.actorId) configuration = (configuration ^ c) * 16777619u;
            if (actor.animator != null && actor.animator.runtimeAnimatorController != null)
                foreach (char c in actor.animator.runtimeAnimatorController.name) configuration = (configuration ^ c) * 16777619u;
        }
        RegisterMessages();
    }

    private void RegisterMessages()
    {
        NetworkServer.ReplaceHandler<TimelineActorQueryMessage>(OnQuery);
        NetworkClient.ReplaceHandler<TimelineActorsSnapshotMessage>(OnSnapshot);
    }

    private void Update()
    {
        if (previousServer != NetworkServer.active || previousClient != NetworkClient.active)
        {
            RegisterMessages();
            if (!NetworkServer.active) sequence = 0;
            if (!NetworkClient.active) ClearReceivedState();
            previousServer = NetworkServer.active; previousClient = NetworkClient.active;
        }
        if (previousConnected && !NetworkClient.isConnected && !NetworkServer.active) ClearReceivedState();
        previousConnected = NetworkClient.isConnected;
        if (Follower && NetworkClient.isConnected && NetworkClient.localPlayer != null && NetworkTime.time >= nextQuery)
        { nextQuery = NetworkTime.time + 2d; NetworkClient.Send(new TimelineActorQueryMessage()); }
        if (Follower && hasSnapshot && ChapterMatches()) ApplyLatest();
    }

    private void ClearReceivedState()
    {
        hasSnapshot = mismatchReported = false; acceptedSequence = 0;
        Array.Clear(initialized, 0, initialized.Length);
        foreach (var actor in actors) if (actor != null)
        { actor.ApplyNetworkPause(false); if (!NetworkClient.active) actor.RestoreLocalNavigation(); }
    }

    private bool ChapterMatches()
    {
        var c = NetworkGameplayCoordinator.Instance;
        return c == null || c.story == null || c.story.CurrentChapterIndex == latest.chapter;
    }

    private void LateUpdate()
    {
        if (NetworkServer.active && NetworkTime.time >= nextSnapshot)
        { nextSnapshot = NetworkTime.time + snapshotInterval; NetworkServer.SendToAll(Capture(), Channels.Reliable, true); }
    }

    private TimelineActorsSnapshotMessage Capture()
    {
        var states = new TimelineActorState[actors.Length];
        for (int i = 0; i < actors.Length; i++)
        {
            var actor = actors[i];
            var state = new TimelineActorState {
                active = actor.gameObject.activeSelf, paused = actor.IsPaused,
                position = actor.transform.position, rotation = actor.transform.rotation,
                visualRotation = actor.visualRoot != null ? actor.visualRoot.localRotation : Quaternion.identity,
                parameters = Array.Empty<GameplayAnimatorParameter>(), layers = Array.Empty<TimelineActorLayerState>()
            };
            var animator = actor.animator;
            if (actor.gameObject.activeInHierarchy && animator != null && animator.isInitialized)
            {
                var parameters = new System.Collections.Generic.List<GameplayAnimatorParameter>();
                foreach (var p in animator.parameters)
                    if (p.type == AnimatorControllerParameterType.Float || p.type == AnimatorControllerParameterType.Bool)
                        parameters.Add(new GameplayAnimatorParameter { hash = p.nameHash,
                            isBool = p.type == AnimatorControllerParameterType.Bool,
                            boolValue = p.type == AnimatorControllerParameterType.Bool && animator.GetBool(p.nameHash),
                            floatValue = p.type == AnimatorControllerParameterType.Float ? animator.GetFloat(p.nameHash) : 0f });
                state.parameters = parameters.ToArray();
                state.layers = new TimelineActorLayerState[animator.layerCount];
                for (int l = 0; l < state.layers.Length; l++)
                {
                    var info = animator.IsInTransition(l) ? animator.GetNextAnimatorStateInfo(l) : animator.GetCurrentAnimatorStateInfo(l);
                    state.layers[l] = new TimelineActorLayerState { hash = info.fullPathHash, normalizedTime = info.normalizedTime,
                        // AnimatorStateInfo.length already includes the state's speed parameter.
                        // Only the Animator-wide speed remains to convert elapsed time to phase.
                        length = info.length, speed = animator.speed,
                        weight = animator.GetLayerWeight(l), loop = info.loop };
                }
            }
            states[i] = state;
        }
        var coordinator = NetworkGameplayCoordinator.Instance;
        return new TimelineActorsSnapshotMessage { configuration = configuration, sequence = ++sequence,
            serverTime = NetworkTime.time, chapter = coordinator != null && coordinator.story != null ? coordinator.story.CurrentChapterIndex : -1,
            actors = states };
    }

    private void OnQuery(NetworkConnectionToClient sender, TimelineActorQueryMessage message)
    { if (NetworkServer.active && sender != null && sender.isReady && sender.identity != null) sender.Send(Capture()); }

    private void OnSnapshot(TimelineActorsSnapshotMessage message)
    {
        if (NetworkServer.active || message.sequence <= acceptedSequence) return;
        if (message.configuration != configuration || message.actors == null || message.actors.Length != actors.Length)
        {
            if (!mismatchReported) Debug.LogWarning("[角色同步] 角色配置不同，请使用相同的打包版本。", this);
            mismatchReported = true; return;
        }
        latest = message; acceptedSequence = message.sequence; hasSnapshot = true;
    }

    private void ApplyLatest()
    {
        for (int i = 0; i < actors.Length; i++)
        {
            var actor = actors[i]; var state = latest.actors[i];
            if (!state.active)
            { if (actor.gameObject.activeSelf) actor.gameObject.SetActive(false); initialized[i] = false; continue; }
            if (!actor.gameObject.activeSelf) actor.gameObject.SetActive(true);
            if (!actor.gameObject.activeInHierarchy) continue;
            if (actor.agent != null) actor.agent.enabled = false;
            float blend = !initialized[i] || Vector3.Distance(actor.transform.position, state.position) > 2f
                ? 1f : 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime);
            actor.transform.SetPositionAndRotation(Vector3.Lerp(actor.transform.position, state.position, blend),
                Quaternion.Slerp(actor.transform.rotation, state.rotation, blend));
            if (actor.visualRoot != null) actor.visualRoot.localRotation = Quaternion.Slerp(actor.visualRoot.localRotation, state.visualRotation, blend);
            actor.ApplyNetworkPause(state.paused);
            ApplyAnimator(i, actor.animator, state);
            initialized[i] = true;
        }
    }

    private void ApplyAnimator(int index, Animator animator, TimelineActorState state)
    {
        if (animator == null || state.layers == null) return;
        if (!animator.isInitialized) { animator.Rebind(); animator.Update(0f); }
        if (state.parameters != null) foreach (var p in state.parameters)
            if (p.isBool) animator.SetBool(p.hash, p.boolValue); else animator.SetFloat(p.hash, p.floatValue);
        if (stateMismatchSince[index] == null || stateMismatchSince[index].Length != state.layers.Length)
            stateMismatchSince[index] = new double[state.layers.Length];
        float elapsed = (float)Math.Max(0d, NetworkTime.time - latest.serverTime);
        for (int l = 0; l < Math.Min(animator.layerCount, state.layers.Length); l++)
        {
            var target = state.layers[l];
            if (target.hash == 0 || !animator.HasState(l, target.hash)) continue;
            animator.SetLayerWeight(l, target.weight);
            // A paused Timeline can still have a running Animator (for example an Idle loop).
            float normalized = target.normalizedTime + (target.length <= 0.001f ? 0f : elapsed * target.speed / target.length);
            if (!target.loop) normalized = Mathf.Clamp01(normalized);
            var current = animator.GetCurrentAnimatorStateInfo(l);
            bool different = current.fullPathHash != target.hash;
            if (different && stateMismatchSince[index][l] == 0d) stateMismatchSince[index][l] = NetworkTime.time;
            if (!different) stateMismatchSince[index][l] = 0d;
            // Give a local Timeline's transition one snapshot to settle before correcting a different state.
            bool correctState = different && (!initialized[index] || NetworkTime.time - stateMismatchSince[index][l] > 0.25d);
            float delta = target.loop ? Mathf.DeltaAngle(current.normalizedTime * 360f, normalized * 360f) / 360f : normalized - current.normalizedTime;
            if (correctState || !different && !animator.IsInTransition(l) && Mathf.Abs(delta) * target.length > animationCorrectionSeconds)
                animator.Play(target.hash, l, normalized);
        }
    }

    private void OnDestroy()
    {
        NetworkServer.UnregisterHandler<TimelineActorQueryMessage>();
        NetworkClient.UnregisterHandler<TimelineActorsSnapshotMessage>();
    }
}
