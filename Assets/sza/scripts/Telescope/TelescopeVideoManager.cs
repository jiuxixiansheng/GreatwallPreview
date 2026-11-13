using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Video;

namespace Sza.Telescope
{
    public struct TelescopeTouchMessage : NetworkMessage { }
    public struct TelescopeQueryMessage : NetworkMessage { }
    public struct TelescopeStateMessage : NetworkMessage
    {
        public uint revision;
        public double startTime;
        public bool active, interactionUnlocked;
    }

    /// <summary>Lives on NetworkManager. Video frames are local; only start time is networked.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-9000)]
    public sealed class TelescopeVideoManager : MonoBehaviour
    {
        [Header("望远镜与视频")]
        public TelescopeVideoInteraction interaction;
        public VideoClip videoClip;
        public Shader maskShader;
        [Tooltip("关闭后，每次触碰必须先离开感应范围，再触碰才能重播。")]
        public bool playOnce = true;

        [Header("剧情等待")]
        [Tooltip("在这条 Timeline 的等待信号处暂停；任一玩家触发视频后立即继续，不等待视频结束。")]
        public PlayableDirector waitingTimeline;

        [Header("头显转场")]
        [Min(0.05f)] public float fadeSeconds = 0.45f;
        [Min(0.1f)] public float networkLeadSeconds = 0.9f;
        [Min(0.2f)] public float screenDistance = 0.65f;
        [Range(0.2f, 0.5f)] public float lensRadius = 0.46f;
        [Range(0f, 1f)] public float videoVolume = 1f;
        [Tooltip("服务端仅接受位于望远镜附近的玩家请求，兼容手部同步的小幅延迟。")]
        [Min(0.2f)] public float serverTouchValidationDistance = 2f;

        private TelescopeVideoView view;
        private bool previousServer, previousClient, hasPlayed;
        private uint serverRevision, acceptedRevision;
        private double serverStart, serverEnd, nextQuery;
        private readonly HashSet<int> initializedConnections = new HashSet<int>();
        private readonly Dictionary<int, double> requestTimes = new Dictionary<int, double>();
        private TelescopeStateMessage pending;
        private bool hasPending;
        private bool waitingForVideo;
        private bool interactionUnlocked;

        public bool IsViewing => view != null && view.IsViewing;
        public bool IsInteractionUnlocked => interactionUnlocked;
        public bool CanInteract => interactionUnlocked && interaction != null && interaction.IsAvailable && videoClip != null &&
            !IsViewing && !hasPending && !(playOnce && hasPlayed) &&
            !(NetworkServer.active && NetworkTime.time < serverEnd);

        public static Camera LocalCamera
        {
            get
            {
                if (NetworkClient.active)
                {
                    var player = NetworkClient.localPlayer;
                    if (player == null) return null;
                    var setup = player.GetComponent<PlayerNetworkSetup>();
                    return setup != null ? setup.localCamera : player.GetComponentInChildren<Camera>(true);
                }
                return Camera.main;
            }
        }

        private void Awake()
        {
            view = gameObject.AddComponent<TelescopeVideoView>();
            RegisterMessages();
        }

        private void RegisterMessages()
        {
            NetworkServer.ReplaceHandler<TelescopeTouchMessage>(OnTouchRequest);
            NetworkServer.ReplaceHandler<TelescopeQueryMessage>(OnQuery);
            NetworkClient.ReplaceHandler<TelescopeStateMessage>(OnState);
        }

        private void Update()
        {
            if (previousServer != NetworkServer.active || previousClient != NetworkClient.active)
            {
                RegisterMessages();
                if (!NetworkClient.active && !NetworkServer.active)
                {
                    view.StopViewing();
                    hasPending = hasPlayed = false;
                    waitingForVideo = false;
                    interactionUnlocked = false;
                    if (interaction != null) interaction.RefreshPrompt();
                    acceptedRevision = serverRevision = 0;
                    serverStart = serverEnd = 0d;
                    initializedConnections.Clear();
                    requestTimes.Clear();
                }
                previousServer = NetworkServer.active;
                previousClient = NetworkClient.active;
            }
            if (NetworkServer.active)
                foreach (var pair in NetworkServer.connections)
                    if (pair.Value != null && pair.Value.isReady && pair.Value.identity != null &&
                        initializedConnections.Add(pair.Key)) pair.Value.Send(CurrentState());
            if (NetworkClient.isConnected && NetworkClient.localPlayer != null && NetworkTime.time >= nextQuery)
            {
                nextQuery = NetworkTime.time + 2d;
                NetworkClient.Send(new TelescopeQueryMessage());
            }
            if (hasPending && LocalCamera != null)
            {
                hasPending = false;
                BeginViewing(pending.startTime, true);
            }
        }

        public void RequestViewing()
        {
            if (!CanInteract) return;
            if (NetworkServer.active) StartSharedViewing();
            else if (NetworkClient.isConnected && NetworkClient.localPlayer != null)
                NetworkClient.Send(new TelescopeTouchMessage());
            else if (!NetworkClient.active)
            {
                hasPlayed = true;
                BeginViewing(Time.unscaledTimeAsDouble + Math.Max(networkLeadSeconds, fadeSeconds), false);
                ResumeWaitingTimeline();
            }
        }

