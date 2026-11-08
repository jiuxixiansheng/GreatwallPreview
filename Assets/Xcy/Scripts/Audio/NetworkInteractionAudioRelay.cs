using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Xcy.CrackRepair;

namespace Xcy.Audio
{
    public struct InteractionSoundMessage : NetworkMessage
    {
        public uint configuration, sequence, epoch;
        public int chapter, emitter;
        public InteractionAudioCue cue;
        public Vector3 position;
        public bool stop;
    }

    // Clients can request only a specific interaction, never an arbitrary audio clip.
    public struct InteractionRequestMessage : NetworkMessage
    {
        public uint configuration, epoch;
        public byte kind; // 0: state, 1: prologue, 2: crack repair
        public int index;
    }

    public struct InteractionStateMessage : NetworkMessage
    {
        public uint configuration, epoch;
        public int chapter;
        public bool brickPrompt, crackPrompt, repairUnlocked, repairCompleted;
        public byte[] cracks;
        public double[] startedAt;
    }

    [DisallowMultipleComponent, DefaultExecutionOrder(-9000)]
    [RequireComponent(typeof(InteractionAudioPlayer))]
    public sealed class NetworkInteractionAudioRelay : MonoBehaviour
    {
        public static NetworkInteractionAudioRelay Instance { get; private set; }
        [Min(1f)] public float maximumInteractionDistance = 12f;
        public int DeniedRequests { get; private set; }
        public uint Epoch => epoch;
        public uint Configuration => player != null && player.library != null ? player.library.ConfigurationHash() : 0;

