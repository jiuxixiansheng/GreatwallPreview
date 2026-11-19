using Mirror;
using UnityEngine;
using UnityEngine.Events;

namespace Greatwall.Flight
{
    /// <summary>
    /// 上龙传送门：骑龙段落（part3）的「走上龙」入口。
    ///
    /// 流程位置：
    ///   chapter2_2 的 timeline 播完 → 自动跳 chapter3 → timeline_03 激活
    ///   part3/骑龙飞上天空（播放 CircleToHover：盘旋 → 笔直悬浮）
    ///   → 本组件检测到龙已出现，延迟 ShowDelay 秒后在玩家前方地面
    ///     亮起蓝色传送区 → 任意玩家走进去 → 全体登龙 → 起飞。
    ///
    /// 设计要点：
    ///   - 不依赖章节系统，也不改 AutoMountOnStart，
    ///     章节只负责把龙激活，本组件只负责「让人走上去」。
    ///   - 传送区默认动态放在玩家前方（VR 里玩家站位不固定，
    ///     固定坐标可能让人走不到），也可以关掉自动放置、用 FixedAnchor 手动摆。
    ///   - 触发用水平距离判定，不依赖物理层/触发器，CharacterController 也能用。
    /// </summary>
    [DisallowMultipleComponent]
    public class DragonBoardingGate : MonoBehaviour
    {
        [Header("引用（留空自动查找）")]
        [Tooltip("骑龙飞行的龙根（part3/骑龙飞上天空）。玩家上龙的瞬间才被激活（开场由独立的盘旋龙演出）。")]
        public Transform DragonRide;

        [Tooltip("开场盘旋龙（独立的 龙环绕飞行3 模型，自带原始盘旋动画）。它出现后开始计时；玩家上龙时隐藏。\n留空则看下面的 UseRideDragonAsTrigger。")]
        public GameObject OpeningDragon;

        [Tooltip("没有开场龙时，是否退化成「骑龙模型被激活」才开始计时。")]
        public bool UseRideDragonAsTrigger = false;

        [Tooltip("蓝色传送区的视觉对象。")]
        public GameObject PadVisual;

        [Tooltip("传送区上方的浮空文字（WorldSpace Canvas）。留空则不处理；有的话跟着传送区一起显隐。")]
        public GameObject PadLabel;

        [Tooltip("骑龙段落的云雾根（part3/FlightClouds）。龙出现时一并激活，让云雾只在骑行段落存在。留空则不处理。")]
        public GameObject FlightCloudsRoot;

        [Tooltip("玩家身上的骑乘控制器（挂 sza_XR Origin）。留空自动查找。")]
        public DragonRideController Rider;

        [Header("出现时机")]
        [Tooltip("龙出现后，延迟多少秒亮起传送区。")]
        [Range(0f, 20f)] public float ShowDelay = 1.5f;

        [Tooltip("传送区自动放在玩家前方。关闭后用 FixedAnchor 的位置。")]
        public bool PlaceInFrontOfPlayer = true;

        [Tooltip("自动放置时，放在玩家前方多远（米）。")]
        [Range(0.8f, 8f)] public float PlaceDistance = 2.2f;

        [Tooltip("关闭自动放置时，传送区使用的固定位置。")]
        public Transform FixedAnchor;

        [Header("大空间上龙起点")]
        [InspectorName("使用指定触发位置")]
        [Tooltip("启用后，将传送区放在下面输入的世界坐标；优先于玩家前方放置和 Fixed Anchor。")]
        public bool UseBoardingWorldPosition;

        [InspectorName("触发位置（世界坐标）")]
        [Tooltip("上龙触发区域的中心坐标，Y 填触发区域所在的地面高度，不能填头显高度。\n" +
                 "玩家上龙时会自动补偿头显实际站位与 XR Origin 原点之间的偏移。")]
        public Vector3 BoardingWorldPosition;

        [Tooltip("亮起时的淡入时长（缩放 + 由暗到亮）。")]
        [Range(0f, 3f)] public float AppearDuration = 0.6f;

        [Header("触发")]
        [Tooltip("任意玩家进入该水平半径即触发全体上龙（米）。")]
        [Range(0.3f, 3f)] public float TriggerRadius = 0.7f;

        [Tooltip("玩家与传送区的高度差超过这个值不触发（防止楼上楼下误触）。")]
        [Range(0.3f, 6f)] public float MaxHeightDifference = 1.8f;

