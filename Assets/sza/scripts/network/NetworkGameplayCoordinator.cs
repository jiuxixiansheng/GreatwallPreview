using System;
using System.Collections.Generic;
using Greatwall.VRAnimation.Timeline;
using Mirror;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Playables;

/// <summary>Attach to NetworkManager. Reliable messages carry durable gameplay state;
/// individual bricks retain NetworkGrabbable/NetworkTransform for pose and grab locks.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(-10000)]
public sealed class NetworkGameplayCoordinator : MonoBehaviour
{
    public static NetworkGameplayCoordinator Instance { get; private set; }
    public static bool IsManaged => Instance != null && Instance.enabled;
    public static bool IsFollower => IsManaged && NetworkClient.active && !NetworkServer.active;
    public static bool IsApplyingSnapshot => IsManaged && Instance.applyingSnapshot;

    [Header("剧情和修墙")]
    public StoryTimelineManager story;
    public BrickDropZone[] brickZones = Array.Empty<BrickDropZone>();
    public NetworkGrabbable[] bricks = Array.Empty<NetworkGrabbable>();
    public NavMeshAgent characterAgent;
    public Animator characterAnimator;
    [Header("状态恢复（顺序须在所有电脑一致）")]
    public GameObject[] persistentObjects = Array.Empty<GameObject>();
    public Renderer[] persistentRenderers = Array.Empty<Renderer>();
    [Header("同步设置")]
    [Min(0.05f)] public float snapshotInterval = 0.15f;
    [Min(0.05f)] public float timelineCorrectionThreshold = 0.35f;
    public bool allowClientDebugSolve;

    private bool serverInitialized, previousServer, previousClient;
    private bool applyingServerAction, applyingSnapshot, hasSnapshot, mismatchReported;
    private bool[] serverSolved;
    private uint configuration, sequence, acceptedSequence;
    private double nextBroadcast, nextRequest;
    private GameplaySnapshotMessage latest;
    private MaterialPropertyBlock block;
    private BuildUpController[] buildOwners;
    private readonly Dictionary<int, double> requestTimes = new Dictionary<int, double>();
    private Behaviour locomotion;
    private bool locomotionWasEnabled;
    private static readonly int DistanceId = Shader.PropertyToID("_distance");
    private static readonly int RestoreId = Shader.PropertyToID("_RestoreProgress");

    private void Awake()
    {
        if (Instance != null && Instance != this) { enabled = false; return; }
        Instance = this;
        block = new MaterialPropertyBlock();
        buildOwners = Array.ConvertAll(persistentRenderers, r => r != null ? r.GetComponentInParent<BuildUpController>() : null);
        configuration = CalculateConfiguration();
        serverSolved = new bool[brickZones.Length];
        if (characterAgent != null)
            foreach (var b in characterAgent.GetComponents<MonoBehaviour>())
                if (b.GetType().Name == "DragonLocomotionAnimator")
                { locomotion = b; locomotionWasEnabled = b.enabled; }
        RegisterMessages();
    }

    private void RegisterMessages()
    {
        NetworkServer.ReplaceHandler<GameplayRequestMessage>(OnRequest);
        NetworkClient.ReplaceHandler<GameplaySnapshotMessage>(OnSnapshot);
    }

    private void Update()
    {
        if (previousServer != NetworkServer.active || previousClient != NetworkClient.active)
        {
            RegisterMessages();
            if (!NetworkServer.active) serverInitialized = false;
            if (!NetworkClient.active) { hasSnapshot = false; acceptedSequence = 0; mismatchReported = false;
                if (locomotion != null) locomotion.enabled = locomotionWasEnabled; }
            previousServer = NetworkServer.active;
            previousClient = NetworkClient.active;
        }
        if (NetworkServer.active && !serverInitialized && story != null)
        {
            serverInitialized = true;
            for (int i = 0; i < serverSolved.Length; i++) serverSolved[i] = brickZones[i] != null && brickZones[i].IsSolved;
            requestTimes.Clear();
            sequence = 0;
            RunServerAction(() => story.InitializeNetworkChapter());
        }
        if (IsFollower && NetworkClient.isConnected && NetworkClient.localPlayer != null &&
            NetworkTime.time >= nextRequest)
        {
            nextRequest = NetworkTime.time + 2d;
            SendRequest(GameplayRequestKind.Snapshot, 0);
        }
        if (IsFollower && locomotion != null) locomotion.enabled = false;
    }

