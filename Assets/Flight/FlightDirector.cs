using System;
using Greatwall.Flight;
using Mirror;
using UnityEngine;
using UnityEngine.Events;

namespace Greatwall.Flight
{
    /// <summary>
    /// 飞行阶段。所有人按同一顺序推进，由服务器裁决。
    /// </summary>
    public enum FlightPhase : byte
    {
        /// <summary>等待所有人上龙</summary>
        WaitingForPlayers = 0,

        /// <summary>起飞爬升</summary>
        TakeOff = 1,

        /// <summary>环绕飞行一圈</summary>
        Orbit = 2,

        /// <summary>向远处飞去</summary>
        Departure = 3,

        /// <summary>黑场淡出，游戏结束</summary>
        Ending = 4
    }

    /// <summary>
    /// 飞行总控（服务器权威）。
    ///
    /// 同步设计：
    ///   - 只有服务器推进阶段与进度，客户端只做表现。
    ///     这样保证所有人看到的动画一致，不会因各自帧率不同而错位。
    ///   - 进度与阶段用 [SyncVar] 同步（增量小），不用逐帧 RPC。
    ///   - 客户端对龙的姿态做插值，掩盖网络抖动。
    ///
    /// ★ 接入步骤（重要）：
    ///   1. 挂载对象是 **`part3/龙环绕飞行6`**（不是 Dragon_root）。
    ///      它的层级：
    ///        part3 (天幕球)
    ///        └─ 龙环绕飞行6     ← 本脚本挂这里（已有 Animator + BigDragonBehavior）
    ///           ├─ SKM_Dragon
    ///           └─ SKM_Dragon.001
    ///              └─ Head    ← DragonSeatAnchor 挂这里或骨骼上
    ///
    ///   2. 给它添加：
    ///        - NetworkIdentity
    ///        - NetworkTransform（Sync Direction = Both，Sync Space = World，
    ///          Sync Scale 关闭，Sync Interval = 0 ← 每帧同步，保证动画一致）
    ///        - 本脚本
    ///      这些组件需要你手动挂，本脚本不自动添加。
    ///
    ///   3. 场景里的 NetworkManager 当前是禁用状态（m_IsActive=0），
    ///      需要手动激活，否则联机不工作。
    ///
    ///   4. 座位锚点挂在 SKM_Dragon.001 或其子骨骼上。
    ///
    ///   5. 玩家上龙后调用 ReportSeated()，服务器计数达标自动起飞。
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class FlightDirector : NetworkBehaviour
    {
        [Header("阶段时长（秒）")]
        [Tooltip("起飞爬升时长。设为 0 则跳过起飞阶段。")]
        public float TakeOffDuration = 6f;

        [Tooltip("环绕飞行的总时长。这条龙叫「龙环绕飞行6」，绕一圈通常 12~20 秒舒适。")]
        public float OrbitDuration = 30f;

        [Tooltip("向远处飞走的时长。")]
        public float DepartureDuration = 20f;

        [Header("环绕参数")]
        [Tooltip("环绕半径。\n" +
                 "★ 这条龙 scale=5.0，模型本体就很大。\n" +
                 "从实测数据看 SKM_Dragon 偏移在 (0, 7.2, 24.5)，\n" +
                 "所以半径太小会贴着龙身转，建议 12~20 起调。")]
        [Range(1f, 60f)]
        public float OrbitRadius = 22f;

        [Tooltip("环绕时的垂直起伏幅度。VR 建议 0.15~0.35，过大会不适。")]
        [Range(0f, 2f)]
        public float OrbitBobbing = 0.05f;

        [Tooltip("环绕圈数。1 表示飞一圈。想多绕几圈改这里。")]
        [Range(1, 5)]
        public int OrbitLoops = 1;

        [Tooltip("环绕时的俯仰角（度）。负值表示略微俯冲。")]
        [Range(-30f, 30f)]
        public float OrbitPitch = 0f;

        [Header("飞行")]
        [Tooltip("飞行时龙中心的世界高度 Y。\n" +
                 "★ 实测：本项目三章地面点位在 Y≈5.5~9，\n" +
                 "  part3 天幕球在 (15.5, -12.2, -29.7)，\n" +
                 "  龙环绕飞行6 起点约 Y≈7。\n" +
                 "  建议设 25~40 获得足够凌空感。")]
        public float FlightAltitude = 32f;

        [Tooltip("爬升角（度）。正值向上俯仰，营造起飞推背感。")]
        [Range(0f, 45f)]
        public float TakeOffPitch = 0f;

        [Tooltip("远飞时的前进速度（米/秒）。")]
        public float DepartureSpeed = 5f;

        [Header("客户端插值")]
        [Tooltip("客户端跟随服务器的平滑时间（秒）。\n" +
                 "0.1~0.25 比较稳，太小会抖动，太大会明显滞后。")]
        [Range(0.02f, 1f)]
        public float ClientSmoothTime = 0.15f;

        [Header("视觉中心补偿")]
        [Tooltip("★ 重要：这条龙的身体（SKM_Dragon）在根节点本地坐标有偏移 (0, 7.19, 24.54)，\n" +
                 "  scale=5 时世界偏移约 (0, 36, 123) 米。\n" +
                 "  如果不补偿，环绕时会出现「龙绕着一个看不见的点转圈」。\n" +
                 "  启用后会自动把根节点修正到模型中心。")]
        public bool AutoCompensateVisualCenter = true;

        [Tooltip("模型中心的本地坐标（相对于本脚本所在物体）。\n" +
                 "实测值来自 SKM_Dragon 在 龙环绕飞行6 下的位置。\n" +
                 "若换了模型请在 Inspector 里调整。")]
        public Vector3 ModelCenterLocal = new Vector3(0f, 7.19f, 24.54f);

        [Header("盘旋节奏")]
        [Tooltip("盘旋时的速度变化幅度。0=匀速，1=先慢后快再慢。\n" +
                 "0.35~0.6 比较自然，像真实的盘旋收放。")]
        [Range(0f, 1f)]
        public float OrbitSpeedVariation = 0f;

        [Tooltip("盘旋的进退幅度（米）。龙在盘旋时会向外扩张再收拢，\n" +
                 "这个值让轨迹不是完美圆，而是有呼吸感的椭圆摆动。")]
        [Range(0f, 20f)]
        public float OrbitRadialSwing = 0f;

        [Header("龙背起伏")]
        [Tooltip("龙背上下起伏的幅度（米）。这是让玩家感觉「在龙背上」的关键。\n" +
                 "建议 0.3~0.8。太大 VR 里会晕。")]
        [Range(0f, 2f)]
        public float BodyBobbing = 0.07f;

        [Tooltip("起伏的频率（每秒几次）。1.2~2.0 比较像翅膀扇动的节奏。")]
        [Range(0.1f, 5f)]
        public float BodyBobbingFreq = 0.35f;

        [Tooltip("侧向摆动幅度（米）。龙飞行时身体的横向摆动。")]
        [Range(0f, 3f)]
        public float BodySway = 0f;

        [Tooltip("滚转幅度（度）。转弯时龙身会侧倾，像真的在转弯。")]
        [Range(0f, 45f)]
        public float BodyRoll = 0f;

        [Header("进入云层")]
        [Tooltip("远飞阶段进入云层的时机（0~1）。0=一开始就冲进去，1=最后才进。\n" +
                 "配合 FlightCloudSpawner 的密度渐变。")]
        [Range(0f, 1f)]
        public float CloudEntryPoint = 0.35f;

        [Header("K 帧驱动（推荐）")]
        [Tooltip("★ 烘焙好的关键帧轨迹表。\n" +
                 "由菜单 Window → Flight → Bake Flight Keyframes 生成。\n" +
                 "填上后位置与朝向完全按 K 帧曲线走，轨迹经过逐帧精调。\n" +
                 "留空则回退到代码实时计算（旧的 SmoothDamp 方案）。")]
        public FlightKeyframeTable Keyframes;

        /// <summary>当前 K 帧时间（秒）。云系统沿轨迹撒云时用它取未来位置。</summary>
        public float KeyframeTime => _keyTime;

        [Header("模型朝向修正")]
        [Tooltip("★ 模型机头在根本地空间的 yaw 偏差（度）。\n" +
                 "实测：本模型嘴部（jaw）朝根本地 yaw≈103°，\n" +
                 "而根 forward=0°——机头天生右偏 103°。\n" +
                 "运行时龙根 yaw = 飞行方向 − 本值，使机头始终指向飞行方向。")]
        public float ModelYawOffset = -77f;

        /// <summary>姿态动画状态名 hash（AC_dragon_flight 的 FlyForward）</summary>
        private static readonly int FlyStateHash = Animator.StringToHash("FlyForward");

        /// <summary>当前飞行方向的世界 yaw（度）。= K 帧 yaw，即机头应对准的方向。</summary>
        public float FlightYaw { get; private set; }

        /// <summary>当前飞行方向（世界空间单位向量）。云海、座位视角等以此为准。</summary>
        public Vector3 FlightDirection =>
            Quaternion.Euler(0f, FlightYaw, 0f) * Vector3.forward;

        [Header("速度平滑")]
        [Tooltip("★ 全局速度变化的平滑时间（秒）。\n" +
                 "越大越柔和。阶段切换时的速度突变是晃动的主要来源，\n" +
                 "这个参数专门用来消除它。1.0~2.0 比较自然。")]
        [Range(0.1f, 5f)]
        public float VelocitySmoothTime = 1.5f;

        [Tooltip("盘旋时的角速度（度/秒）。直接控制转圈快慢，\n" +
                 "比用「一圈时长」更直观。8~20 比较平缓。")]
        [Range(2f, 60f)]
        public float OrbitAngularSpeedDeg = 12f;

        [Header("运动平滑度")]
        [Tooltip("★ 转向角速度（度/秒）。越小转向越柔和，VR 里更舒适。\n" +
                 "建议 40~90。太大会有生硬的瞬间转向感。")]
        [Range(10f, 240f)]
        public float RotationSpeedDeg = 18f;

        /// <summary>★ 平滑后的 pitch 状态：K 帧俯仰角经匀速角速度限制后应用——
        ///   升空段 K 帧 pitch 变化剧烈，直通会造成龙身突跳式摆动（用户实测）；
        ///   限速后全程姿态变化速率恒定（匀速观感），巡航段变化慢不受影响。</summary>
        private float _smoothedPitch;

        [Tooltip("起飞时向前滑行的距离（米）。0 = 纯垂直上升。")]
        [Range(0f, 30f)]
        public float TakeOffForwardDistance = 3f;

        [Header("单机模式")]
        [Tooltip("★ 单机预览用：没有 NetworkManager 时也推进飞行。\n" +
                 "联机时不要勾（否则每个客户端都会自己推进，导致不同步）。")]
        public bool StandaloneMode = true;

        [Header("起飞触发")]
        [Tooltip("需要多少人上龙才起飞。0 = 自动（取当前在线玩家数）。\n" +
                 "★ 多人时建议设 0，人齐自动起飞；调试时可手动设 1。")]
        public int RequiredPlayers = 0;

        [Header("黑场")]
        [Tooltip("请求淡出到全黑时触发。\n" +
                 "★ 需要在 Inspector 里绑定一个把 Canvas 淡出到全黑的方法。\n" +
                 "  团队 StoryTimelineManager 的 onFadeOutRequested 目前是空的，\n" +
                 "  所以这个事件要你自己接。\n" +
                 "  淡出动画播完后，请调用 StoryTimelineManager.NotifyFadeOutComplete()。")]
        public UnityEvent onFadeOutRequested;

        [Tooltip("★ 自带黑场兜底：场景里找到 FlightFadeController 就自动用。\n" +
                 "不依赖团队的事件/UI 接线，保证黑场一定发生。\n" +
                 "淡出时长（秒）。0.8~1.5 比较自然。")]
        public float FadeOutDuration = 1.2f;

        // ---------------- 网络同步状态 ----------------
        // 注意：本项目的 Mirror 版本使用 [SyncVar] + Weaver 代码生成，
        // 没有 NetworkVariable<T> 泛型类。写入后会自动生成网络属性。

        /// <summary>当前阶段（服务器写，客户端只读）</summary>
        [SyncVar(hook = nameof(OnPhaseChangedHook))]
        public int NetPhase = (int)FlightPhase.WaitingForPlayers;

        /// <summary>阶段内进度 0~1</summary>
        [SyncVar]
        public float NetProgress = 0f;

        /// <summary>已就座人数（服务器写）</summary>
        [SyncVar]
        public int NetSeatedCount = 0;

        // ---------------- 运行时 ----------------

        /// <summary>
        /// 本地进度缓存。
        /// [SyncVar] 在单机（无网络）时写入不生效，
        /// 所以飞行计算一律用这个本地值，网络同步只用于给客户端用。
        /// </summary>
        private float _orbitAngularSpeed;
        private Vector3 _departureStart;
        private Vector3 _departureDir;
        private float _departTravelled;
        private float _departureElapsed;
        private float _orbitElapsed;

        /// <summary>K 帧驱动的全局时间（秒）</summary>
        private float _keyTime;

        /// <summary>
        /// 阶段时间节点。★ 直接读 K 帧表里烘焙好的值，不做按比例推算 ——
        /// 推算会在阶段时长调整后产生偏差。
        /// </summary>
        private float TakeOffEnd => Keyframes != null
            ? Keyframes.takeOffEnd
            : TakeOffDuration;

        private float OrbitEnd => Keyframes != null
            ? Keyframes.orbitEnd
            : TakeOffDuration + OrbitDuration;

        private float DepartureEnd => Keyframes != null
            ? Keyframes.departureEnd
            : TakeOffDuration + OrbitDuration + DepartureDuration;
        private float _takeOffElapsed;

        /// <summary>平滑后的实际角速度（度/秒），避免阶段切换时突跳</summary>
        private float _smoothedOrbitSpeed;

        /// <summary>SmoothDamp 内部用的速度状态（Unity 要求传 ref）</summary>
        private float _orbitSpeedVel;
        private float _departureSpeedVel;

        /// <summary>平滑后的实际飞行速度（米/秒）</summary>
        private float _smoothedDepartureSpeed;
        private Vector3 _takeOffForward;
        private float _localProgress;

        /// <summary>本地阶段（单机无网络时 SyncVar 不生效，所以另存一份）</summary>
        private FlightPhase _localPhase = FlightPhase.WaitingForPlayers;

        private DragonSeatAnchor _seat;
        private Transform _playerRoot;
        private Vector3 _phaseStartPos;
        private float _phaseTimer;
        private float _orbitAngle;
        private bool _launched;
        private bool _isAuthoritySide;

        /// <summary>阶段变化回调（仅客户端，用于 UI / 音效）</summary>
        public event Action<FlightPhase> PhaseChanged;

        /// <summary>当前阶段（本地读取用）</summary>
        /// <summary>
        /// 当前阶段内的进度 0~1（本地值，单机或服务器都可用）。
        /// 供 FlightCloudSpawner 判断何时进入云层。
        /// </summary>
        public float CurrentProgress => _localProgress;

        public FlightPhase CurrentPhase =>
            (StandaloneMode && !HasNetwork) ? _localPhase : (FlightPhase)NetPhase;

        private void Awake()
        {
            _seat = GetComponentInChildren<DragonSeatAnchor>();
            if (_seat == null)
            {
                Debug.LogError("[Flight] 找不到 DragonSeatAnchor，无法定位座位。", this);
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _isAuthoritySide = true;
            _launched = false;
            _phaseTimer = 0f;
            _localProgress = 0f;
            _departureStart = Vector3.zero;
            _departureElapsed = 0f;
            _orbitElapsed = 0f;
            _takeOffElapsed = 0f;
            _localPhase = FlightPhase.WaitingForPlayers;
            NetSeatedCount = 0;
            NetPhase = (int)FlightPhase.WaitingForPlayers;
            _phaseStartPos = GetVisualCenter();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _isAuthoritySide = isServer;
            _phaseStartPos = GetVisualCenter();
        }

        private void OnPhaseChangedHook(int oldValue, int newValue)
        {
            _phaseTimer = 0f;
            _orbitAngle = 0f;

            var phase = (FlightPhase)newValue;
            PhaseChanged?.Invoke(phase);

            if (phase == FlightPhase.Ending)
            {
                RequestFadeOut();
            }
        }

        // ---------------- 主循环 ----------------

        private void Update()
        {
            // 单机模式：没有网络时也推进飞行（预览用）
            bool authority = isServer || (StandaloneMode && !HasNetwork);

            if (authority)
            {
                ServerTick();
                // ★ K 帧模式下不强制水平——K 帧的 pitch 就是设计的上仰角，
                //   EnforceLevelAttitude 会把它清零（上仰感消失）。
                //   该强制只用于旧公式回退模式（roll 防护）。
                if (Keyframes == null || Keyframes.frames == null
                    || Keyframes.frames.Count <= 1)
                {
                    EnforceLevelAttitude();
                }
            }
            else
            {
                ClientTick();
            }
        }

        /// <summary>
        /// 是否存在网络会话。单机预览时为 false。
        /// </summary>
        private bool HasNetwork
        {
            get
            {
                var nm = Mirror.NetworkManager.singleton;
                return nm != null && nm.isNetworkActive;
            }
        }

        /// <summary>
        /// 服务器：推进阶段与龙的运动。全场唯一的真相来源。
        /// </summary>
        private void ServerTick()
        {
            if (!_launched)
            {
                return;
            }

            // ★ 有 K 帧表时，完全按关键帧曲线走。
            //   表由菜单 Window → Flight → Bake Flight Keyframes 烘焙，
            //   3361 个关键帧 / 60fps / 56 秒，横滚全程为 0。
            //   留空则回退到下面的公式计算（兼容旧方案）。
            if (Keyframes != null && Keyframes.frames != null
                && Keyframes.frames.Count > 1)
            {
                ServerTickKeyframed();
                return;
            }

            switch (CurrentPhase)
            {
                case FlightPhase.WaitingForPlayers:
                    break;

                case FlightPhase.TakeOff:
                    if (AdvanceTimer(TakeOffDuration))
                    {
                        SetPhase(FlightPhase.Orbit);
                    }
                    else
                    {
                        ApplyTakeOff();
                    }
                    break;

                case FlightPhase.Orbit:
                    if (AdvanceTimer(OrbitDuration))
                    {
                        SetPhase(FlightPhase.Departure);
                    }
                    else
                    {
                        ApplyOrbit();
                    }
                    break;

                case FlightPhase.Departure:
                    // ★ 轨迹末端保护：龙动画快放完时立即进 Ending——
                    //   之前 Ending 由阶段计时触发，晚于 29s 轨迹终点，
                    //   动画停止后到全黑之间有一段"龙静止"的空窗。
                    //   提前 FadeOutDuration + 0.2s 开始淡出，
                    //   动画停止瞬间画面已基本全黑。
                    if (Keyframes != null
                        && Keyframes.frames != null
                        && Keyframes.frames.Count > 1
                        && KeyframeTime >= Keyframes.totalTime
                                        - Mathf.Max(0.3f, FadeOutDuration) - 0.2f)
                    {
                        SetPhase(FlightPhase.Ending);
                    }
                    else if (AdvanceTimer(DepartureDuration))
                    {
                        SetPhase(FlightPhase.Ending);
                    }
                    else
                    {
                        ApplyDeparture();
                    }
                    break;

                case FlightPhase.Ending:
                    // 停在黑场，等外部决定后续
                    break;
            }
        }

        /// <summary>
        /// ★ K 帧驱动的服务器推进。
        ///
        ///   与旧方案的区别：
        ///   旧 —— 每帧用公式实时算位置（Sin / SmoothDamp）
        ///   新 —— 按烘焙好的关键帧表采样，轨迹是一条确定的曲线
        ///
        ///   阶段划分仍然保留（用于云海密度、动画切换等逻辑），
        ///   但位置与朝向统一由 K 帧表给出。
        /// </summary>
        private void ServerTickKeyframed()
        {
            // 推进全局时间
            _keyTime += Time.deltaTime;

            // 超出表长就停在末尾（黑场阶段由 Ending 处理）
            if (_keyTime >= Keyframes.totalTime)
            {
                _keyTime = Keyframes.totalTime;
            }

            Vector3 pos;
            float yaw, roll, pitch;
            Keyframes.SampleFull(_keyTime, out pos, out yaw, out pitch, out roll);

            // ★ 关键：K 帧表的坐标是「烘焙时龙根节点的世界位置」。
            //   龙根节点下方有模型偏移（SKM_Dragon 在本地 (0, 7.19, 24.53)），
            //   所以要把模型视觉中心对齐到 K 帧位置。
            SetPositionWithCenterOffset(pos);

            // ★★★ 姿态：yaw + pitch，roll 恒 0。
            //   K 帧的 yaw = 飞行方向；pitch = 抬头角（正=抬头）。
            //   ★ Unity 绕 X 正转 = 低头，应用时取负。
            //   ★ 模型机头天生右偏 ModelYawOffset（实测 jaw 朝根本地 103°），
            //     龙根 yaw = 飞行方向 − ModelYawOffset，
            //     使机头始终指向飞行方向。
            float rootYaw = yaw - ModelYawOffset;
            float newYaw = Quaternion.RotateTowards(
                Quaternion.Euler(0f, transform.eulerAngles.y, 0f),
                Quaternion.Euler(0f, rootYaw, 0f),
                RotationSpeedDeg * Time.deltaTime).eulerAngles.y;
            // ★ pitch 匀速追踪（与 yaw 同速 RotationSpeedDeg）：
            //   摆动速度全程恒定，不再有升空段的突跳。
            _smoothedPitch = Mathf.MoveTowards(_smoothedPitch, pitch,
                RotationSpeedDeg * Time.deltaTime);
            transform.rotation = Quaternion.Euler(-_smoothedPitch, newYaw, 0f);
            FlightYaw = yaw;

            // 阶段推进：只改状态，不动位置
            UpdatePhaseByKeyTime();

            // ★★★ 骨骼动画时间与轨迹时间同步。
            //   龙的姿态动画（dragon_flight.fbx 的 29s 'Scene' clip）
            //   是按本轨迹的时间轴制作的：0-2.5s 滑跑、2.5-13s 拉起、13-29s 巡航。
            //  Animator 自由播放会和 K 帧时间脱节，这里每帧强制对时。
            var rideAnim = GetComponent<Animator>();
            if (rideAnim != null && rideAnim.runtimeAnimatorController != null
                && Keyframes.totalTime > 0.01f)
            {
                float normalized = Mathf.Clamp01(_keyTime / Keyframes.totalTime);
                rideAnim.Play(FlyStateHash, 0, normalized);
                rideAnim.Update(0f);
            }

        }

        /// <summary>
        /// 按 K 帧时间推进阶段。位置完全由 K 帧表负责，
        /// 这里只负责切阶段（云海、动画、黑场等逻辑需要知道当前阶段）。
        /// </summary>
        private void UpdatePhaseByKeyTime()
        {
            float t = _keyTime;

            // ★ 轨迹末端保护（黑场前移）：Ending 必须在龙动画停止前
            //   FadeOutDuration + 0.2s 就触发——否则动画停在最后一帧、
            //   黑场才刚开始淡出，出现一段"龙静止"的空窗（观感差）。
            //   提前淡出后，动画停止瞬间画面已基本全黑。
            float endingAt = DepartureEnd;
            if (Keyframes != null && Keyframes.frames != null
                && Keyframes.frames.Count > 1)
            {
                endingAt = Mathf.Min(DepartureEnd,
                    Keyframes.totalTime
                    - Mathf.Max(0.3f, FadeOutDuration) - 0.2f);
            }

            if (t < TakeOffEnd)
            {
                if (CurrentPhase != FlightPhase.TakeOff)
                    SetPhase(FlightPhase.TakeOff);
            }
            else if (t < OrbitEnd)
            {
                if (CurrentPhase != FlightPhase.Orbit)
                    SetPhase(FlightPhase.Orbit);
            }
            else if (t < endingAt)
            {
                if (CurrentPhase != FlightPhase.Departure)
                    SetPhase(FlightPhase.Departure);
            }
            else
            {
                if (CurrentPhase != FlightPhase.Ending)
                    SetPhase(FlightPhase.Ending);
            }
        }

        /// <summary>★ 强制水平姿态：清除任何残留的俯仰与横滚。
        ///
        ///   数学上三个 Apply* 都已经用「水平方向 + Vector3.up」构造朝向，
        ///   理论上不会有倾斜。但 RotateTowards 是增量旋转，
        ///   长时间运行或帧率异常时可能累积微小误差。
        ///   这个方法每帧把姿态重新压平，作为最后一道保险。
        ///
        ///   横滚（Z 轴）在第一人称里表现为画面左右摇晃，最让人不适，
        ///   所以这里只保留偏航（Y 轴），其余全部归零。
        /// </summary>
        private void EnforceLevelAttitude()
        {
            Vector3 fwd = transform.forward;

            // 取水平分量 → 纯偏航
            Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
            if (flat.sqrMagnitude < 0.0001f)
            {
                // 视线接近垂直（不应该发生），保持原样避免跳变
                return;
            }

            Quaternion level = Quaternion.LookRotation(flat.normalized, Vector3.up);

            // 只在有偏差时才写，避免每帧无谓赋值
            if (Quaternion.Angle(transform.rotation, level) > 0.01f)
            {
                transform.rotation = level;
            }
        }

        /// <summary>
        /// 客户端：向服务器状态插值，掩盖网络抖动。
        /// </summary>
        private void ClientTick()
        {
            if (_launched && CurrentPhase != FlightPhase.Ending)
            {
                // 位置由 NetworkTransform 同步，朝向由服务器经 NetworkTransform 同步过来。
                // ★ 这里刻意不做旋转插值 —— Slerp 会在两帧之间产生
                //   中间姿态，可能带来短暂的俯仰/横滚。
                //   同时强制水平，保证客户端也没有倾斜。
                EnforceLevelAttitude();
            }

            // 座位跟随（本地玩家）
            if (_seat != null && _playerRoot != null && ShouldRide())
            {
                _seat.AlignTo(_playerRoot);
            }
        }

        private bool ShouldRide()
        {
            // 起飞后所有玩家都在龙背上
            return _launched;
        }

        // ---------------- 阶段推进 ----------------

        private bool AdvanceTimer(float duration)
        {
            if (duration <= 0f)
            {
                return true;
            }

            _phaseTimer += Time.deltaTime;
            _localProgress = Mathf.Clamp01(_phaseTimer / duration);
            NetProgress = _localProgress;
            return _phaseTimer >= duration;
        }

        private void SetPhase(FlightPhase phase)
        {
            _phaseTimer = 0f;
            _localProgress = 0f;
            NetProgress = 0f;
            _phaseStartPos = GetVisualCenter();
            _localPhase = phase;

            // 阶段切换时重置该阶段的运行时状态
            if (phase == FlightPhase.Departure)
            {
                _departureStart = Vector3.zero;
                _departureElapsed = 0f;
                _departTravelled = 0f;
            }
            if (phase == FlightPhase.Orbit)
            {
                _orbitElapsed = 0f;
                _orbitAngularSpeed = 0f;
                // ★ 不重置 _smoothedOrbitSpeed —— 让角速度从上一阶段平滑过渡过来
            }
            if (phase == FlightPhase.TakeOff)
            {
                _takeOffElapsed = 0f;
            }
            NetPhase = (int)phase;

            // ★★★ 黑场触发补丁（本次新增）：
            //   RequestFadeOut 原来只挂在 NetPhase 的 SyncVar hook 里，
            //   但 Mirror 在【单机/无网络】模式下 SyncVar 写入不触发 hook
            //   —— 单机预览时飞到结尾黑场永远不会出现。
            //   这里在权威侧切阶段时直接触发；联网时 hook 与本处会各触发
            //   一次，FlightFadeController 的 MoveTowards 幂等，重复调用无害。
            if (phase == FlightPhase.Ending)
            {
                RequestFadeOut();
            }
        }

        // ---------------- 各阶段运动 ----------------

        private void ApplyTakeOff()
        {
            float t = _localProgress;

            // ★ 缓入缓出：起飞和到达都平滑，不会有突然的加速/停顿
            float eased = Mathf.SmoothStep(0f, 1f, t);

            Vector3 center = GetVisualCenter();
            Vector3 pos = center;

            // 爬升到目标高度（缓入缓出）
            float baseY = Mathf.Lerp(_phaseStartPos.y, FlightAltitude, eased);

            // ★ 龙背起伏：起飞时幅度小、频率低，只是轻微的浮沉感
            _takeOffElapsed += Time.deltaTime;
            float bob = Mathf.Sin(_takeOffElapsed * BodyBobbingFreq * Mathf.PI * 2f)
                       * BodyBobbing * 0.6f;
            pos.y = baseY + bob;

            // 水平方向：先悬停一下再前进，像真实起飞
            float forwardT = Mathf.Max(0f, (t - 0.25f) / 0.75f);
            forwardT = Mathf.SmoothStep(0f, 1f, forwardT);
            pos += _takeOffForward * (forwardT * TakeOffForwardDistance);

            SetPositionWithCenterOffset(pos);

            // ★ 始终保持水平：只改变偏航（Y 轴），绝不俯仰或侧倾。
            //   第一人称骑乘时任何俯仰都会让人强烈不适。
            float yaw = transform.eulerAngles.y;
            Quaternion desired = Quaternion.Euler(0f, yaw, 0f);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, desired, RotationSpeedDeg * 0.5f * Time.deltaTime);
        }

        /// <summary>
        /// 取得模型中心的当前世界坐标。
        /// 若启用 AutoCompensateVisualCenter，会把 ModelCenterLocal 的偏移扣掉，
        /// 让脚本控制的是"龙的视觉中心"而不是"预制体根节点"。
        /// </summary>
        private Vector3 GetVisualCenter()
        {
            if (!AutoCompensateVisualCenter)
            {
                return transform.position;
            }

            // 用世界方向计算偏移量，避免受父级旋转影响
            Vector3 worldOffset = transform.TransformDirection(ModelCenterLocal);
            return transform.position + worldOffset;
        }

        /// <summary>
        /// 移动到指定位置，并自动补偿模型中心偏移。
        /// </summary>
        private void SetPositionWithCenterOffset(Vector3 targetPosition)
        {
            if (!AutoCompensateVisualCenter)
            {
                transform.position = targetPosition;
                return;
            }

            // 目标位置是"模型中心应该在的地方"，
            // 所以根节点要往反方向退掉偏移量
            Vector3 worldOffset = transform.TransformDirection(ModelCenterLocal);
            transform.position = targetPosition - worldOffset;
        }

        private void ApplyOrbit()
        {
            // ★ 目标角速度：直接由 OrbitAngularSpeedDeg 决定（度/秒），
            //   盘旋时长由它与圈数自然决定，不做二次换算。
            float targetSpeed = OrbitAngularSpeedDeg;

            // 平滑逼近目标角速度 —— 阶段切换时不会突然加速
            _smoothedOrbitSpeed = Mathf.SmoothDamp(
                _smoothedOrbitSpeed, targetSpeed, ref _orbitSpeedVel,
                VelocitySmoothTime);

            // 首帧直接给目标值，避免从 0 开始慢慢加速
            if (_orbitElapsed < 0.05f && _smoothedOrbitSpeed < 0.01f)
            {
                _smoothedOrbitSpeed = targetSpeed;
            }

            // ★ 速度呼吸：默认 Orbit Speed Variation = 0，即匀速。
            //   如需极轻微的快慢变化可调大，但建议不超过 0.15。
            if (OrbitSpeedVariation > 0.001f)
            {
                float speedMod = 1f
                    + Mathf.Sin(_orbitElapsed * 0.13f) * OrbitSpeedVariation;
                _smoothedOrbitSpeed *= speedMod;
            }

            _orbitAngularSpeed = _smoothedOrbitSpeed;

            _orbitAngle += _orbitAngularSpeed * Time.deltaTime;
            _orbitElapsed += Time.deltaTime;

            float rad = _orbitAngle * Mathf.Deg2Rad;
            Vector3 center = GetVisualCenter();

            // ★ 半径呼吸：盘旋时缓慢向外扩张再收拢，不是完美圆。
            //   只用一圈一个周期（sin(rad)），避免 2 倍频造成椭圆抖动。
            float radiusVar = 1f
                + Mathf.Sin(rad) * (OrbitRadialSwing / Mathf.Max(1f, OrbitRadius));
            float radius = OrbitRadius * radiusVar;

            // 垂直起伏：极低频极小幅度，只为避免完全僵硬
            float bobY = Mathf.Sin(_orbitElapsed * 0.3f) * OrbitBobbing;

            Vector3 pos = center + new Vector3(
                Mathf.Cos(rad) * radius,
                bobY,
                Mathf.Sin(rad) * radius);

            SetPositionWithCenterOffset(pos);

            // ★ 朝向：沿运动方向的切线（不是纯圆心切线，跟随半径变化）
            Vector3 tangent = new Vector3(
                -Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;

            // ★ 完全水平：朝向就是水平切线方向，零俯仰零侧倾
            Vector3 flatTangent = new Vector3(tangent.x, 0f, tangent.z);
            if (flatTangent.sqrMagnitude < 0.0001f) flatTangent = Vector3.forward;
            Quaternion desired = Quaternion.LookRotation(flatTangent.normalized, Vector3.up);

            // ★ 不做侧倾——用户要求只保留上下起伏。
            //   如需转弯侧倾，把 Body Roll 调大并启用下面这行：
            //   desired *= Quaternion.Euler(0f, 0f, -BodyRoll);

            // 角速度限制的平滑转向
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, desired, RotationSpeedDeg * Time.deltaTime);
        }

        private void ApplyDeparture()
        {
            float t = _localProgress;

            // ★ 阶段开始时记录前进方向与起点
            if (_departureStart.sqrMagnitude < 0.001f)
            {
                _departureStart = GetVisualCenter();
                Vector3 fwd = transform.forward;
                fwd.y = 0f;
                _departureDir = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            }

            // ★★ 速度平滑：这是消除阶段切换突跳的关键。
            //   用 SmoothDamp 让实际速度平滑逼近目标速度，
            //   任何时候速度都是连续变化的，不会突然加速或减速。
            float targetSpeed = DepartureSpeed;

            // 进入云层后减速（像穿过阻力），用平滑插值不用突变
            if (t > CloudEntryPoint)
            {
                float inCloud = (t - CloudEntryPoint)
                              / Mathf.Max(0.01f, 1f - CloudEntryPoint);
                targetSpeed *= Mathf.Lerp(1f, 0.5f,
                                Mathf.SmoothStep(0f, 1f, inCloud));
            }

            _smoothedDepartureSpeed = Mathf.SmoothDamp(
                _smoothedDepartureSpeed, targetSpeed, ref _departureSpeedVel,
                VelocitySmoothTime);

            // 首帧直接给目标值，避免从 0 开始缓慢加速
            if (_departureElapsed < 0.05f)
            {
                _smoothedDepartureSpeed = targetSpeed;
            }

            // ★ 位移完全由平滑后的速度累积而来，
            //   不再用「固定距离 × 进度」——那样会导致速度与位移矛盾。
            _departTravelled += _smoothedDepartureSpeed * Time.deltaTime;

            Vector3 pos = _departureStart + _departureDir * _departTravelled;

            // ★ 龙背起伏：只做上下，低频小幅度。
            //   进入云层后逐渐平息，像被云包裹住。
            float bobAmp = BodyBobbing
                         * (1f - 0.7f * Mathf.SmoothStep(
                                0f, 1f,
                                Mathf.Max(0f, (t - CloudEntryPoint) / 0.5f)));
            _departureElapsed += Time.deltaTime;
            float bob = Mathf.Sin(_departureElapsed * BodyBobbingFreq * Mathf.PI * 2f) * bobAmp;

            // 高度缓慢锁定到目标高度，起伏叠加在锁定值之上
            float baseY = Mathf.Lerp(_departureStart.y, FlightAltitude, 1.2f * Time.deltaTime);
            pos.y = baseY + bob;

            // ★ 不做侧向摆动——用户要求只保留上下起伏。
            //   如需侧摆，把 Body Sway 调大并在下面启用：
            //   Vector3 right = Vector3.Cross(_departureDir, Vector3.up).normalized;
            //   pos += right * (Mathf.Sin(_departureElapsed * BodyBobbingFreq) * BodySway);

            SetPositionWithCenterOffset(pos);

            // ★ 完全水平：朝向前进方向，零俯仰
            Vector3 flatDir = new Vector3(_departureDir.x, 0f, _departureDir.z);
            if (flatDir.sqrMagnitude < 0.0001f) flatDir = Vector3.forward;
            Quaternion desired = Quaternion.LookRotation(flatDir.normalized, Vector3.up);

            // ★ 不做侧倾（Body Roll 默认 0）。如需转弯侧倾，把 Body Roll 调大并启用：
            // desired *= Quaternion.Euler(0f, 0f, -Mathf.Sin(_departureElapsed * 0.25f) * BodyRoll);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, desired, RotationSpeedDeg * 0.8f * Time.deltaTime);
        }

        private static float SmoothStep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // ---------------- 玩家接口 ----------------

        /// <summary>
        /// 绑定本地玩家的 XR Origin（或任意根物体）用于座位跟随。
        /// 由外部调用，通常在玩家生成时。
        /// </summary>
        public void BindPlayerRoot(Transform playerRoot)
        {
            _playerRoot = playerRoot;
            if (_seat != null)
            {
                _seat.ResetSmoothing();
            }
        }

        /// <summary>
        /// 玩家报告"我已上龙"。本地调用即可，内部自动走 Command 上报服务器。
        /// ★ 单机预览（StandaloneMode 且无网络会话）时直接本地处理——
        ///   Mirror 在无网络时 Command 会被静默丢弃，必须走本地路径。
        /// </summary>
        public void ReportSeated()
        {
            if (isServer || (StandaloneMode && !HasNetwork))
            {
                TryLaunch();
            }
            else
            {
                CmdReportSeated();
            }
        }

        [Command(requiresAuthority = false)]
        private void CmdReportSeated()
        {
            TryLaunch();
        }

        private void TryLaunch()
        {
            if (_launched)
            {
                return;
            }

            // ★★★ 本次修复：真正等待所有人上龙。
            //
            //   旧逻辑的问题：任何人 ReportSeated() 都直接把
            //   NetSeatedCount 拉满并立即起飞 —— 多人时其他玩家
            //   还没上龙龙就飞了。
            //
            //   新逻辑：每次 ReportSeated 计数 +1，
            //   达到预期人数（RequiredPlayers，0=自动取当前在线人数）
            //   才起飞。StandaloneMode（单人调试）保持 1 人即飞。
            int expected = RequiredPlayers;
            if (expected <= 0 && NetworkManager.singleton != null)
            {
                expected = Mathf.Max(1, NetworkManager.singleton.numPlayers);
            }
            if (expected <= 0)
            {
                expected = 1;
            }

            if (StandaloneMode && !HasNetwork)
            {
                // 单机调试：不计人数，直接起飞
                NetSeatedCount = 1;
                LaunchNow(expected);
                return;
            }

            int seated = NetSeatedCount + 1;
            NetSeatedCount = seated;

            Debug.Log($"[Flight] 玩家就位 {seated}/{expected}", this);

            if (seated >= expected)
            {
                LaunchNow(expected);
            }
            else
            {
                // 广播等待状态，客户端可显示「等待其他玩家上龙 x/y」
                NetPhase = (int)FlightPhase.WaitingForPlayers;
            }
        }

        private void LaunchNow(int expected)
        {
            _launched = true;
            _phaseTimer = 0f;
            _phaseStartPos = GetVisualCenter();
            _takeOffForward = transform.forward;
            _keyTime = 0f;
            // ★ 平滑 pitch 同步到轨迹起点俯仰角，避免复位后残余追赶
            if (Keyframes != null && Keyframes.frames != null
                && Keyframes.frames.Count > 0)
            {
                _smoothedPitch = Keyframes.frames[0].pitch;
            }
            _localPhase = FlightPhase.TakeOff;
            NetPhase = (int)FlightPhase.TakeOff;

            Debug.Log($"[Flight] 🚀 起飞！参与人数 {expected}", this);
        }

        /// <summary>
        /// ★ 复位整个飞行体验（黑场后循环用）：
        ///   龙回轨迹起点、相位回 WaitingForPlayers、座位计数清零、
        ///   骨骼动画对时归零。玩家端的下龙/淡入由 FlightExperienceLoop 驱动。
        ///   仅服务器/单机可调（多人时各端由 NetPhase 同步看到等待态）。
        /// </summary>
        public void ResetFlight()
        {
            if (Application.isPlaying && !(isServer || (StandaloneMode && !HasNetwork)))
            {
                Debug.LogWarning("[Flight] ResetFlight 仅服务器/单机可调。", this);
                return;
            }

            _launched = false;
            _keyTime = 0f;
            _phaseTimer = 0f;
            NetSeatedCount = 0;
            _localPhase = FlightPhase.WaitingForPlayers;
            NetPhase = (int)FlightPhase.WaitingForPlayers;

            // 龙回到轨迹起点（位置 + 朝向）
            if (Keyframes != null && Keyframes.frames != null
                && Keyframes.frames.Count > 0)
            {
                var f0 = Keyframes.frames[0];
                SetPositionWithCenterOffset(f0.pos);
                transform.rotation = Quaternion.Euler(
                    0f, f0.yaw - ModelYawOffset, 0f);
                FlightYaw = f0.yaw;
            }

            // 骨骼动画对时归零
            var rideAnim = GetComponent<Animator>();
            if (rideAnim != null && rideAnim.runtimeAnimatorController != null)
            {
                rideAnim.Play(FlyStateHash, 0, 0f);
                rideAnim.Update(0f);
            }

            Debug.Log("[Flight] ♻ 飞行体验已复位（WaitingForPlayers）", this);
        }

        /// <summary>
        /// 强制起飞，跳过等待。用于调试。
        /// </summary>
        [ContextMenu("强制起飞（服务器）")]
        public void ForceLaunch()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Flight] 需在运行模式下使用。", this);
                return;
            }
            if (isServer || (StandaloneMode && !HasNetwork))
            {
                TryLaunch();
            }
            else
            {
                CmdForceLaunch();
            }
        }

        [Command(requiresAuthority = false)]
        private void CmdForceLaunch()
        {
            TryLaunch();
        }

        // ---------------- 黑场衔接 ----------------

        /// <summary>
        /// 请求淡出到全黑。
        ///
        /// ★ 重要（实测发现的团队现状）：
        ///   1. StoryTimelineManager 的 `onFadeOutRequested` / `onFadeInRequested`
        ///      在场景里**没有绑定任何事件**（m_Calls 为空数组），
        ///      所以直接依赖它黑屏不会生效，需要团队先在 Inspector 里接上 UI。
        ///   2. `NotifyFadeOutComplete()` 的语义是「黑场**已完成**」，
        ///      应该由黑场动画播完时调用，**不是**在请求时调用。
        ///
        ///   所以这里的做法是：
        ///     - 优先派发中文事件「翱翔淡出」，团队的 CommandTerminal / UI 可监听
        ///     - 若场景里有 EventDispatcher，同时派发它
        ///     - 黑场动画播完后，外部应调用 NotifyFadeOutComplete() 通知系统
        /// </summary>
        private void RequestFadeOut()
        {
            // 方式 0：★ 自带黑场兜底（本次新增，保证黑场一定发生）
            //   FlightFadeController 在场景里就自动淡出，不依赖团队接线。
            //   多人时每个客户端在进入 Ending 阶段时本地各自淡出，
            //   时序由服务器权威的 NetPhase 驱动，天然一致。
            var fade = FindFirstObjectByType<FlightFadeController>();
            if (fade != null)
            {
                fade.FadeOut(FadeOutDuration);
            }

            // 方式 1：团队的事件总线（如果团队接了监听者就会生效）
            var dispatcher = FindFirstObjectByType<EventDispatcher>();
            if (dispatcher != null)
            {
                dispatcher.Dispatch("翱翔淡出");
            }

            // 方式 2：UnityEvent（需要在 Inspector 绑定黑场 UI）
            if (onFadeOutRequested != null)
            {
                onFadeOutRequested.Invoke();
            }

            // 方式 3：StoryTimelineManager 的淡出请求
            var manager = FindFirstObjectByType<Greatwall.VRAnimation.Timeline.StoryTimelineManager>();
            if (manager != null)
            {
                // 注意：这里不调 NotifyFadeOutComplete（那是"已完成"回调），
                // 只记录意图。团队需要在 onFadeOutRequested 里做黑场，
                // 动画播完后再调 NotifyFadeOutComplete()。
                Debug.Log("[Flight] 已请求淡出。黑场动画播完后请调用 " +
                          "StoryTimelineManager.NotifyFadeOutComplete()", this);
            }
            else if (fade == null)
            {
                Debug.LogWarning("[Flight] 场景里既没有 FlightFadeController 也没有 " +
                                 "StoryTimelineManager，黑场无法执行！", this);
            }
        }
    }
}