        [Header("上龙瞬间")]
        [Tooltip("上龙时把大龙材质从「完全透明」渐显出来（BigDragonBehavior 在 Awake 里把 _distance 设成 1.01）。\n" +
                 "关掉的话骑龙全程看不见龙，玩家会像被瞬移到空中。")]
        public bool RevealDragonOnBoard = true;

        [Tooltip("渐显时长（秒）。0 = 立即显示。")]
        [Range(0f, 3f)] public float RevealDuration = 0.45f;

        [Tooltip("上龙时把骑龙整体挪到「开场龙龙头所在的位置」，避免上龙瞬间跨度几十米。")]
        public bool AlignRideDragonToOpening = true;

        [Header("事件")]
        public UnityEvent onBoardingShown;
        public UnityEvent onBoarded;

        // ---------------- 运行时 ----------------
        private bool _padShown;
        private bool _boarded;
        private float _dragonSeenAt = -1f;
        private float _appearTimer = -1f;
        private Vector3 _padScale = Vector3.one;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            if (Rider == null && !NetworkServer.active && !NetworkClient.active)
                Rider = FindFirstObjectByType<DragonRideController>(FindObjectsInactive.Include);
            if (PadVisual != null)
            {
                _padScale = PadVisual.transform.localScale;
                PadVisual.SetActive(false);
            }
            if (PadLabel != null) PadLabel.SetActive(false);
        }

        /// <summary>
        /// 绑定开场盘旋龙并从「现在」重新计时（传送板会先收回去）。
        /// 运行时才生成开场龙的场合（例如测试驱动）用这个入口，避免沿用旧计时状态。
        /// </summary>
        public void BeginWithOpeningDragon(GameObject dragon)
        {
            OpeningDragon = dragon;
            UseRideDragonAsTrigger = false;
            _dragonSeenAt = -1f;
            _padShown = false;
            _boarded = false;
            _appearTimer = -1f;

            if (PadVisual != null)
            {
                PadVisual.transform.localScale = _padScale;
                PadVisual.SetActive(false);
            }
            if (PadLabel != null) PadLabel.SetActive(false);
        }

        private void Update()
        {
            if (_boarded) return;

            bool networked = NetworkGameplayCoordinator.IsManaged && (NetworkServer.active || NetworkClient.active);
            var sync = NetworkGameplayCoordinator.Instance;
            if (networked && sync.DragonBoarding.boardAt > 0d)
            {
                // 快照可能先于本地玩家生成或传送区淡入。等本机骑乘控制器就绪再执行。
                if (NetworkTime.time >= sync.DragonBoarding.boardAt && Rider != null && Rider.SeatAnchor != null)
                    Board(sync.DragonBoarding);
                return;
            }

            if (!_padShown)
            {
                TryShowPad();
                return;
            }

            UpdateAppearAnimation();
            Vector3 padPosition = PadVisual.transform.position;
            if (networked)
            {
                if (NetworkServer.active && AnyPlayerInRange(NetworkServer.connections.Values, padPosition))
                    sync.ScheduleDragonBoarding(CaptureBoarding(padPosition));
                return;
            }

            if (Rider != null && Rider.SeatAnchor != null &&
                IsPlayerInRange((HeadTransform != null ? HeadTransform : Rider.transform).position,
                    Rider.transform.position.y, padPosition))
                Board(CaptureBoarding(padPosition));
        }

        private bool AnyPlayerInRange(System.Collections.Generic.IEnumerable<NetworkConnectionToClient> connections,
            Vector3 padPosition)
        {
            foreach (var connection in connections)
            {
                if (connection == null || !connection.isReady || connection.identity == null) continue;
                var player = connection.identity;
                var setup = player.GetComponent<PlayerNetworkSetup>();
                Camera head = setup != null ? setup.localCamera : null;
                // 远端相机组件是禁用的，但其 Transform 仍通过 Mirror 同步。
                if (head != null && IsPlayerInRange(head.transform.position, player.transform.position.y, padPosition))
                    return true;
            }
            return false;
        }

        private bool IsPlayerInRange(Vector3 headPosition, float groundY, Vector3 padPosition)
        {
            Vector3 offset = headPosition - padPosition;
            offset.y = 0f;
            return offset.sqrMagnitude <= TriggerRadius * TriggerRadius &&
                Mathf.Abs(groundY - padPosition.y) <= MaxHeightDifference;
        }

