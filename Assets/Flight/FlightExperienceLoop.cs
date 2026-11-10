using System.Collections;
using UnityEngine;

namespace Greatwall.Flight
{
    /// <summary>
    /// ★★★ 龙骑行体验闭环：把「触发 → 起飞 → 飞行 → 黑场」接成
    ///      可循环的完整玩家体验。
    ///
    /// 全流程链路：
    ///   ① Play 启动 → SnapBlack 立即全黑（避免生硬开局）
    ///   ② 淡入 → WaitingForPlayers（龙显现、悬浮在起点）
    ///   ③ 上龙（AutoMountOnStart / 剧情 / 交互触发 Rider.Mount()）
    ///      → ReportSeated → 人齐（单机立即）LaunchNow 起飞
    ///   ④ TakeOff 滑跑拉起 → Orbit 环绕 → Departure 远飞
    ///      云雾前薄后厚 → 末段滚动云墙 → 冲入云中
    ///   ⑤ keyT ≥ endingAt → Ending + FadeOut(1.2s) → 全黑
    ///   ⑥ ♻ 本组件：保持全黑 HoldBlackSeconds 后复位——
    ///        龙回起点（Director.ResetFlight）
    ///        云重撒（Clouds.RespawnClouds，渐浓因子归零）
    ///        玩家下龙回原位（Rider.Unmount，解锁移动）
    ///      → FadeIn 淡入 → 回到 ② 等待下一次体验
    ///
    /// RestartMode：
    ///   AutoReloop    —— 淡入后自动重新上龙（演示/预览循环）
    ///   WaitForTrigger —— 淡入后停在等待态，由剧情/交互再调 Mount()
    ///
    /// ★ 多人说明：复位动作仅服务器/单机执行（ResetFlight 内有权限
    ///   校验）；客户端由 NetPhase 同步看到 WaitingForPlayers，
    ///   各端 FadeIn 由本组件在检测到相位回落后各自执行。
    /// </summary>
    [DisallowMultipleComponent]
    public class FlightExperienceLoop : MonoBehaviour
    {
        private enum LoopState
        {
            /// <summary>体验进行中 / 等待 Ending 全黑</summary>
            Idle,
            /// <summary>全黑保持中</summary>
            HoldingBlack,
            /// <summary>复位动作已完成，稍候淡入</summary>
            Resetting,
            /// <summary>淡入中 / 等待重新触发</summary>
            Ready
        }

        public enum RestartMode
        {
            /// <summary>淡入完成后自动重新上龙（演示循环）</summary>
            AutoReloop,
            /// <summary>淡入后停在等待态，等剧情/交互再调 Mount()</summary>
            WaitForTrigger
        }

        [Header("引用（留空自动查找）")]
        public FlightDirector Director;
        public FlightFadeController Fade;
        public DragonRideController Rider;
        public FlightCloudSpawner Clouds;

        [Header("循环行为")]
        [Tooltip("黑场后的处理方式。AutoReloop=自动重新上龙循环体验；WaitForTrigger=停在等待态由外部触发。")]
        public RestartMode Mode = RestartMode.AutoReloop;

        [Tooltip("全黑后保持多少秒再复位（让玩家意识到段落结束）。")]
        [Range(0.5f, 6f)]
        public float HoldBlackSeconds = 1.6f;

        [Tooltip("复位后淡入时长（秒）。")]
        [Range(0.5f, 5f)]
        public float FadeInDuration = 1.5f;

        [Header("启动淡入")]
        [Tooltip("Play 启动时先全黑再淡入（避免画面瞬间弹出的生硬感）。")]
        public bool FadeInOnStart = true;

        [Tooltip("启动全黑保持时长（秒）。")]
        [Range(0f, 3f)]
        public float StartBlackSeconds = 0.6f;

        [Tooltip("AutoReloop 模式下，淡入后延迟几秒再重新上龙（给玩家缓一拍）。")]
        [Range(0f, 8f)]
        public float ReloopDelay = 2.5f;