        private InteractionAudioPlayer player;
        private bool previousServer, previousClient, previousConnected, brickPrompt, crackPrompt, prologueCommitted;
        private int chapter = -1;
        private uint epoch, sequence, receivedSequence;
        private double nextState, nextStateRequest;
        private readonly Dictionary<int, double> requestTimes = new Dictionary<int, double>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; return; }
            Instance = this;
            player = GetComponent<InteractionAudioPlayer>();
            RegisterHandlers();
        }

        private void RegisterHandlers()
        {
            NetworkServer.ReplaceHandler<InteractionRequestMessage>(OnRequest);
            NetworkClient.ReplaceHandler<InteractionSoundMessage>(OnSound);
            NetworkClient.ReplaceHandler<InteractionStateMessage>(OnState);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            NetworkServer.UnregisterHandler<InteractionRequestMessage>();
            NetworkClient.UnregisterHandler<InteractionSoundMessage>();
            NetworkClient.UnregisterHandler<InteractionStateMessage>();
        }

        private void Update()
        {
            if (player == null || player.story == null) return;
            if (previousServer != NetworkServer.active || previousClient != NetworkClient.active)
            {
                if (previousServer || previousClient) player.StopAllAudio();
                RegisterHandlers();
                previousServer = NetworkServer.active;
                previousClient = NetworkClient.active;
                receivedSequence = 0;
                requestTimes.Clear();
            }
            if (previousConnected && !NetworkClient.isConnected && !NetworkServer.active) player.StopAllAudio();
            previousConnected = NetworkClient.isConnected;
            if (chapter != player.story.CurrentChapterIndex)
            {
                chapter = player.story.CurrentChapterIndex;
                if (NetworkServer.active || !NetworkClient.active) epoch++;
                brickPrompt = crackPrompt = prologueCommitted = false;
                requestTimes.Clear();
            }
            if (NetworkServer.active && NetworkTime.time >= nextState)
            {
                nextState = NetworkTime.time + 0.25d;
                NetworkServer.SendToAll(CaptureState(), Channels.Reliable, true);
            }
            else if (!NetworkServer.active && NetworkClient.isConnected && NetworkClient.localPlayer != null &&
                     NetworkTime.time >= nextStateRequest)
            {
                nextStateRequest = NetworkTime.time + 2d;
                SendRequest(0, -1);
            }
        }

        public void EmitAuthority(InteractionAudioCue cue, int emitter, Vector3 position, bool stop = false)
        {
            if (!NetworkServer.active && NetworkClient.active) return;
            if (player == null || player.story == null) return;
            if (cue == InteractionAudioCue.BrickPrompt) brickPrompt = !stop;
            if (cue == InteractionAudioCue.CrackPrompt) crackPrompt = !stop;
            player.ApplyCue(cue, emitter, position, stop);
            if (NetworkServer.active)
                NetworkServer.SendToAll(new InteractionSoundMessage
                {
                    configuration = Configuration, epoch = epoch, sequence = ++sequence,
                    chapter = player.story.CurrentChapterIndex, cue = cue, emitter = emitter,
                    position = position, stop = stop
                }, Channels.Reliable, true);
        }

        private void OnSound(InteractionSoundMessage message)
        {
            // Host already played it at the source. Snapshots never replay one-shots.
            if (NetworkServer.active || message.configuration != Configuration || player.story == null ||
                message.chapter != player.story.CurrentChapterIndex || message.epoch < epoch ||
                message.sequence <= receivedSequence || message.emitter < 0 || message.emitter > 3) return;
            epoch = message.epoch;
            receivedSequence = message.sequence;
            player.ApplyCue(message.cue, message.emitter, message.position, message.stop);
        }

        public void RequestPrologue()
        {
            if (NetworkServer.active || !NetworkClient.active) AcceptPrologue();
            else SendRequest(1, 0);
        }

        private void AcceptPrologue()
        {
            if (player.story == null || player.story.CurrentChapterIndex != 0 || player.story.IsTransitionPending || prologueCommitted) return;
            prologueCommitted = true;
            player.RunPrologueFromAuthority();
        }

        public static bool RouteCrackRepair(CrackRepairDecal crack)
        {
            if (!NetworkServer.active && !NetworkClient.active) return false;
            var relay = Instance;
            if (relay == null || relay.player.crackRepair == null) return true;
            int index = relay.player.crackRepair.IndexOf(crack);
            if (NetworkServer.active) relay.AcceptCrack(index);
            else relay.SendRequest(2, index);
            return true;
        }

        private bool AcceptCrack(int index)
        {
            var coordinator = player.crackRepair;
            if (player.story == null || player.story.CurrentChapterIndex != 2 || player.story.IsTransitionPending ||
                coordinator == null || !coordinator.isActiveAndEnabled || !coordinator.IsRepairUnlocked ||
                coordinator.IsRepairCompleted || index < 0 || index >= coordinator.Cracks.Count) return false;
            var crack = coordinator.Cracks[index];
            if (crack == null || !crack.CanStartRepair) return false;
            crack.StartRepairFromAuthority();
            return true;
        }

        private void SendRequest(byte kind, int index)
        {
            if (!NetworkClient.isConnected || NetworkClient.localPlayer == null) return;
            NetworkClient.Send(new InteractionRequestMessage { configuration = Configuration, epoch = epoch, kind = kind, index = index });
        }

        private bool Near(NetworkConnectionToClient sender, Vector3 position)
        {
            if (sender == null || !sender.isAuthenticated || sender.identity == null) return false;
            var camera = sender.identity.GetComponentInChildren<Camera>(true);
            Vector3 head = camera != null ? camera.transform.position : sender.identity.transform.position;
            return Vector3.Distance(head, position) <= maximumInteractionDistance;
        }

        private void OnRequest(NetworkConnectionToClient sender, InteractionRequestMessage message)
        {
            if (sender == null || sender.identity == null || message.configuration != Configuration)
            { DeniedRequests++; return; }
            if (message.kind == 0) { sender.Send(CaptureState()); return; }
            if (message.epoch != epoch || player.story == null || player.story.IsTransitionPending ||
                requestTimes.TryGetValue(sender.connectionId, out double previous) && NetworkTime.time - previous < 0.1d)
            { DeniedRequests++; return; }
            requestTimes[sender.connectionId] = NetworkTime.time;
            if (message.kind == 1 && player.prologueTarget != null && Near(sender, player.prologueTarget.position) &&
                player.story.CurrentChapterIndex == 0 && !prologueCommitted) { AcceptPrologue(); return; }
            var coordinator = player.crackRepair;
            if (message.kind == 2 && coordinator != null && message.index >= 0 && message.index < coordinator.Cracks.Count)
            {
                var crack = coordinator.Cracks[message.index];
                if (crack != null && Near(sender, crack.transform.position) && AcceptCrack(message.index)) return;
            }
            DeniedRequests++;
        }

        private InteractionStateMessage CaptureState()
        {
            var coordinator = player.crackRepair;
            int count = coordinator != null ? coordinator.Cracks.Count : 0;
            var states = new byte[count];
            var started = new double[count];
            for (int i = 0; i < count; i++)
            {
                var crack = coordinator.Cracks[i];
                if (crack == null) continue;
                states[i] = crack.IsRepaired ? (byte)2 : crack.IsRepairing ? (byte)1 : (byte)0;
                started[i] = crack.RepairStartedAt;
            }
            return new InteractionStateMessage
            {
                configuration = Configuration, epoch = epoch,
                chapter = player.story != null ? player.story.CurrentChapterIndex : -1,
                brickPrompt = brickPrompt, crackPrompt = crackPrompt,
                repairUnlocked = coordinator != null && coordinator.IsRepairUnlocked,
                repairCompleted = coordinator != null && coordinator.IsRepairCompleted,
                cracks = states, startedAt = started
            };
        }

        private void OnState(InteractionStateMessage message)
        {
            if (NetworkServer.active || message.configuration != Configuration || player.story == null ||
                message.chapter != player.story.CurrentChapterIndex || message.epoch < epoch) return;
            var coordinator = player.crackRepair;
            if (coordinator == null || message.cracks == null || message.startedAt == null ||
                message.cracks.Length != coordinator.Cracks.Count || message.startedAt.Length != message.cracks.Length) return;
            epoch = message.epoch;
            if (message.chapter == 1 && player.brickZones != null && player.brickZones.Length > 0)
                player.ApplyCue(InteractionAudioCue.BrickPrompt, 0, player.brickZones[0].transform.position, !message.brickPrompt);
            if (message.chapter != 2 || !coordinator.isActiveAndEnabled) return;
            coordinator.ApplyNetworkRepairState(message.repairUnlocked, message.repairCompleted);
            player.ApplyCue(InteractionAudioCue.CrackPrompt, 0, coordinator.transform.position, !message.crackPrompt);
            for (int i = 0; i < message.cracks.Length; i++)
            {
                if (message.cracks[i] > 2) continue;
                var crack = coordinator.Cracks[i];
                if (crack == null) continue;
                crack.ApplyNetworkRepairState(message.cracks[i], message.startedAt[i], message.repairUnlocked);
                player.ApplyCue(InteractionAudioCue.CrackRepair, i, crack.transform.position, message.cracks[i] != 1);
            }
        }
    }
}
