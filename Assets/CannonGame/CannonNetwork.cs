using System;
using Mirror;
using UnityEngine;

namespace Greatwall.CannonGame
{
    internal enum CannonItemPhase : byte { Home, Held, Loaded, CoolingDown }
    internal enum CannonCommandKind : byte { Query, Grab, Move, Release }

    internal struct CannonCommand : NetworkMessage
    {
        public CannonCommandKind kind;
        public int item;
        public uint token;
        public Vector3 position;
        public Quaternion rotation;
        public float pull;
    }

    internal struct CannonGrabDenied : NetworkMessage
    {
        public int item;
        public uint token;
    }

    internal struct CannonItemState
    {
        public CannonItemPhase phase;
        public uint holder, token;
        public Vector3 position;
        public Quaternion rotation;
        public float pull;
    }

    internal struct CannonSnapshot : NetworkMessage
    {
        public CannonItemState[] items;
        public int loadedIndex, shots;
    }

    // JiPao and its interactables are initialized at runtime. Index the four scene shells
    // and the chain in a single reliable stream, without adding scene NetworkIdentities.
    internal sealed class CannonNetwork
    {
        private const float SendInterval = 1f / 20f;
        // The near/far interactor reaches 10 m from the hand. Allow for the
        // head-to-hand offset and the remote head pose arriving a little later.
        private const float MaximumReach = 12f;
        private const double RespawnSeconds = 2.8;
        private const double HoldTimeoutSeconds = 3.0;
        private static CannonNetwork current;
        private readonly CannonGame game;
        private readonly CannonChain chain;
        private readonly CannonItemState[] serverItems;
        private readonly double[] respawnAt;
        private readonly double[] holdExpiresAt;
        private readonly uint[] localTokens;
        private readonly float[] grabDeadline;

        private CannonItemState[] visibleItems;
        private int loadedIndex = -1;
        private int shots;
        private uint nextToken;
        private bool serverWasActive, clientWasActive, stateChanged, firedOnThisPull;
        private float nextQuery, nextPose, nextSnapshot;

        internal CannonNetwork(CannonGame game, CannonChain chain)
        {
            this.game = game;
            this.chain = chain;
            int itemCount = game.Ammo.Count + 1;
            serverItems = new CannonItemState[itemCount];
            respawnAt = new double[game.Ammo.Count];
            holdExpiresAt = new double[itemCount];
            localTokens = new uint[itemCount];
            grabDeadline = new float[itemCount];
        }

        internal static bool IsActive => NetworkServer.active || NetworkClient.active;
        internal static void RegisterHandlers()
        {
            // Keep the handlers installed while clients enter/leave the chapter at different times.
            NetworkServer.ReplaceHandler<CannonCommand>((sender, command) => current?.OnCommand(sender, command));
            NetworkClient.ReplaceHandler<CannonSnapshot>(snapshot => current?.OnSnapshot(snapshot));
            NetworkClient.ReplaceHandler<CannonGrabDenied>(denial => current?.OnDenied(denial));
        }

        internal void Enable() => current = this;

        private int ChainIndex => serverItems.Length - 1;
        private static uint LocalPlayerId => NetworkClient.localPlayer != null ? NetworkClient.localPlayer.netId : 0;
        private static bool CanSend => NetworkClient.ready && NetworkClient.isConnected && LocalPlayerId != 0;
        private bool IsItem(int index) => index >= 0 && index < serverItems.Length;

        internal void Tick()
        {
            if (serverWasActive != NetworkServer.active || clientWasActive != NetworkClient.active)
            {
                Reset();
                serverWasActive = NetworkServer.active;
                clientWasActive = NetworkClient.active;
                if (serverWasActive || clientWasActive) RegisterHandlers();
            }

            float now = Time.unscaledTime;
            if (NetworkServer.active) TickServer(now);
            if (!CanSend) return;

            if (visibleItems == null && now >= nextQuery)
            {
                nextQuery = now + 0.5f;
                NetworkClient.Send(new CannonCommand { kind = CannonCommandKind.Query });
            }

            if (now < nextPose) return;
            nextPose = now + SendInterval;
            for (int i = 0; i < localTokens.Length; i++)
            {
                if (localTokens[i] == 0) continue;
                if (now >= grabDeadline[i])
                {
                    Release(i);
                    CancelSelection(i);
                }
                else
                {
                    Send(CannonCommandKind.Move, i, localTokens[i]);
                }
            }
        }