        /// <summary>玩家脚底（XR Origin 根），只用来取地面高度</summary>
        private Transform PlayerTransform
        {
            get
            {
                if (Rider != null) return Rider.transform;
                if (Camera.main != null) return Camera.main.transform;
                return null;
            }
        }

        /// <summary>玩家的视线（头显），用来决定板放在哪个方向</summary>
        private Transform HeadTransform
        {
            get
            {
                if (Rider != null && Rider.TrackedHead != null) return Rider.TrackedHead;
                var cam = Camera.main;
                if (cam != null && cam.transform.IsChildOf(PlayerTransform != null ? PlayerTransform : cam.transform))
                    return cam.transform;
                return PlayerTransform;
            }
        }

        private void TryShowPad()
        {
            if (PadVisual == null) return;

            // 计时起点：只认「观众实际看到的那条龙」。
            //   ① 指定了 OpeningDragon：它一出现就开始计时；
            //   ② 没指定但勾了 UseRideDragonAsTrigger：退化成「骑龙模型被激活」；
            //   ③ 都没有：不计时（避免早早点亮传送板）。
            if (OpeningDragon != null)
            {
                if (!OpeningDragon.activeInHierarchy)
                {
                    _dragonSeenAt = -1f;
                    return;
                }
                if (_dragonSeenAt < 0f) _dragonSeenAt = Time.time;
            }
            else if (UseRideDragonAsTrigger && DragonRide != null && DragonRide.gameObject.activeInHierarchy)
            {
                if (_dragonSeenAt < 0f) _dragonSeenAt = Time.time;
            }
            else
            {
                _dragonSeenAt = -1f;
                return;
            }

            if (_dragonSeenAt < 0f) return;
            if (Time.time - _dragonSeenAt < ShowDelay) return;

            Transform player = PlayerTransform;

            // 三种摆法：
            //   ① PlaceInFrontOfPlayer = 开：板跟着玩家走（放在行为前方）
            //   ② FixedAnchor 指定锚点：板固定在锚点
            //   ③ 都不设：★ 完全使用场景里摆好的位置，运行时不动它
            bool reposition = PlaceInFrontOfPlayer && player != null;

            if (UseBoardingWorldPosition)
            {
                Vector3 delta = BoardingWorldPosition - PadVisual.transform.position;
                PadVisual.transform.position = BoardingWorldPosition;
                if (PadLabel != null && !PadLabel.transform.IsChildOf(PadVisual.transform))
                    PadLabel.transform.position += delta;
            }
            else if (reposition)
            {
                var head = HeadTransform;
                Vector3 forward = head != null ? head.forward : player.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                forward.Normalize();

                Vector3 origin = player.position;
                Vector3 pos = origin + forward * PlaceDistance;
                pos.y = player.position.y + 0.12f;
                PadVisual.transform.position = pos;
                PadVisual.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
            else if (FixedAnchor != null)
            {
                PadVisual.transform.position = FixedAnchor.position;
                PadVisual.transform.rotation = FixedAnchor.rotation;
            }

            PadVisual.SetActive(true);
            PadVisual.transform.localScale = Vector3.zero;
            if (PadLabel != null) PadLabel.SetActive(true);
            _padShown = true;
            _appearTimer = 0f;
            onBoardingShown?.Invoke();
            Debug.Log("[BoardingGate] 传送区已亮起，等玩家走上去。", this);
        }

        private void UpdateAppearAnimation()
        {
            if (_appearTimer < 0f) return;
            _appearTimer += Time.deltaTime;
            float t = AppearDuration <= 0.01f ? 1f : Mathf.Clamp01(_appearTimer / AppearDuration);
            float eased = t * t * (3f - 2f * t);
            PadVisual.transform.localScale = _padScale * eased;

            // 由暗到亮（只对带 _BaseColor 的材质生效；
            // 复用 part1 的 board.mat 全息边框时没有该属性，就只做缩放淡入）
            var renderer = PadVisual.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                var mat = renderer.sharedMaterial;
                if (mat != null && mat.HasProperty(BaseColorId) && mat.HasProperty(EmissionColorId))
                {
                    Color baseColor = new Color(0.18f, 0.78f, 1f, 0.55f);
                    Color emit = new Color(0.1f, 0.55f, 1f) * (0.4f + 1.6f * eased);
                    mat.SetColor(BaseColorId, new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * eased));
                    mat.SetColor(EmissionColorId, emit);
                }
            }

