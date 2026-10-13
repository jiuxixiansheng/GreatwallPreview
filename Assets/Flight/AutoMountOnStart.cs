using UnityEngine;
using Greatwall.Flight;

namespace Greatwall.Flight
{
    /// <summary>
    /// 【仅用于预览测试】进入 Play 后自动调用上龙。
    ///
    /// ★ 正式流程请删除本脚本，改由剧情/交互触发：
    ///     GetComponent<DragonRideController>().Mount();
    ///
    /// 用法：
    ///   1. 挂到 sza_XR Origin (XR Rig) 上
    ///   2. 把 DragonRideController 拖进 Rider 字段
    ///   3. Play，约 Delay 秒后自动上龙并起飞
    /// </summary>
    [DisallowMultipleComponent]
    public class AutoMountOnStart : MonoBehaviour
    {
        [Tooltip("要上龙的骑乘控制器（通常就在本物体上）。")]
        public DragonRideController Rider;

        [Tooltip("延迟多少秒后上龙。给 1~2 秒让场景先稳定。")]
        [Range(0f, 10f)]
        public float Delay = 2f;

        [Tooltip("上龙后是否自动强制起飞。\n" +
                 "勾上后不用手动点 FlightDirector 的按钮。")]
        public bool AutoLaunch = true;

        [Tooltip("勾上则启动时禁用自动上龙，改用键盘 F9 触发（方便反复调试）。")]
        public bool WaitForKeyInstead = false;

        [Tooltip("上龙前先派发「大龙特效转场」让龙显现。\n" +
                 "★ BigDragonBehavior.Awake() 会把龙材质 _distance 设为 1.01（完全隐藏），\n" +
                 "  只有派发这个事件才播放显现动画。预览时必须打开。")]
        public bool RevealDragonBeforeMount = true;

        [Tooltip("显现动画时长（秒），传给「大龙特效转场」事件。")]
        [Range(0.1f, 5f)]
        public float RevealDuration = 1f;

        [Tooltip("★ 强制激活飞行龙与其父级 part3。\n" +
                 "实测：Play 时 part3 激活但 龙环绕飞行6 自身为 inactive，\n" +
                 "Prefab 激活 override 正确但运行时未生效，所以这里强制打开。")]
        public bool ForceActivateDragon = true;

        [Tooltip("★ 直接把龙材质 _distance 设为 0 让它完全显现。\n" +
                 "BigDragonBehavior.Awake() 会设 1.01（完全隐藏），\n" +
                 "而它的显现协程依赖动画参数 'start'，我新建的 Controller 没有该参数，\n" +
                 "所以走那条路不会生效。这里直接改材质，最可靠。")]
        public bool ForceRevealMaterial = true;

        [Tooltip("显现时把 _distance 设成的值。0 = 完全显现，1.01 = 完全隐藏。")]
        [Range(0f, 1.01f)]
        public float MaterialDistance = 0f;

        private FlightDirector _director;

        private void Start()
        {
            if (Rider == null)
            {
                Rider = GetComponent<DragonRideController>();
            }

            if (Rider != null && Rider.Director != null)
            {
                _director = Rider.Director;
            }

            if (WaitForKeyInstead)
            {
                Debug.Log("[AutoMount] 已启用手动模式，按 F9 上龙（Inspector 里改 WaitForKey 切换）。", this);
                return;
            }

            Invoke(nameof(DoMount), Delay);
        }

        private void Update()
        {
            // 每帧确保龙是激活的（实测 Play 时它会处于 inactive 状态）
            if (ForceActivateDragon)
            {
                EnsureDragonActive();
            }

            if (WaitForKeyInstead && Input.GetKeyDown(KeyCode.F9))
            {
                DoMount();
            }
        }


        private void EnsureDragonActive()
        {
            if (_director == null) return;

            var dragon = _director.gameObject;
            if (!dragon.activeSelf)
            {
                dragon.SetActive(true);
            }

            // 父级链也检查一遍
            var p = dragon.transform.parent;
            while (p != null)
            {
                if (!p.gameObject.activeSelf)
                {
                    p.gameObject.SetActive(true);
                }
                p = p.parent;
            }

            // ★ 材质显现：BigDragonBehavior.Awake() 把 _distance 设成 1.01（隐藏），
            //   它的显现协程依赖 Animator 参数 'start'，我新建的 Controller 没这个参数，
            //   所以那条路不会生效。这里每帧强制刷一次材质，确保龙可见。
            if (ForceRevealMaterial)
            {
                ForceRevealDragonMaterial();
            }
        }

        private float _lastDistance = -1f;

        private void ForceRevealDragonMaterial()
        {
            // 只在值变化时写，避免每帧 SetFloat
            if (Mathf.Approximately(_lastDistance, MaterialDistance)) return;
            _lastDistance = MaterialDistance;

            var dragon = _director.gameObject;
            var renderers = dragon.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].HasProperty("_distance"))
                    {
                        mats[i].SetFloat("_distance", MaterialDistance);
                    }
                }
            }
        }

        private void DoMount()
        {
            if (Rider == null)
            {
                Debug.LogWarning("[AutoMount] 找不到 DragonRideController。", this);
                return;
            }

            if (Rider.SeatAnchor == null)
            {
                Debug.LogWarning("[AutoMount] DragonRideController 的 Seat Anchor 是空的，" +
                                 "请把龙身上的 DragonSeat 拖进去。", this);
                return;
            }

            // ★ BigDragonBehavior 默认把龙隐藏（_distance = 1.01），
            //   必须派发「大龙特效转场」事件才能显现。
            if (RevealDragonBeforeMount)
            {
                var dispatcher = FindFirstObjectByType<EventDispatcher>();
                if (dispatcher != null)
                {
                    dispatcher.Dispatch("大龙特效转场", RevealDuration);
                    Debug.Log("[AutoMount] 已派发「大龙特效转场」，龙将在 " +
                              RevealDuration + " 秒内显现。", this);
                }
                else
                {
                    Debug.LogWarning("[AutoMount] 场景里没有 EventDispatcher，" +
                                     "龙可能保持隐藏状态。", this);
                }
            }

            Debug.Log("[AutoMount] 开始上龙。", this);
            Rider.Mount();
        }

        private void LateUpdate()
        {
            // 上龙完成的那一帧，通知飞行总控可以起飞了
            if (!AutoLaunch || _director == null || !Rider.IsMounted)
            {
                return;
            }

            if (_director.CurrentPhase == FlightPhase.WaitingForPlayers
                && _director.NetSeatedCount == 0)
            {
                // 兜底：DragonRideController 里正常会调 ReportSeated，
                // 这里只处理没调到的情况（单机预览时 NetworkManager 未启动等）。
                // ★ 不要直接 ForceLaunch —— 那会绕过"人齐才起飞"的计数。
                //   ReportSeated 走统一计数路径：单机/人齐时自然起飞，
                //   多人时正确等待其他人。
                _director.ReportSeated();
            }
        }
    }
}