    private void LateUpdate()
    {
        if (NetworkServer.active && serverInitialized)
        {
            // Chapter/Activation Tracks can make previously inactive scene bricks available.
            foreach (var b in bricks)
                if (b != null && b.gameObject.activeInHierarchy && b.GetComponent<NetworkIdentity>().netId == 0)
                    NetworkServer.Spawn(b.gameObject);
            for (int i = 0; i < serverSolved.Length; i++)
                if (serverSolved[i] && brickZones[i] != null) brickZones[i].ApplySolvedState(false);
            if (NetworkTime.time >= nextBroadcast)
            {
                nextBroadcast = NetworkTime.time + snapshotInterval;
                Broadcast();
            }
        }
        else if (IsFollower && hasSnapshot)
        {
            ApplyDurableObjects(latest);
            ApplyVisuals(latest);
            if (characterAgent != null)
            {
                characterAgent.enabled = false;
                var t = characterAgent.transform;
                float blend = Vector3.Distance(t.position, latest.characterPosition) > 2f
                    ? 1f : 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime);
                t.SetPositionAndRotation(Vector3.Lerp(t.position, latest.characterPosition, blend),
                    Quaternion.Slerp(t.rotation, latest.characterRotation, blend));
            }
            if (characterAnimator != null && latest.parameters != null)
                foreach (var p in latest.parameters)
                    if (p.isBool) characterAnimator.SetBool(p.hash, p.boolValue);
                    else characterAnimator.SetFloat(p.hash, p.floatValue);
        }
    }

    public static bool RouteChapterRequest(int index)
    {
        if (!IsManaged || Instance.applyingServerAction) return false;
        Instance.RequestChapter(index);
        return true;
    }

    public void SwitchToNextChapter()
    {
        if (story != null) RequestChapter((story.CurrentChapterIndex + 1) % Math.Max(1, story.ChapterCount));
    }
    public void RequestChapter(int index)
    {
        if (NetworkServer.active) ExecuteRequest(GameplayRequestKind.Chapter, index);
        else if (!applyingSnapshot) SendRequest(GameplayRequestKind.Chapter, index);
    }
    public void PauseTimeline() => RequestPlayback(false);
    public void ResumeTimeline() => RequestPlayback(true);
    private void RequestPlayback(bool play)
    {
        var kind = play ? GameplayRequestKind.Resume : GameplayRequestKind.Pause;
        if (NetworkServer.active) ExecuteRequest(kind, 0);
        else if (!applyingSnapshot) SendRequest(kind, 0);
    }

    // Timeline signals are evaluated on every peer; only the server interprets them as commands.
    public void HandleTimelineSignal(PlayableDirector director, bool play)
    {
        if (NetworkServer.active && story != null && story.CurrentDirector == director)
            ExecuteRequest(play ? GameplayRequestKind.Resume : GameplayRequestKind.Pause, 0);
    }

    public void RequestDebugSolve(BrickDropZone zone)
    {
        int index = Array.IndexOf(brickZones, zone);
        if (index < 0) return;
        if (NetworkServer.active) ServerSolve(zone);
        else SendRequest(GameplayRequestKind.DebugSolve, index);
    }

    public bool IsRegisteredBrick(Rigidbody body)
    {
        foreach (var b in bricks) if (b != null && b.GetComponent<Rigidbody>() == body) return true;
        return false;
    }

    public void ServerSolve(BrickDropZone zone)
    {
        if (!NetworkServer.active || !serverInitialized || zone == null || !zone.gameObject.activeInHierarchy) return;
        int index = Array.IndexOf(brickZones, zone);
        if (index < 0 || serverSolved[index]) return;
        serverSolved[index] = true;
        zone.ApplySolvedState(true);
        Broadcast();
    }

    private void SendRequest(GameplayRequestKind kind, int argument)
    {
        if (!NetworkClient.isConnected || NetworkClient.localPlayer == null || story == null) return;
        NetworkClient.Send(new GameplayRequestMessage {
            kind = kind, argument = argument, chapter = story.CurrentChapterIndex, configuration = configuration
        });
    }

    private void OnRequest(NetworkConnectionToClient sender, GameplayRequestMessage message)
    {
        if (!serverInitialized || sender == null || sender.identity == null || !sender.isReady) return;
        if (message.configuration != configuration)
        { Debug.LogWarning("[联机剧情] 客户端场景配置不一致，请使用相同版本。", this); return; }
        if (message.kind == GameplayRequestKind.Snapshot) { sender.Send(Capture()); return; }
        if (story == null || message.chapter != story.CurrentChapterIndex || story.IsTransitionPending) return;
        double now = NetworkTime.time;
        if (requestTimes.TryGetValue(sender.connectionId, out double previous) && now - previous < 0.15d) return;
        requestTimes[sender.connectionId] = now;
        if (message.kind == GameplayRequestKind.DebugSolve)
        {
            if (allowClientDebugSolve && message.argument >= 0 && message.argument < brickZones.Length)
                ServerSolve(brickZones[message.argument]);
            return;
        }
        ExecuteRequest(message.kind, message.argument);
    }

    private void ExecuteRequest(GameplayRequestKind kind, int argument)
    {
        if (!serverInitialized || story == null) return;
        if (kind == GameplayRequestKind.Chapter)
        {
            if (argument < 0 || argument >= story.ChapterCount || story.IsTransitionPending) return;
            RunServerAction(() => story.SwitchToChapterByIndex(argument));
        }
        else if (!story.IsTransitionPending && kind == GameplayRequestKind.Pause)
            story.SetNetworkPlayback(false);
        else if (!story.IsTransitionPending && kind == GameplayRequestKind.Resume)
            story.SetNetworkPlayback(true);
        Broadcast();
    }

    private void RunServerAction(Action action)
    {
        applyingServerAction = true;
        try { action(); } finally { applyingServerAction = false; }
    }

    private void Broadcast()
    {
        if (NetworkServer.active && serverInitialized) NetworkServer.SendToAll(Capture(), Channels.Reliable, true);
    }

    private GameplaySnapshotMessage Capture()
    {
        var director = story.CurrentDirector;
        var active = new bool[persistentObjects.Length];
        for (int i = 0; i < active.Length; i++) active[i] = persistentObjects[i] != null && persistentObjects[i].activeSelf;
        var visuals = new GameplayVisualState[persistentRenderers.Length];
        for (int i = 0; i < visuals.Length; i++)
        {
            var r = persistentRenderers[i]; if (r == null) continue;
            block.Clear(); r.GetPropertyBlock(block);
            var material = r.sharedMaterial;
            bool distance = block.HasFloat(DistanceId) || (material != null && material.HasProperty(DistanceId));
            bool restore = block.HasFloat(RestoreId) || (material != null && material.HasProperty(RestoreId));
            visuals[i] = new GameplayVisualState {
                distanceSet = distance, restoreSet = restore, shadows = (int)r.shadowCastingMode,
                distance = block.HasFloat(DistanceId) ? block.GetFloat(DistanceId) : distance ? material.GetFloat(DistanceId) : 0,
                restore = block.HasFloat(RestoreId) ? block.GetFloat(RestoreId) : restore ? material.GetFloat(RestoreId) : 0
            };
        }
        var parameters = new List<GameplayAnimatorParameter>();
        if (characterAnimator != null && characterAnimator.isActiveAndEnabled)
            foreach (var p in characterAnimator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Bool)
                    parameters.Add(new GameplayAnimatorParameter { hash = p.nameHash, isBool = true, boolValue = characterAnimator.GetBool(p.nameHash) });
                else if (p.type == AnimatorControllerParameterType.Float)
                    parameters.Add(new GameplayAnimatorParameter { hash = p.nameHash, floatValue = characterAnimator.GetFloat(p.nameHash) });
            }
        return new GameplaySnapshotMessage {
            configuration = configuration, sequence = ++sequence, chapter = story.CurrentChapterIndex,
            serverTime = NetworkTime.time, timelineTime = director != null ? director.time : 0,
            playing = director != null && director.state == PlayState.Playing,
            transitioning = story.IsTransitionPending, solved = (bool[])serverSolved.Clone(), objectActive = active,
            visuals = visuals, characterPosition = characterAgent != null ? characterAgent.transform.position : Vector3.zero,
            characterRotation = characterAgent != null ? characterAgent.transform.rotation : Quaternion.identity,
            parameters = parameters.ToArray()
        };
    }

    private void OnSnapshot(GameplaySnapshotMessage message)
    {
        if (NetworkServer.active || story == null || message.sequence <= acceptedSequence) return;
        if (message.configuration != configuration || message.objectActive == null ||
            message.objectActive.Length != persistentObjects.Length || message.solved == null ||
            message.solved.Length != brickZones.Length || message.visuals == null || message.visuals.Length != persistentRenderers.Length)
        {
            if (!mismatchReported) Debug.LogError("[联机剧情] 配置不一致，已拒绝应用状态，请让所有电脑使用相同版本。", this);
            mismatchReported = true;
            return;
        }
        bool initial = !hasSnapshot;
        bool changedChapter = story.CurrentChapterIndex != message.chapter;
        latest = message; acceptedSequence = message.sequence; hasSnapshot = true;
        applyingSnapshot = true;
        try
        {
            double time = message.timelineTime + (message.playing && !message.transitioning
                ? Math.Max(0, NetworkTime.time - message.serverTime) : 0);
            story.ApplyNetworkState(message.chapter, time, message.playing, message.transitioning,
                initial, timelineCorrectionThreshold);
            ApplyDurableObjects(message);
            if (initial || changedChapter) ApplyVisuals(message);
        }
        finally { applyingSnapshot = false; }
    }

    private void ApplyDurableObjects(GameplaySnapshotMessage message)
    {
        for (int i = 0; i < persistentObjects.Length; i++)
        {
            var o = persistentObjects[i];
            if (o != null && o.activeSelf != message.objectActive[i]) o.SetActive(message.objectActive[i]);
        }
        for (int i = 0; i < brickZones.Length; i++)
            if (message.solved[i] && brickZones[i] != null) brickZones[i].ApplySolvedState(false);
    }

    private void ApplyVisuals(GameplaySnapshotMessage message)
    {
        for (int i = 0; i < persistentRenderers.Length; i++)
        {
            var r = persistentRenderers[i]; if (r == null) continue;
            var v = message.visuals[i];
            r.GetPropertyBlock(block);
            float blend = IsFollower && !applyingSnapshot ? 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime) : 1f;
            if (v.distanceSet) block.SetFloat(DistanceId, Mathf.Lerp(block.HasFloat(DistanceId) ? block.GetFloat(DistanceId) : v.distance, v.distance, blend));
            if (v.restoreSet) block.SetFloat(RestoreId, Mathf.Lerp(block.HasFloat(RestoreId) ? block.GetFloat(RestoreId) : v.restore, v.restore, blend));
            r.SetPropertyBlock(block);
            r.shadowCastingMode = (UnityEngine.Rendering.ShadowCastingMode)v.shadows;
            var build = buildOwners != null ? buildOwners[i] : null;
            if (build != null && v.distanceSet) build.SetDistance(block.GetFloat(DistanceId));
        }
    }

    private uint CalculateConfiguration()
    {
        uint hash = 2166136261;
        Action<string> add = value => { foreach (char c in value) hash = unchecked((hash ^ c) * 16777619); };
        add(story != null ? story.ChapterCount.ToString() : "missing-story");
        foreach (var z in brickZones) add(z != null ? ObjectPath(z.transform) : "null-zone");
        foreach (var o in persistentObjects) add(o != null ? ObjectPath(o.transform) : "null-object");
        foreach (var r in persistentRenderers) add(r != null ? ObjectPath(r.transform) : "null-renderer");
        return hash;
    }

    private static string ObjectPath(Transform t)
    {
        string path = t.name + "#" + t.GetSiblingIndex();
        while (t.parent != null) { t = t.parent; path = t.name + "#" + t.GetSiblingIndex() + "/" + path; }
        return path;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        if (locomotion != null) locomotion.enabled = locomotionWasEnabled;
        NetworkServer.UnregisterHandler<GameplayRequestMessage>();
        NetworkClient.UnregisterHandler<GameplaySnapshotMessage>();
        Instance = null;
    }
}