        // ---------------- 运行时 ----------------

        private LoopState _state = LoopState.Idle;
        private float _timer;
        private bool _startFadeDone;

        private void Awake()
        {
            if (Director == null) Director = FindFirstObjectByType<FlightDirector>();
            if (Fade == null) Fade = FindFirstObjectByType<FlightFadeController>();
            if (Rider == null) Rider = FindFirstObjectByType<DragonRideController>();
            if (Clouds == null) Clouds = FindFirstObjectByType<FlightCloudSpawner>();
        }

        private IEnumerator Start()
        {
            if (!FadeInOnStart || Fade == null)
            {
                _startFadeDone = true;
                yield break;
            }

            // ① 启动：先全黑（遮住场景初始化/XR 加载），再淡入
            Fade.SnapBlack();
            yield return new WaitForSeconds(StartBlackSeconds);
            Fade.FadeIn(FadeInDuration);
            _startFadeDone = true;
            Debug.Log("[ExperienceLoop] ▶ 启动淡入完成，等待玩家上龙。", this);
        }

        private void Update()
        {
            if (Director == null || Fade == null || !_startFadeDone)
            {
                return;
            }

            switch (_state)
            {
                case LoopState.Idle:
                    // ⑤→⑥：检测「Ending + 已全黑」→ 进入保持阶段
                    if (Director.CurrentPhase == FlightPhase.Ending && Fade.IsBlack)
                    {
                        _state = LoopState.HoldingBlack;
                        _timer = 0f;
                        Debug.Log("[ExperienceLoop] ⬛ 全黑达成，进入保持阶段。", this);
                    }
                    break;

                case LoopState.HoldingBlack:
                    _timer += Time.deltaTime;
                    if (_timer >= HoldBlackSeconds)
                    {
                        DoReset();
                        _state = LoopState.Resetting;
                        _timer = 0f;
                    }
                    break;

                case LoopState.Resetting:
                    // 复位动作稳定一拍后再淡入（避免同帧闪烁）
                    _timer += Time.deltaTime;
                    if (_timer >= 0.4f)
                    {
                        Fade.FadeIn(FadeInDuration);
                        _state = LoopState.Ready;
                        _timer = 0f;
                        Debug.Log("[ExperienceLoop] ♻ 复位完成，淡入中。", this);
                    }
                    break;

                case LoopState.Ready:
                    _timer += Time.deltaTime;
                    if (_timer >= FadeInDuration + ReloopDelay)
                    {
                        if (Mode == RestartMode.AutoReloop)
                        {
                            // 自动重新上龙 → ReportSeated → 起飞 → 新一轮
                            if (Rider != null && !Rider.IsMounted
                                && Director.CurrentPhase == FlightPhase.WaitingForPlayers)
                            {
                                Rider.Mount();
                                Debug.Log("[ExperienceLoop] 🔁 自动重新上龙，开始新一轮体验。", this);
                            }
                        }
                        _state = LoopState.Idle;
                    }
                    break;
            }
        }

        /// <summary>⑥ 复位：黑幕之下把整条链拉回起点（玩家无感）。</summary>
        private void DoReset()
        {
            if (Rider != null && Rider.IsMounted)
            {
                Rider.Unmount();   // 玩家回上龙前位置、解锁移动
            }
            if (Director != null)
            {
                Director.ResetFlight();   // 龙回起点、相位回等待、计数清零
            }
            if (Clouds != null)
            {
                Clouds.RespawnClouds();   // 云重撒、渐浓因子归零
            }
        }

        /// <summary>调试：立即触发一轮复位（跳过黑场检测）。</summary>
        [ContextMenu("调试：立即复位循环")]
        public void DebugResetNow()
        {
            DoReset();
            Fade?.FadeIn(FadeInDuration);
            _state = LoopState.Ready;
            _timer = 0f;
        }
    }
}