        private void OnTouchRequest(NetworkConnectionToClient sender, TelescopeTouchMessage message)
        {
            if (sender == null || !sender.isReady || sender.identity == null || !CanInteract) return;
            if (!interaction.IsPlayerNearby(sender.identity, serverTouchValidationDistance)) return;
            if (requestTimes.TryGetValue(sender.connectionId, out double last) && NetworkTime.time - last < 0.5d) return;
            requestTimes[sender.connectionId] = NetworkTime.time;
            StartSharedViewing();
        }

        private void StartSharedViewing()
        {
            if (!NetworkServer.active || !CanInteract) return;
            hasPlayed = true;
            serverRevision++;
            serverStart = NetworkTime.time + Math.Max(networkLeadSeconds, fadeSeconds);
            serverEnd = serverStart + videoClip.length + fadeSeconds * 2d + 0.5d;
            NetworkServer.SendToAll(CurrentState(), Channels.Reliable, true);
            ResumeWaitingTimeline();
        }

        // Signals run on every peer. Only the Host (or an offline session) controls the story.
        public void WaitForVideo()
        {
            if (!Application.isPlaying || (NetworkClient.active && !NetworkServer.active) ||
                !CanControlWaitingTimeline() || hasPlayed) return;
            waitingForVideo = true;
            SetWaitingTimelinePlayback(false);
            interactionUnlocked = true;
            if (NetworkServer.active) NetworkServer.SendToAll(CurrentState(), Channels.Reliable, true);
            if (interaction != null) interaction.RefreshPrompt();
        }

        // Leaving the telescope/chapter closes the gate until the next authoritative wait signal.
        public void ResetInteractionGate()
        {
            bool changed = interactionUnlocked;
            interactionUnlocked = waitingForVideo = false;
            if (interaction != null) interaction.RefreshPrompt();
            if (changed && NetworkServer.active)
                NetworkServer.SendToAll(CurrentState(), Channels.Reliable, true);
        }

        private void ResumeWaitingTimeline()
        {
            if (!waitingForVideo) return;
            waitingForVideo = false;
            if (CanControlWaitingTimeline()) SetWaitingTimelinePlayback(true);
        }

        private bool CanControlWaitingTimeline()
        {
            if (waitingTimeline == null || !waitingTimeline.gameObject.activeInHierarchy) return false;
            var coordinator = NetworkGameplayCoordinator.Instance;
            return coordinator == null || !coordinator.isActiveAndEnabled ||
                (coordinator.story != null && coordinator.story.CurrentDirector == waitingTimeline &&
                 !coordinator.story.IsTransitionPending);
        }

        private void SetWaitingTimelinePlayback(bool play)
        {
            var coordinator = NetworkGameplayCoordinator.Instance;
            if (NetworkServer.active && coordinator != null && coordinator.isActiveAndEnabled)
                coordinator.HandleTimelineSignal(waitingTimeline, play);
            else if (!NetworkClient.active || NetworkServer.active)
            {
                if (coordinator != null && coordinator.isActiveAndEnabled && coordinator.story != null)
                    coordinator.story.SetNetworkPlayback(play);
                else if (play) waitingTimeline.Resume();
                else waitingTimeline.Pause();
            }
        }

        private TelescopeStateMessage CurrentState() => new TelescopeStateMessage
        {
            revision = serverRevision, startTime = serverStart,
            interactionUnlocked = interactionUnlocked,
            active = videoClip != null && serverRevision != 0 && NetworkTime.time < serverStart + videoClip.length
        };

        private void OnQuery(NetworkConnectionToClient sender, TelescopeQueryMessage message)
        {
            if (sender != null && sender.isReady && sender.identity != null && videoClip != null)
                sender.Send(CurrentState());
        }

        private void OnState(TelescopeStateMessage message)
        {
            if (message.revision < acceptedRevision) return;
            // Gate changes must be applied even before the first video (revision == 0),
            // and on queries/late joins that repeat an existing playback revision.
            interactionUnlocked = message.interactionUnlocked;
            if (message.revision == 0 || message.revision == acceptedRevision)
            {
                if (interaction != null) interaction.RefreshPrompt();
                return;
            }
            acceptedRevision = message.revision;
            hasPlayed = true;
            if (interaction != null) interaction.RefreshPrompt();
            if (!message.active || videoClip == null ||
                NetworkTime.time >= message.startTime + videoClip.length) return;
            if (LocalCamera == null) { pending = message; hasPending = true; }
            else BeginViewing(message.startTime, true);
        }

        private void BeginViewing(double start, bool synchronized)
        {
            var camera = LocalCamera;
            if (camera == null || videoClip == null || maskShader == null) return;
            view.Play(camera, videoClip, maskShader, start, synchronized, fadeSeconds,
                screenDistance, lensRadius, videoVolume);
        }

        private void OnDisable()
        {
            ResetInteractionGate();
            if (view != null) view.StopViewing();
        }

        private void OnDestroy()
        {
            NetworkServer.UnregisterHandler<TelescopeTouchMessage>();
            NetworkServer.UnregisterHandler<TelescopeQueryMessage>();
            NetworkClient.UnregisterHandler<TelescopeStateMessage>();
        }
    }
}
