using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Sza.Bow
{
    /// <summary>Two-hand input is predicted locally; accepted holders send ordered draw/release requests.</summary>
    [RequireComponent(typeof(NetworkMagicBow))]
    public sealed class NetworkMagicBowWeapon : NetworkBehaviour, IXRSelectFilter
    {
        public NetworkMagicBow bow;
        public BowStringHandle stringHandle;
        public Transform stringBone;
        public Vector3 relaxedNock;
        [Header("双手拉弓（米）")]
        [Min(0.02f)] public float stringGrabDistance = 0.14f;
        [Min(0.02f)] public float arrowThreshold = 0.2f;
        [Min(0.05f)] public float maximumPull = 0.65f;
        [Tooltip("只限制弓弦的视觉拉伸范围，超出此距离不会自动松手。")]
        [Min(0f)] public float maximumLateralPull = 0.75f;
        [Min(0.02f)] public float arrowDissolveSeconds = 0.3f;
        [Header("玩家射箭")]
        [Min(0.1f)] public float projectileSpeed = 18f;
        [Min(0.1f)] public float maximumShotDistance = 24f;
        [Min(0.001f)] public float hitRadius = 0.025f;
        public LayerMask hitLayers = ~0;
        [Min(0.05f)] public float shotCooldown = 0.35f;
        [SyncVar] private float pull;
        [SyncVar] private Vector2 lateral;
        [SyncVar] private bool drawing, loaded;
        [SyncVar] private double revealStart;
        private XRGrabInteractable grip;
        private NetworkGrabbable networkGrab;
        private IXRSelectInteractor pullingHand;
        private XRBaseInteractor persistentInteractor;
        private bool originalKeepSelectedTarget;
        private Vector3 arrowNockLocal;
        private Quaternion arrowRotation;
        private MaterialPropertyBlock material;
        private bool cached, localDrawing, localLoaded, canceling;
        private float localPull, renderPull, nextSend;
        private Vector2 localLateral, renderLateral;
        private double localRevealStart, nextShot, suppressNockUntil, lastPullMessage;
        private uint localCharge, serverCharge, chargeHolder, shotSequence;
        private readonly Dictionary<uint, MagicBowProjectile> projectiles = new Dictionary<uint, MagicBowProjectile>();
        private readonly List<GameObject> impacts = new List<GameObject>();
        public static double Clock => NetworkClient.active || NetworkServer.active ? NetworkTime.time : Time.unscaledTimeAsDouble;
        private bool Authority => NetworkServer.active || !NetworkClient.active;
        public bool canProcess => isActiveAndEnabled;
        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) =>
            interactor is XRDirectInteractor || interactor is NearFarInteractor;
        public bool IsLocalHolder => isActiveAndEnabled && bow != null && bow.Dropped && grip != null && grip.isSelected &&
            (!NetworkClient.active && !NetworkServer.active || NetworkClient.localPlayer != null &&
                networkGrab.HolderNetId == NetworkClient.localPlayer.netId);
        public float CurrentPull => pull;
        public bool ArrowLoaded => loaded;
        public int ActiveProjectiles => projectiles.Count;
        public uint ShotsFired => shotSequence;
        public int ImpactCount { get; private set; }
        public Vector3 LastImpactPoint { get; private set; }

        private void Awake() => Cache();
        private void Cache()
        {
            if (cached) return;
            cached = true;
            if (bow == null) bow = GetComponent<NetworkMagicBow>();
            grip = GetComponent<XRGrabInteractable>();
            networkGrab = GetComponent<NetworkGrabbable>();
            material = new MaterialPropertyBlock();
            var bounds = bow.arrow.GetComponent<MeshFilter>().sharedMesh.bounds;
            arrowNockLocal = new Vector3(bounds.min.x, bounds.center.y, bounds.center.z);
            arrowRotation = bow.arrow.localRotation;
            grip.selectEntered.AddListener(OnGrip);
            grip.selectExited.AddListener(OnDrop);
            grip.selectFilters.Add(this);
        }

        private void OnGrip(SelectEnterEventArgs args)
        {
            var nearFar = args.interactorObject as NearFarInteractor;
            if (nearFar != null && nearFar.interactionAttachController != null)
                nearFar.interactionAttachController.ResetOffset();
        }
        private void OnDrop(SelectExitEventArgs args) => CancelLocalPull();

        private static Transform Hand(IXRSelectInteractor interactor)
        {
            for (var t = interactor.transform; t != null; t = t.parent)
                if (t.name.ToLowerInvariant().Contains("controller")) return t;
            return interactor.transform;
        }
        public bool CanStartPull(IXRSelectInteractor interactor)
        {
            Cache();
            if (!IsLocalHolder || interactor == null || !Process(interactor, stringHandle) || grip.interactorsSelecting.Count == 0) return false;
            var holding = grip.interactorsSelecting[0];
            if (holding == interactor || Hand(holding) == Hand(interactor)) return false;
            var a = holding as XRBaseInteractor;
            var b = interactor as XRBaseInteractor;
            if (a != null && b != null && a.handedness != InteractorHandedness.None && a.handedness == b.handedness) return false;
            return Vector3.Distance(Hand(interactor).position, stringHandle.transform.position) <= stringGrabDistance;
        }
        public void BeginPull(IXRSelectInteractor hand)
        {
            if (!CanStartPull(hand)) { CancelLocalPull(); return; }
            pullingHand = hand;
            persistentInteractor = hand as XRBaseInteractor;
            if (persistentInteractor != null)
            {
                originalKeepSelectedTarget = persistentInteractor.keepSelectedTargetValid;
                persistentInteractor.keepSelectedTargetValid = true;
            }
            localDrawing = true; localLoaded = false; localPull = 0; localLateral = Vector2.zero;
            if (++localCharge == 0) ++localCharge;
            nextSend = 0;
            if (NetworkClient.active) CmdBeginPull(localCharge, Hand(hand).position);
            else BeginServer(localCharge);
        }
        public void EndPull(bool fire)
        {
            if (!localDrawing) return;
            float distance = MeasurePull();
            Vector2 offset = MeasureLateral();
            localDrawing = false;
            if (persistentInteractor != null)
                persistentInteractor.keepSelectedTargetValid = originalKeepSelectedTarget;
            persistentInteractor = null;
            pullingHand = null;
            suppressNockUntil = Clock + 0.2d;
            if (NetworkClient.active) CmdEndPull(localCharge, distance, offset, fire && IsLocalHolder);
            else EndServer(localCharge, distance, offset, fire && IsLocalHolder,
                grip.interactorsSelecting.Count > 0 ? grip.interactorsSelecting[0].transform.GetComponentInParent<NetworkIdentity>() : null);
            localLoaded = false;
        }
        private float MeasurePull() => pullingHand == null ? localPull :
            Mathf.Clamp(Vector3.Dot(bow.model.transform.TransformPoint(relaxedNock) - Hand(pullingHand).position,
                transform.right), 0f, maximumPull);
        private Vector2 MeasureLateral()
        {
            if (pullingHand == null) return localLateral;
            Vector3 delta = Hand(pullingHand).position - bow.model.transform.TransformPoint(relaxedNock);
            return Vector2.ClampMagnitude(new Vector2(Vector3.Dot(delta, transform.up),
                Vector3.Dot(delta, transform.forward)), maximumLateralPull);
        }

        private void Update()
        {
            if (!Application.isPlaying || !bow.Dropped) return;
            if (localDrawing)
            {
                if (!IsLocalHolder || pullingHand == null || !pullingHand.transform.gameObject.activeInHierarchy)
                    CancelLocalPull();
                else
                {
                    // A held grab remains held when the hand moves sideways or beyond the visual stretch limit.
                    localPull = MeasurePull();
                    localLateral = MeasureLateral();
                    SetLoaded(localPull, ref localLoaded, ref localRevealStart);
                    if (Time.unscaledTime >= nextSend)
                    {
                        nextSend = Time.unscaledTime + 1f / 30f;
                        if (NetworkClient.active) CmdUpdatePull(localCharge, localPull, localLateral);
                        else UpdateServer(localCharge, localPull, localLateral);
                    }
                }
            }
            if (Authority && drawing && (NetworkServer.active && networkGrab.HolderNetId != chargeHolder || Clock - lastPullMessage > 1d))
                ResetServerCharge();
        }

        private void CancelLocalPull()
        {
            if (canceling) return;
            canceling = true;
            EndPull(false);
            if (stringHandle != null && stringHandle.isSelected && stringHandle.interactionManager != null)
                stringHandle.interactionManager.CancelInteractableSelection((IXRSelectInteractable)stringHandle);
            canceling = false;
        }
        private void SetLoaded(float distance, ref bool ready, ref double started)
        {
            if (distance >= arrowThreshold && !ready) { ready = true; started = Clock; }
            else if (distance < arrowThreshold * 0.6f) ready = false;
        }
        private bool Holder(NetworkConnectionToClient sender) => isActiveAndEnabled && sender != null && sender.identity != null &&
            networkGrab.HolderNetId == sender.identity.netId && bow.Dropped && !networkGrab.ScriptedMotion;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private void BeginServer(uint charge)
        {
            if (drawing && chargeHolder != networkGrab.HolderNetId) ResetServerCharge();
            if (charge == 0 || drawing) return;
            serverCharge = charge; chargeHolder = networkGrab.HolderNetId;
            drawing = true; loaded = false; pull = 0f; lateral = Vector2.zero; lastPullMessage = Clock;
        }
        private void UpdateServer(uint charge, float distance, Vector2 offset)
        {
            if (!drawing || charge != serverCharge || !Finite(distance) || !Finite(offset.x) || !Finite(offset.y)) return;
            pull = Mathf.Clamp(distance, 0f, maximumPull); lastPullMessage = Clock;
            lateral = Vector2.ClampMagnitude(offset, maximumLateralPull);
            bool ready = loaded; double started = revealStart;
            SetLoaded(pull, ref ready, ref started);
            loaded = ready; revealStart = started;
        }
        private void EndServer(uint charge, float distance, Vector2 offset, bool fire, NetworkIdentity shooter)
        {
            if (!drawing || charge != serverCharge || !Finite(distance) || !Finite(offset.x) || !Finite(offset.y)) return;
            UpdateServer(charge, distance, offset);
            bool shoot = fire && loaded && pull >= arrowThreshold && Clock >= nextShot && projectiles.Count < 8;
            if (shoot)
            {
                // Refresh the pose before capturing the arrow; a dedicated server has no selected XRI hand.
                renderPull = pull;
                renderLateral = lateral;
                RenderPose();
                Vector3 position = bow.arrow.position;
                Quaternion rotation = bow.arrow.rotation;
                Vector3 scale = bow.arrow.lossyScale;
                double now = Clock;
                uint id = ++shotSequence;
                Vector3 forward = bow.arrow.right;
                SpawnProjectile(id, position, rotation, scale, forward, now, revealStart, shooter, true);
                if (NetworkServer.active) RpcShot(id, position, rotation, scale, forward, now, revealStart);
                nextShot = now + shotCooldown;
            }
            ResetServerCharge();
        }
        private void ResetServerCharge() { drawing = loaded = false; pull = 0f; lateral = Vector2.zero; serverCharge = 0; }

        [Command(requiresAuthority = false)]
        private void CmdBeginPull(uint charge, Vector3 hand, NetworkConnectionToClient sender = null)
        {
            Vector3 expected = bow.model.transform.TransformPoint(relaxedNock);
            if (!Holder(sender) || !Finite(hand.x) || !Finite(hand.y) || !Finite(hand.z) ||
                Vector3.Distance(hand, expected) > stringGrabDistance + 0.35f) return;
            BeginServer(charge);
        }
        [Command(requiresAuthority = false)]
        private void CmdUpdatePull(uint charge, float distance, Vector2 offset, NetworkConnectionToClient sender = null)
        { if (Holder(sender)) UpdateServer(charge, distance, offset); }
        [Command(requiresAuthority = false)]
        private void CmdEndPull(uint charge, float distance, Vector2 offset, bool fire, NetworkConnectionToClient sender = null)
        { if (Holder(sender)) EndServer(charge, distance, offset, fire, sender.identity); }

        public void RenderPose()
        {
            Cache();
            bool predicted = localDrawing && IsLocalHolder;
            bool active = predicted || drawing;
            float desired = predicted ? localPull : drawing ? pull : 0f;
            Vector2 desiredLateral = predicted ? localLateral : drawing ? lateral : Vector2.zero;
            renderPull = Application.isPlaying ? Mathf.MoveTowards(renderPull, desired, Time.unscaledDeltaTime * 6f) : desired;
            renderLateral = Application.isPlaying ? Vector2.MoveTowards(renderLateral, desiredLateral, Time.unscaledDeltaTime * 6f) : desiredLateral;
            float draw = Mathf.Clamp01(renderPull / Mathf.Max(0.05f, maximumPull));
            bow.drawAndFire.SampleAnimation(bow.model, draw);
            Vector3 nock = bow.model.transform.TransformPoint(relaxedNock) - transform.right * renderPull +
                transform.up * renderLateral.x + transform.forward * renderLateral.y;
            stringBone.position = nock;
            stringHandle.transform.SetPositionAndRotation(nock, transform.rotation);
            stringHandle.gameObject.SetActive(Application.isPlaying && bow.Dropped);
            Vector3 aim = grip.attachTransform.position - nock;
            bow.arrow.rotation = Quaternion.FromToRotation(transform.right, aim.normalized) * transform.rotation * arrowRotation;
            Vector3 scaledNock = Vector3.Scale(arrowNockLocal, bow.arrow.lossyScale);
            bow.arrow.position = nock - bow.arrow.rotation * scaledNock;
            bool visible = active && (predicted ? localLoaded : loaded) && Clock >= suppressNockUntil;
            bow.arrow.gameObject.SetActive(visible);
            double started = predicted ? localRevealStart : revealStart;
            bow.arrowRenderer.GetPropertyBlock(material);
            material.SetFloat("_Reveal", visible ? Mathf.Clamp01((float)(Clock - started) / Mathf.Max(0.02f, arrowDissolveSeconds)) : 0f);
            material.SetFloat("_Highlight", 0f);
            bow.arrowRenderer.SetPropertyBlock(material);
        }

        private void SpawnProjectile(uint id, Vector3 position, Quaternion rotation, Vector3 scale, Vector3 direction,
            double started, double revealed, NetworkIdentity shooter, bool authority)
        {
            var arrow = Instantiate(bow.arrow.gameObject, position, rotation);
            arrow.name = "Magic Arrow · " + id;
            arrow.transform.localScale = scale;
            arrow.SetActive(true);
            var projectile = arrow.AddComponent<MagicBowProjectile>();
            projectile.Initialize(this, id, direction, started, revealed, shooter, authority);
            projectiles[id] = projectile;
            suppressNockUntil = Clock + 0.2d;
        }
        [ClientRpc]
        private void RpcShot(uint id, Vector3 position, Quaternion rotation, Vector3 scale, Vector3 direction,
            double started, double revealed)
        { if (!isServer) SpawnProjectile(id, position, rotation, scale, direction, started, revealed, null, false); }

        public void ProjectileHit(uint id, Vector3 point, Vector3 normal)
        {
            ShowImpact(id, point, normal);
            if (NetworkServer.active) RpcHit(id, point, normal);
        }
        [ClientRpc]
        private void RpcHit(uint id, Vector3 point, Vector3 normal)
        { if (!isServer) ShowImpact(id, point, normal); }
        private void ShowImpact(uint id, Vector3 point, Vector3 normal)
        {
            ImpactCount++; LastImpactPoint = point;
            MagicBowProjectile projectile;
            if (projectiles.TryGetValue(id, out projectile)) { projectiles.Remove(id); if (projectile != null) Destroy(projectile.gameObject); }
            if (bow.hitEffect == null) return;
            var fx = Instantiate(bow.hitEffect.gameObject, point,
                normal.sqrMagnitude > 0.001f ? Quaternion.LookRotation(normal) : Quaternion.identity);
            fx.transform.localScale = bow.hitEffect.transform.lossyScale;
            fx.SetActive(true);
            var ps = fx.GetComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Play(true);
            impacts.RemoveAll(x => x == null); impacts.Add(fx); Destroy(fx, 5f);
        }
        private void OnDisable()
        {
            if (!cached) return;
            CancelLocalPull();
            if (Authority && Application.isPlaying) ResetServerCharge();
            if (stringHandle != null) stringHandle.gameObject.SetActive(false);
            foreach (var p in projectiles.Values) if (p != null) Destroy(p.gameObject);
            projectiles.Clear();
            foreach (var fx in impacts) if (fx != null) Destroy(fx);
            impacts.Clear();
        }
        private void OnDestroy()
        {
            if (grip != null)
            {
                grip.selectFilters.Remove(this);
                grip.selectEntered.RemoveListener(OnGrip); grip.selectExited.RemoveListener(OnDrop);
            }
        }
    }
}
