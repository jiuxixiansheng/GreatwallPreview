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
    ///     亮起蓝色传送区 → 玩家走进去 → Rider.Mount() → 骑龙起飞。
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
        [Tooltip("骑龙飞行的龙根（part3/骑龙飞上天空）。它被 Timeline 激活后，传送区才会出现。")]
        public Transform DragonRide;

        [Tooltip("蓝色传送区的视觉对象。")]
        public GameObject PadVisual;

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

        [Tooltip("亮起时的淡入时长（缩放 + 由暗到亮）。")]
        [Range(0f, 3f)] public float AppearDuration = 0.6f;

        [Header("触发")]
        [Tooltip("玩家进入该水平半径即触发上龙（米）。")]
        [Range(0.3f, 3f)] public float TriggerRadius = 0.7f;

        [Tooltip("玩家与传送区的高度差超过这个值不触发（防止楼上楼下误触）。")]
        [Range(0.3f, 6f)] public float MaxHeightDifference = 1.8f;

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
            if (Rider == null) Rider = FindObjectOfType<DragonRideController>(true);
            if (PadVisual != null)
            {
                _padScale = PadVisual.transform.localScale;
                PadVisual.SetActive(false);
            }
        }

        private void Update()
        {
            if (_boarded) return;

            if (!_padShown)
            {
                TryShowPad();
                return;
            }

            UpdateAppearAnimation();

            // 玩家走进去 → 上龙
            Transform player = PlayerTransform;
            if (player == null) return;

            Vector3 padPos = PadVisual.transform.position;
            Vector3 a = new Vector3(player.position.x, 0f, player.position.z);
            Vector3 b = new Vector3(padPos.x, 0f, padPos.z);
            bool inRange = Vector3.Distance(a, b) <= TriggerRadius;
            bool heightOk = Mathf.Abs(player.position.y - padPos.y) <= MaxHeightDifference;

            if (inRange && heightOk) Board();
        }

        private Transform PlayerTransform
        {
            get
            {
                if (Rider != null) return Rider.transform;
                if (Camera.main != null) return Camera.main.transform;
                return null;
            }
        }

        private void TryShowPad()
        {
            if (PadVisual == null) return;
            if (DragonRide == null)
            {
                // 没指定龙根时，直接按「本物体已激活」处理
                _dragonSeenAt = Time.time;
            }
            else if (DragonRide.gameObject.activeInHierarchy)
            {
                if (_dragonSeenAt < 0f)
                {
                    _dragonSeenAt = Time.time;
                    // 龙一出现就把骑行段落的云雾点亮（云只在骑行段落存在）
                    if (FlightCloudsRoot != null && !FlightCloudsRoot.activeSelf)
                        FlightCloudsRoot.SetActive(true);
                }
            }
            else
            {
                _dragonSeenAt = -1f;
                return;
            }

            if (_dragonSeenAt < 0f) return;
            if (Time.time - _dragonSeenAt < ShowDelay) return;

            Transform player = PlayerTransform;
            Vector3 pos;
            if (PlaceInFrontOfPlayer && player != null)
            {
                Vector3 forward = player.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                forward.Normalize();
                pos = player.position + forward * PlaceDistance;
                pos.y = player.position.y;
                PadVisual.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
            else if (FixedAnchor != null)
            {
                pos = FixedAnchor.position;
                PadVisual.transform.rotation = FixedAnchor.rotation;
            }
            else
            {
                pos = transform.position;
            }

            PadVisual.transform.position = pos;
            PadVisual.SetActive(true);
            PadVisual.transform.localScale = Vector3.zero;
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

            // 由暗到亮
            var renderer = PadVisual.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                var mat = renderer.material;
                Color baseColor = new Color(0.18f, 0.78f, 1f, 0.55f);
                Color emit = new Color(0.1f, 0.55f, 1f) * (0.4f + 1.6f * eased);
                if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * eased));
                if (mat.HasProperty(EmissionColorId)) mat.SetColor(EmissionColorId, emit);
            }

            if (t >= 1f)
            {
                _appearTimer = -1f;
                PadVisual.transform.localScale = _padScale;
            }
        }

        private void Board()
        {
            _boarded = true;
            if (PadVisual != null) PadVisual.SetActive(false);

            if (Rider == null) Rider = FindObjectOfType<DragonRideController>(true);
            if (Rider != null)
            {
                Rider.Mount();
                Debug.Log("[BoardingGate] 玩家走上传送区 → 上龙。", this);
            }
            else
            {
                Debug.LogWarning("[BoardingGate] 场景里没有 DragonRideController，无法上龙。", this);
            }

            onBoarded?.Invoke();
        }

        private void OnDrawGizmosSelected()
        {
            if (PadVisual == null) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(PadVisual.transform.position, TriggerRadius);
        }
    }
}