            if (t >= 1f)
            {
                _appearTimer = -1f;
                PadVisual.transform.localScale = _padScale;
            }
        }

        private GameplayDragonBoardingState CaptureBoarding(Vector3 padPosition)
        {
            var head = AlignRideDragonToOpening && OpeningDragon != null
                ? FindDeep(OpeningDragon.transform, "Head") : null;
            return new GameplayDragonBoardingState
            {
                padPosition = padPosition,
                alignToOpeningHead = head != null,
                openingHeadPosition = head != null ? head.position : Vector3.zero
            };
        }

        private void Board(GameplayDragonBoardingState boarding)
        {
            _boarded = true;

            if (FlightCloudsRoot != null && !FlightCloudsRoot.activeSelf)
                FlightCloudsRoot.SetActive(true);

            if (PadVisual != null) PadVisual.SetActive(false);
            if (PadLabel != null) PadLabel.SetActive(false);

            if (DragonRide != null && !DragonRide.gameObject.activeSelf)
                DragonRide.gameObject.SetActive(true);
            if (OpeningDragon != null && OpeningDragon.activeSelf)
                OpeningDragon.SetActive(false);

            StartCoroutine(MountNextFrame(boarding));
            onBoarded?.Invoke();
        }

        private System.Collections.IEnumerator MountNextFrame(GameplayDragonBoardingState boarding)
        {
            // 让刚激活的骑龙完成初始化，再对齐到服务器记录的开场龙头位置。
            yield return null;
            if (Rider == null || Rider.SeatAnchor == null) yield break;

            if (boarding.alignToOpeningHead && DragonRide != null)
            {
                DragonRide.position += boarding.openingHeadPosition - Rider.SeatAnchor.SeatWorldPosition;
                yield return null;
                if (Rider == null) yield break;
            }

            if (RevealDragonOnBoard) StartCoroutine(RevealDragonCoroutine());

            if (Rider.Director != null)
            {
                var director = Rider.Director;
                Vector3 seat = Rider.SeatAnchor.SeatWorldPosition;
                if (TryGetComponent(out FlightLandscape landscape) && landscape.isActiveAndEnabled)
                    landscape.SetDeparturePose(seat, director.ScenicDepartureHeading);
                if (FlightCloudsRoot != null
                    && FlightCloudsRoot.TryGetComponent(out FlightCloudSpawner cloudSpawner))
                    cloudSpawner.SetScenicRouteOrigin(director, seat);
            }

            // 每位玩家取自己的头显水平位置，统一使用传送区的地面高度。
            Rider.MountFromWorldPosition(boarding.padPosition);
            Debug.Log("[BoardingGate] 本机玩家开始登龙。", this);
        }

        /// <summary>把骑龙材质的不透明度从 1.01（全透明）渐变到 0（实体）。</summary>
        private System.Collections.IEnumerator RevealDragonCoroutine()
        {
            if (DragonRide == null) yield break;
            var renderers = DragonRide.GetComponentsInChildren<Renderer>(true);
            var mats = new System.Collections.Generic.List<Material>();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                foreach (var m in r.materials)          // .materials 拿到的是实例，不影响别的龙
                    if (m != null && m.HasProperty(DistanceId)) mats.Add(m);
            }
            if (mats.Count == 0) yield break;

            float dur = Mathf.Max(0f, RevealDuration);
            if (dur <= 0.01f)
            {
                foreach (var m in mats) m.SetFloat(DistanceId, 0f);
                yield break;
            }

            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float v = Mathf.Lerp(1.01f, 0f, Mathf.Clamp01(t / dur));
                foreach (var m in mats) if (m != null) m.SetFloat(DistanceId, v);
                yield return null;
            }
            foreach (var m in mats) if (m != null) m.SetFloat(DistanceId, 0f);
        }

        private static readonly int DistanceId = Shader.PropertyToID("_distance");

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        private void OnDrawGizmosSelected()
        {
            if (PadVisual == null) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(UseBoardingWorldPosition
                ? BoardingWorldPosition : PadVisual.transform.position, TriggerRadius);
        }
    }
}