        internal bool CanSelect(int item)
        {
            if (!IsActive) return true;
            if (!CanSend || visibleItems == null) return false;
            return localTokens[item] != 0 ||
                (visibleItems[item].phase == CannonItemPhase.Home &&
                (item != ChainIndex || game.LoadedAmmo != null));
        }

        internal void Grab(int item)
        {
            if (!CanSelect(item) || localTokens[item] != 0) return;
            if (++nextToken == 0) ++nextToken;
            localTokens[item] = nextToken;
            grabDeadline[item] = Time.unscaledTime + 3f;
            Send(CannonCommandKind.Grab, item, nextToken);
        }

        internal void Release(int item)
        {
            uint token = localTokens[item];
            localTokens[item] = 0;
            if (token != 0 && CanSend) Send(CannonCommandKind.Release, item, token);
        }

        internal void SendChainThreshold()
        {
            if (CanSend && localTokens[ChainIndex] != 0)
                Send(CannonCommandKind.Move, ChainIndex, localTokens[ChainIndex]);
        }

        private void Send(CannonCommandKind kind, int item, uint token)
        {
            var command = new CannonCommand { kind = kind, item = item, token = token };
            if (item == ChainIndex)
                command.pull = chain.Pull;
            else
            {
                var pose = game.Ammo[item].transform;
                command.position = pose.position;
                command.rotation = pose.rotation;
            }
            // The same reliable channel orders the claim, the last hand pose and release.
            NetworkClient.Send(command);
        }

        private void TickServer(float now)
        {
            for (int i = 0; i < serverItems.Length; i++)
            {
                var item = serverItems[i];
                if ((item.phase == CannonItemPhase.Held &&
                        (!IsOnline(item.holder) || NetworkTime.time >= holdExpiresAt[i])) ||
                    (i < respawnAt.Length && item.phase == CannonItemPhase.CoolingDown && NetworkTime.time >= respawnAt[i]))
                    ReturnHome(i);
            }
            if (stateChanged && now >= nextSnapshot) Broadcast();
        }

        private static bool IsOnline(uint holder)
        {
            return NetworkServer.spawned.TryGetValue(holder, out var identity) && identity != null &&
                identity.connectionToClient != null && identity.connectionToClient.isReady;
        }

        private void OnCommand(NetworkConnectionToClient sender, CannonCommand command)
        {
            if (!game.isActiveAndEnabled || sender == null || !sender.isReady || sender.identity == null ||
                sender.identity.netId == 0) return;
            if (command.kind == CannonCommandKind.Query)
            {
                sender.Send(Capture());
                return;
            }
            int i = command.item;
            if (!IsItem(i) || command.token == 0) return;
            if (command.kind == CannonCommandKind.Grab)
            {
                if (!TryClaim(sender.identity, i, command.token))
                    sender.Send(new CannonGrabDenied { item = i, token = command.token });
                return;
            }
            var item = serverItems[i];
            if (item.holder != sender.identity.netId || item.token != command.token ||
                (command.kind != CannonCommandKind.Move && command.kind != CannonCommandKind.Release)) return;

            holdExpiresAt[i] = NetworkTime.time + HoldTimeoutSeconds;
            ApplyMotion(sender.identity, command);
            if (command.kind == CannonCommandKind.Release && serverItems[i].phase == CannonItemPhase.Held)
            {
                ReturnHome(i);
                Broadcast();
            }
        }

        private bool TryClaim(NetworkIdentity player, int i, uint token)
        {
            if (serverItems[i].phase != CannonItemPhase.Home ||
                (i == ChainIndex && loadedIndex < 0)) return false;
            Vector3 home = i == ChainIndex ? chain.GrabHomePosition : game.Ammo[i].HomePosition;
            if (!WithinReach(player, home)) return false;

            serverItems[i] = new CannonItemState
            {
                phase = CannonItemPhase.Held, holder = player.netId, token = token, position = home,
                rotation = i == ChainIndex ? Quaternion.identity : game.Ammo[i].HomeRotation
            };
            holdExpiresAt[i] = NetworkTime.time + HoldTimeoutSeconds;
            if (i == ChainIndex) firedOnThisPull = false;
            Broadcast();
            return true;
        }

