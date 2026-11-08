using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Greatwall.CannonGame
{
    [DefaultExecutionOrder(100)]
    public class CannonGame : MonoBehaviour
    {
        private const int AmmoCount = 4;
        private const int InteractableLayer = 7;
        private const float ShotSpeed = 28f;
        private const float ShotElevationDegrees = 30f;
        private const float SlotRadius = 0.3f;

        private static readonly Vector3[] AmmoHomes =
        {
            new Vector3(-10.90f, 5.61f, -1.41f),
            new Vector3(-10.90f, 5.61f, -1.09f),
            new Vector3(-10.90f, 5.61f, -0.77f),
            new Vector3(-10.90f, 5.61f, -0.45f),
        };

        private Transform _ballSource;
        private Transform _chain;
        private Transform _barrel;
        private Vector3 _barrelHomeLocalPosition;

        private Mesh _ballMesh;
        private Material[] _ballMaterials;
        private Bounds _ballMeshBounds;
        private Vector3 _ballVisualScale;

        private Vector3 _slotPosition;
        private Quaternion _slotRotation;
        private Vector3 _muzzlePosition;
        private Vector3 _fireDirection;

        private readonly List<CannonAmmo> _ammo = new List<CannonAmmo>();
        private CannonAmmo _loaded;
        private CannonChain _chainPull;
        private CannonVfx _vfx;
        private int _shotCount;
        private bool _ready;

        public CannonAmmo LoadedAmmo => _loaded;
        public IReadOnlyList<CannonAmmo> Ammo => _ammo;
        public int ShotCount => _shotCount;
        public bool IsReady => _ready;
        public Vector3 MuzzlePosition => _muzzlePosition;
        public Vector3 FireDirection => _fireDirection;
        public Vector3 SlotPosition => _slotPosition;

        private void Awake()
        {
            if (!ResolveParts())
            {
                Debug.LogError("[CannonGame] JiPao 子节点不完整，小游戏未启用。", this);
                enabled = false;
                return;
            }

            var ballRenderer = _ballSource.GetComponent<Renderer>();
            var ballFilter = _ballSource.GetComponent<MeshFilter>();
            _ballMesh = ballFilter.sharedMesh;
            _ballMaterials = ballRenderer.sharedMaterials;
            _ballMeshBounds = _ballMesh.bounds;
            _ballVisualScale = _ballSource.lossyScale;
            _slotPosition = ballRenderer.bounds.center;
            _slotRotation = _ballSource.rotation;
            _ballSource.gameObject.SetActive(false);

            if (!ComputeMuzzle(out _muzzlePosition, out _fireDirection))
            {
                Debug.LogError("[CannonGame] 无法从炮管网格推算炮口，小游戏未启用。", this);
                enabled = false;
                return;
            }

            _barrelHomeLocalPosition = _barrel.localPosition;

            CreateAmmo();
            CreateChainGrab();
            CreateVfx();
            CreateGuidance();
            _ready = true;

            Debug.Log($"[CannonGame] ready muzzle={_muzzlePosition} dir={_fireDirection} slot={_slotPosition} ammo={_ammo.Count}");
        }

        private bool ResolveParts()
        {
            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                if (child.parent != transform) continue;
                if (child.name == "Cannon2") _ballSource = child;
                else if (child.name == "polySurface1") _chain = child;
                else if (child.name.StartsWith("polySurface18")) _barrel = child;
            }

            return _ballSource != null && _chain != null && _barrel != null;
        }

        private bool ComputeMuzzle(out Vector3 muzzle, out Vector3 direction)
        {
            muzzle = Vector3.zero;
            direction = Vector3.left;

            var filter = _barrel.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return false;

            // 打包后网格默认不可读（isReadable=false），拿不到顶点：
            // 用网格包围盒的长轴两端近似炮管两端，取离弹槽更远的一端当炮口。
            var bounds = filter.sharedMesh.bounds;
            var extents = bounds.extents;
            int axis = 0;
            if (extents.y > extents[axis]) axis = 1;
            if (extents.z > extents[axis]) axis = 2;

            var offset = Vector3.zero;
            offset[axis] = extents[axis];
            var matrix = _barrel.localToWorldMatrix;
            var endA = matrix.MultiplyPoint3x4(bounds.center + offset);
            var endB = matrix.MultiplyPoint3x4(bounds.center - offset);

            bool aIsMuzzle = (endA - _slotPosition).sqrMagnitude >= (endB - _slotPosition).sqrMagnitude;
            muzzle = aIsMuzzle ? endA : endB;
            var breech = aIsMuzzle ? endB : endA;

            direction = muzzle - breech;
            if (direction.sqrMagnitude < 0.0001f) return false;
            direction.Normalize();
            return true;
        }

        private void CreateAmmo()
        {
            if (TryAdoptPreplacedAmmo()) return;

            var root = new GameObject("Ammo").transform;
            root.SetParent(transform, false);

            for (int i = 0; i < AmmoCount; i++)
            {
                var go = new GameObject($"CannonAmmo_{i + 1}");
                go.transform.SetParent(root, true);
                var rotation = _slotRotation * Quaternion.Euler(0f, i * 32f, 0f);
                go.transform.SetPositionAndRotation(AmmoHomes[i], rotation);

                var visual = new GameObject("Visual");
                visual.transform.SetParent(go.transform, false);
                visual.transform.localPosition = -Vector3.Scale(_ballMeshBounds.center, _ballVisualScale);
                visual.transform.localScale = _ballVisualScale;
                var filter = visual.AddComponent<MeshFilter>();
                filter.sharedMesh = _ballMesh;
                var renderer = visual.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = _ballMaterials;

                SetupAmmo(go.transform, visual);
            }
        }

        private bool TryAdoptPreplacedAmmo()
        {
            var group = transform.Find("CannonAmmo");
            if (group == null) return false;

            var wrappers = new List<Transform>(AmmoCount);
            for (int i = 0; i < AmmoCount; i++)
            {
                var wrapper = group.Find($"CannonAmmo_{i + 1}");
                if (wrapper == null) break;
                wrappers.Add(wrapper);
            }

            if (wrappers.Count != AmmoCount)
            {
                Debug.LogWarning($"[CannonGame] 场景预置炮弹数量不足（{wrappers.Count}/{AmmoCount}），改为运行时生成。", this);
                return false;
            }

            for (int i = 0; i < AmmoCount; i++)
            {
                var visual = wrappers[i].Find("Visual");
                if (visual == null || visual.GetComponent<MeshFilter>() == null || visual.GetComponent<Renderer>() == null)
                {
                    Debug.LogWarning($"[CannonGame] 场景预置炮弹 {wrappers[i].name} 缺少可视网格，改为运行时生成。", this);
                    return false;
                }
            }

            for (int i = 0; i < AmmoCount; i++)
            {
                var visual = wrappers[i].Find("Visual");
                visual.localPosition = -Vector3.Scale(_ballMeshBounds.center, _ballVisualScale);
                visual.localScale = _ballVisualScale;
                SetupAmmo(wrappers[i], visual.gameObject);
            }

            Debug.Log($"[CannonGame] adopted {wrappers.Count} preplaced ammo");
            return true;
        }

        private void SetupAmmo(Transform wrapper, GameObject visual)
        {
            wrapper.gameObject.layer = InteractableLayer;

            var collider = wrapper.GetComponent<BoxCollider>();
            if (collider == null) collider = wrapper.gameObject.AddComponent<BoxCollider>();
            collider.size = Vector3.Scale(_ballMeshBounds.size, _ballVisualScale);

            var body = wrapper.GetComponent<Rigidbody>();
            if (body == null) body = wrapper.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var grab = wrapper.GetComponent<XRGrabInteractable>();
            if (grab == null) grab = wrapper.gameObject.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.throwOnDetach = false;
            grab.attachEaseInTime = 0.08f;

            var ammo = wrapper.GetComponent<CannonAmmo>();
            if (ammo == null) ammo = wrapper.gameObject.AddComponent<CannonAmmo>();
            ammo.Initialize(grab, collider, body, visual, wrapper.position, wrapper.rotation);
            _ammo.Add(ammo);
        }

        private void CreateChainGrab()
        {
            var chainBounds = _chain.GetComponent<Renderer>().bounds;
            var zone = new GameObject("ChainGrab");
            zone.layer = InteractableLayer;
            zone.transform.SetParent(transform, true);
            zone.transform.SetPositionAndRotation(chainBounds.center, transform.rotation);

            var collider = zone.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.3f, chainBounds.size.y + 0.12f, 0.3f);
            var body = zone.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var grab = zone.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.throwOnDetach = false;
            grab.attachEaseInTime = 0f;

            _chainPull = zone.AddComponent<CannonChain>();
            _chainPull.Initialize(this, _chain, grab);
        }

        private void CreateVfx()
        {
            var go = new GameObject("CannonVfx");
            go.transform.SetParent(transform, true);
            _vfx = go.AddComponent<CannonVfx>();
            _vfx.Initialize(_muzzlePosition, _fireDirection, _ballMesh, _ballMaterials, _ballMeshBounds, _ballVisualScale, _slotPosition, _slotRotation);
        }

        private void CreateGuidance()
        {
            var font = Resources.Load<Font>("NotoSansSC-Guide");
            if (font == null)
                Debug.LogWarning("[CannonGame] 缺少悬浮提示字体 Resources/NotoSansSC-Guide.ttf", this);

            var go = new GameObject("CannonGuidance");
            go.transform.SetParent(transform, true);
            go.AddComponent<CannonGuidance>().Initialize(this, _ammo, _chain, font);
        }

        private void Update()
        {
            if (!_ready || _loaded != null) return;

            for (int i = 0; i < _ammo.Count; i++)
            {
                var ammo = _ammo[i];
                if (ammo == null || !ammo.IsHeld) continue;
                if ((ammo.transform.position - _slotPosition).sqrMagnitude > SlotRadius * SlotRadius) continue;
                InsertAmmo(ammo);
                return;
            }
        }

        private void InsertAmmo(CannonAmmo ammo)
        {
            _loaded = ammo;
            ammo.InsertIntoSlot(_slotPosition, _slotRotation);
            _vfx.SetGhostVisible(false);
            Debug.Log("[CannonGame] ammo inserted; ready to fire");
        }

        public void TryFire()
        {
            if (!_ready || _loaded == null) return;

            var ammo = _loaded;
            _loaded = null;
            ammo.Consume();
            _shotCount++;
            StartCoroutine(FireSequence());
        }

        private IEnumerator FireSequence()
        {
            float elevation = ShotElevationDegrees * Mathf.Deg2Rad;
            var flat = new Vector3(_fireDirection.x, 0f, _fireDirection.z);
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
            flat.Normalize();
            var velocity = (flat * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation)) * ShotSpeed;
            float apexTime = velocity.y / 9.81f;
            var apex = _muzzlePosition + velocity * apexTime + Vector3.down * (4.905f * apexTime * apexTime);

            _vfx.PlayMuzzleFlash();
            _vfx.LaunchProjectile(_muzzlePosition + _fireDirection * 0.12f, velocity, apexTime);
            StartCoroutine(Recoil());

            yield return new WaitForSeconds(0.35f);
            _vfx.SetGhostVisible(true);
            yield return new WaitForSeconds(Mathf.Max(0f, apexTime - 0.35f));
            _vfx.PlayFirework(apex);
            Debug.Log($"[CannonGame] shot={_shotCount} apex={apex}");
        }

        private IEnumerator Recoil()
        {
            var localKick = _barrel.parent.InverseTransformVector(-_fireDirection * 0.12f);

            float t = 0f;
            while (t < 0.09f)
            {
                t += Time.deltaTime;
                _barrel.localPosition = _barrelHomeLocalPosition + localKick * Mathf.SmoothStep(0f, 1f, t / 0.09f);
                yield return null;
            }

            t = 0f;
            while (t < 0.55f)
            {
                t += Time.deltaTime;
                _barrel.localPosition = _barrelHomeLocalPosition + localKick * (1f - Mathf.SmoothStep(0f, 1f, t / 0.55f));
                yield return null;
            }

            _barrel.localPosition = _barrelHomeLocalPosition;
        }
    }
}