        private void ApplyMotion(NetworkIdentity player, CannonCommand command)
        {
            int i = command.item;
            if (i == ChainIndex)
            {
                if (!float.IsFinite(command.pull) || !WithinReach(player, chain.GrabHomePosition)) return;
                float pull = Mathf.Clamp(command.pull, 0f, CannonChain.MaxPull);
                stateChanged |= serverItems[i].pull != pull;
                serverItems[i].pull = pull;
                if (!firedOnThisPull && loadedIndex >= 0 && pull >= CannonChain.FireThreshold)
                {
                    firedOnThisPull = true;
                    serverItems[loadedIndex] = new CannonItemState { phase = CannonItemPhase.CoolingDown };
                    respawnAt[loadedIndex] = NetworkTime.time + RespawnSeconds;
                    loadedIndex = -1;
                    shots++;
                    Broadcast();
                }
                return;
            }

            if (!WithinReach(player, command.position) || !ValidRotation(command.rotation)) return;
            stateChanged |= serverItems[i].position != command.position || serverItems[i].rotation != command.rotation;
            serverItems[i].position = command.position;
            serverItems[i].rotation = command.rotation.normalized;
            if (loadedIndex >= 0 || !game.IsInSlot(command.position)) return;
            serverItems[i] = new CannonItemState { phase = CannonItemPhase.Loaded };
            loadedIndex = i;
            Broadcast();
        }

        private static bool WithinReach(NetworkIdentity player, Vector3 point)
        {
            var setup = player.GetComponent<PlayerNetworkSetup>();
            Vector3 origin = setup != null && setup.localCamera != null
                ? setup.localCamera.transform.position : player.transform.position;
            return (point - origin).sqrMagnitude <= MaximumReach * MaximumReach;
        }

        private static bool ValidRotation(Quaternion value)
        {
            float norm = Quaternion.Dot(value, value);
            return norm > 0.5f && norm < 1.5f;
        }

        private void ReturnHome(int i)
        {
            serverItems[i] = default;
            stateChanged = true;
        }

        private CannonSnapshot Capture() => new CannonSnapshot
        {
            items = serverItems, loadedIndex = loadedIndex, shots = shots
        };

        private void Broadcast()
        {
            NetworkServer.SendToAll(Capture(), Channels.Reliable, true);
            stateChanged = false;
            nextSnapshot = Time.unscaledTime + SendInterval;
        }

        private void OnSnapshot(CannonSnapshot snapshot)
        {
            if (!game.isActiveAndEnabled || snapshot.items == null || snapshot.items.Length != serverItems.Length ||
                snapshot.loadedIndex < -1 || snapshot.loadedIndex >= ChainIndex) return;
            bool initial = visibleItems == null;
            visibleItems = snapshot.items;
            for (int i = 0; i < visibleItems.Length; i++)
            {
                var item = visibleItems[i];
                bool local = localTokens[i] != 0 && item.holder == LocalPlayerId && item.token == localTokens[i];
                if (local) grabDeadline[i] = float.PositiveInfinity;
                else if (localTokens[i] != 0 && (item.phase != CannonItemPhase.Home ||
                    float.IsPositiveInfinity(grabDeadline[i])))
                    CancelSelection(i);

                if (i == ChainIndex) chain.ApplyNetworkPull(item, local, initial);
                else game.Ammo[i].ApplyNetworkState(item, game.SlotPosition, game.SlotRotation, local, initial);
            }
            game.ApplyNetworkCannonState(snapshot.loadedIndex, snapshot.shots, initial);
        }

        private void OnDenied(CannonGrabDenied denial)
        {
            if (IsItem(denial.item) && denial.token != 0 && localTokens[denial.item] == denial.token)
                CancelSelection(denial.item);
        }

        private void CancelSelection(int i)
        {
            localTokens[i] = 0;   // Cancellation callbacks must not send a second release.
            if (i == ChainIndex) chain.CancelNetworkSelection();
            else game.Ammo[i].CancelNetworkSelection();
        }

        internal void Disable()
        {
            if (current == this) current = null;
            if (serverWasActive || clientWasActive) Reset();
            serverWasActive = clientWasActive = false;
        }

        private void Reset()
        {
            Array.Clear(serverItems, 0, serverItems.Length);
            loadedIndex = -1;
            shots = 0;
            stateChanged = firedOnThisPull = false;
            visibleItems = null;
            nextQuery = nextPose = nextSnapshot = 0f;
            for (int i = 0; i < localTokens.Length; i++) CancelSelection(i);
            foreach (var ammo in game.Ammo) ammo.ResetNetworkState();
            chain.ApplyNetworkPull(default, false, true);
            game.ApplyNetworkCannonState(-1, 0, true);
        }
    }
}
