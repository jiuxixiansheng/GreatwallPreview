using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace Greatwall.Flight
{
    /// <summary>
    /// 骑龙段落的正式接入点（挂在 part3/上龙传送门 上）。
    ///
    /// 触发：StoryTimelineManager 进入 chapter3 的那一帧（按章节索引监听；
    /// timeline_03 所在根节点未激活、播不起来时也能生效），
    /// 本组件接管整段演出：
    ///   1. 停掉 timeline_03 —— 段落内容全部由本组件驱动；
    ///      提前 Stop 不会触发 TimelineCompletionRelay（invokeWhenStoppedEarly = false），
    ///      所以章节不会在 27s 后被绕回 chapter0；
    ///   2. 玩家位置完全不动（段落布景固定在场景里，不跟随玩家）；
    ///   3. 隐藏 part2-2 交接下来的剧情龙（Dragon_root），避免和开场龙同框；
    ///   4. 骑龙保持隐藏，只在玩家踩上传送板那一刻由 DragonBoardingGate 激活并对齐到龙头；
    ///   5. 生成「龙环绕飞行3」的开场龙：盘旋 → 前飞 → 悬浮，并交给传送门计时。
    ///
    /// 之后全由 DragonBoardingGate 负责：开场龙出现 9s 后亮传送板 → 走上去 → 上龙 → 起飞。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]   // 必须早于 DragonBoardingGate（0）执行：触发判定前先纠正模拟器头显偏移
    public class FlightSegmentStage : MonoBehaviour
    {
        [Header("章节触发")]
        [Tooltip("章节索引：StoryManager 切到这一章时接管段落内容（当前工程 chapter3 = 4）。")]
        public int ChapterIndex = 4;

        [Tooltip("chapter3 的 PlayableDirector（timeline_03）。留空则按下面的名字自动查找。")]
        public PlayableDirector ChapterDirector;

        [Tooltip("自动查找时匹配的对象名。")]
        public string ChapterDirectorName = "timeline_03";

        [Header("引用（留空自动查找）")]
        [Tooltip("上龙传送门。默认取本物体上的 DragonBoardingGate。")]
        public DragonBoardingGate Gate;

        [Tooltip("骑龙飞行的龙根（part3/骑龙飞上天空），整段保持隐藏。")]
        public Transform RideDragon;

        [Tooltip("章节系统留下的剧情龙（Dragon_root）。接管后整段隐藏。")]
        public Transform StoryDragon;

        [Header("舞台基准")]
        [Tooltip("段落固定朝向（绕 Y 轴角度，0=+Z）。传送门、开场龙、骑龙航线都按这个方向在场景里摆好，不参考玩家实际站位。")]
        public float StageYaw = -92.68f;

        [Header("骑龙摆位")]
        [Tooltip("骑龙放在传送门前方多远（米）。上龙时 DragonBoardingGate 会把它对齐到开场龙头。")]
        public float DragonDistance = 62f;

        [Tooltip("骑龙相对传送门抬高多少米。")]
        public float DragonHeight = 3.06f;

        [Header("悬浮落点")]
        [Tooltip("悬浮段龙头的世界坐标。")]
        public Vector3 HoverHeadWorldPos = new Vector3(-21.345f, 8.200f, -1.710f);

        [Tooltip("盘旋朝向相对「正对观众」偏转多少度。只改朝向，飞行仍是沿龙头直线。")]
        public float DragonYawOffset = -12f;

        [Header("云雾（必须在 FlightClouds 激活前写入，否则不生效）")]
        [Tooltip("远景天际线云团数覆盖。0 = 用场景值。")]
        public int CloudDistantCount = 72;

        [Tooltip("远景云尺寸倍率覆盖。")]
        public float CloudDistantSizeMul = 3.6f;

        [Tooltip("远景云与轨迹的最小水平距离（米）。")]
        public float CloudDistantMinDist = 120f;

        [Tooltip("远景云与轨迹的最大水平距离（米）。")]
        public float CloudDistantMaxDist = 500f;

        [Tooltip("近景云团配额覆盖（粒子池上限）。")]
        public int CloudMaxCount = 200;

        [Tooltip("近景预撒云团数覆盖。")]
        public int CloudPreWarm = 220;

        [Tooltip("新云生成的最小距离（米）。")]
        public float CloudMinSpawnDistance = 100f;

        [Tooltip("补云间隔（秒）。")]
        public float CloudSpawnInterval = 0.9f;

        [Tooltip("云雾整体不透明度覆盖。")]
        public float CloudOpacity = 0.62f;

        private const string OpeningDragonResource = "OpeningDragon";

        // part 显隐是演出流程（CommandTerminal.ZhuanChang01/02/03）驱动的，不由章节驱动：
        // 直接切到 chapter3 时 part2-2 仍是关的，段落会搭在 part1 的城墙上。
        // 进入段落时按同一套语义补切一次，保证背景恒为 part2-2（正常流程里是空操作）。
        private const string BackdropPartRoot = "part2-2";
        private static readonly string[] PrecedingPartRoots = { "part0", "part1", "Part2" };

        private bool _started;
        private bool _everMounted;
        private DragonRideController _rider;
        private bool _riderWarned;
        private XRDeviceSimulator _simulator;
        private bool _chapterResolved;
        private MonoBehaviour _chapterManager;
        private System.Reflection.FieldInfo _chapterIndexField;

        private void Awake()
        {
            if (Gate == null) Gate = GetComponent<DragonBoardingGate>();
            if (RideDragon == null) RideDragon = FindSceneTransform("骑龙飞上天空");
            if (StoryDragon == null) StoryDragon = FindSceneTransform("Dragon_root");
            if (ChapterDirector == null) ChapterDirector = FindSceneDirector(ChapterDirectorName);
            _rider = FindFirstObjectByType<DragonRideController>(FindObjectsInactive.Include);

            if (ChapterDirector == null)
                Debug.LogWarning($"[FlightStage] 没找到章节导演 '{ChapterDirectorName}'，只按章节索引触发。", this);
        }

        private void Update()
        {
            if (!_started)
            {
                if (!_chapterResolved) ResolveChapterManager();

                // 章节索引是主触发：part2-2（timeline_03 的父级）未激活时
                // timeline_03 根本播不起来，只靠 director.state 会漏触发；
                // director 正在播放则作为兜底（例如手动播放 timeline_03）。
                if (IsTargetChapterActive() ||
                    (ChapterDirector != null && ChapterDirector.state == PlayState.Playing))
                    EnterSegment();
                return;
            }

            if (EnsureRider() == null) return;

            // 模拟器的 WASD 只挪「头显」，XR Origin 根不动。上龙前每帧把这段位移
            // 并进根（头显偏移始终≈0），否则攒到传送板前一次性并回会让眼睛跟着前移，
            // 第一次踩板不触发、必须退回来再走一次。
            if (_rider.IsMounted)
            {
                if (!_everMounted)
                {
                    _everMounted = true;
                    // 上龙后镜头整个飞行段都在城墙外/上方，留着会穿帮；复位淡入前再放回来
                    SetBackdropPartActive(false);
                }
                ZeroSimulatorHeadOffset();
            }
            else
            {
                // 骑完一圈后 FlightExperienceLoop 会复位并下龙，骑龙停在航线起点，
                // 从城墙上能看见它悬在墙外；收尾时把骑龙收回、城墙放回来。
                if (_everMounted && _rider.Director != null
                    && _rider.Director.CurrentPhase == FlightPhase.WaitingForPlayers)
                {
                    HideRideDragonAfterFlight();
                    SetBackdropPartActive(true);
                }

                TransferSimulatorOffsetToRig();
            }
        }

        private void HideRideDragonAfterFlight()
        {
            if (RideDragon == null || !RideDragon.gameObject.activeSelf) return;
            RideDragon.gameObject.SetActive(false);
        }

        /// <summary>
        /// 团队 10-27 联机版把玩家改成运行时生成的预制体（Mirror playerPrefab），
        /// 场景里那份挂着 DragonRideController 的旧 XR Origin 被删掉了。
        /// 这里在段落开始时给「本机玩家」补一个并接好座位/飞行总控，
        /// 只影响本机镜头所在的 rig，远端玩家不会被瞬移。
        /// </summary>
        private DragonRideController EnsureRider()
        {
            if (_rider != null) return _rider;

            Camera cam = Camera.main;
            Transform rig = cam != null ? cam.transform.root : null;

            // 优先认本机镜头所在的 rig，避免多人时误抓远端玩家的控制器
            var rider = rig != null ? rig.GetComponent<DragonRideController>() : null;
            if (rider == null) rider = FindFirstObjectByType<DragonRideController>(FindObjectsInactive.Include);
            if (rider == null)
            {
                if (rig == null) return null;   // 玩家 rig 还没生成（联机 host/client 尚未就绪）
                rider = rig.gameObject.AddComponent<DragonRideController>();
            }

            if (rider.SeatAnchor == null)
            {
                if (RideDragon != null) rider.SeatAnchor = RideDragon.GetComponentInChildren<DragonSeatAnchor>(true);
                if (rider.SeatAnchor == null)
                    rider.SeatAnchor = FindFirstObjectByType<DragonSeatAnchor>(FindObjectsInactive.Include);
            }
            if (rider.Director == null)
            {
                if (RideDragon != null) rider.Director = RideDragon.GetComponent<FlightDirector>();
                if (rider.Director == null)
                    rider.Director = FindFirstObjectByType<FlightDirector>(FindObjectsInactive.Include);
            }

            if (rider.SeatAnchor == null || rider.Director == null)
            {
                if (!_riderWarned)
                {
                    _riderWarned = true;
                    Debug.LogWarning("[FlightStage] 玩家 rig 上补了 DragonRideController，但找不到 " +
                                     "DragonSeatAnchor/FlightDirector，无法上龙。", this);
                }
                return null;
            }

            _rider = rider;
            return _rider;
        }

        /// <summary>
        /// 接管 chapter3：停掉 timeline_03，摆好观众机位 / 开场龙 / 骑龙，
        /// 之后交给 DragonBoardingGate。
        /// </summary>
        public void EnterSegment()
        {
            if (_started) return;
            _started = true;

            EnsureBackdropPart();

            if (ChapterDirector != null && ChapterDirector.state == PlayState.Playing)
                ChapterDirector.Stop();

            if (StoryDragon != null)
                StoryDragon.gameObject.SetActive(false);

            // 防 timeline_03 的激活轨抢先打开骑龙（Stop 会还原，这里再兜一层）
            if (RideDragon != null)
                RideDragon.gameObject.SetActive(false);

            ApplyCloudOverrides();
            PlaceRideDragon();
            SpawnOpeningDragon();
        }

        /// <summary>
        /// 把背景 part 切到 part2-2（隐藏 part0/part1/Part2）。
        /// 与 CommandTerminal.ZhuanChang01~03 的 hide/show 语义一致；正常情况下
        /// 转场 03 已经切过，这里不会产生任何可见变化。
        /// </summary>
        private void EnsureBackdropPart()
        {
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                string n = roots[i].name;
                for (int j = 0; j < PrecedingPartRoots.Length; j++)
                {
                    if (n == PrecedingPartRoots[j] && roots[i].activeSelf)
                        roots[i].SetActive(false);
                }
            }
            SetBackdropPartActive(true);
        }

        private void SetBackdropPartActive(bool active)
        {
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name != BackdropPartRoot || roots[i].activeSelf == active) continue;
                roots[i].SetActive(active);
                Debug.Log(active ? "[FlightStage] 复位：放回 part2-2 城墙。" : "[FlightStage] 已上龙：收起 part2-2 城墙。");
            }
        }

        [ContextMenu("调试：直接进入骑龙段落")]
        private void DebugEnterSegment()
        {
            if (Application.isPlaying) EnterSegment();
        }

        /// <summary>舞台固定朝向（水平面内），所有摆位都用它，不读玩家朝向。</summary>
        private Vector3 StageForward
        {
            get
            {
                Vector3 forward = Quaternion.Euler(0f, StageYaw, 0f) * Vector3.forward;
                forward.y = 0f;
                return forward.normalized;
            }
        }

        /// <summary>生成开场盘旋龙：直接实例化「龙环绕飞行3」自己的模型 + 原始盘旋动画。</summary>
        private void SpawnOpeningDragon()
        {
            var prefab = Resources.Load<GameObject>(OpeningDragonResource);
            if (prefab == null)
            {
                Debug.LogError($"[FlightStage] Resources 下找不到 {OpeningDragonResource} 预制体。", this);
                return;
            }

            // 场景里若残留旧的开场龙实例，先清掉，避免两条盘旋龙同框
            var strays = Resources.FindObjectsOfTypeAll<OpeningDragon>();
            for (int i = 0; i < strays.Length; i++)
            {
                if (strays[i] != null && strays[i].gameObject.scene.IsValid())
                    Destroy(strays[i].gameObject);
            }

            Vector3 forward = StageForward;

            var go = Instantiate(prefab);
            go.name = "OpeningDragon";

            var comp = go.GetComponent<OpeningDragon>();
            if (comp == null) comp = go.AddComponent<OpeningDragon>();

            // 悬浮龙头钉到固定落点，龙头朝向 = 观众反方向偏 DragonYawOffset
            Vector3 dragonForward = Quaternion.Euler(0f, DragonYawOffset, 0f) * (-forward);
            comp.AlignHoverHeadTo(HoverHeadWorldPos, dragonForward);

            if (Gate != null)
                Gate.BeginWithOpeningDragon(go);
            else
                Debug.LogWarning("[FlightStage] 没有 DragonBoardingGate，传送板不会亮起。", this);

            comp.Play();
            Debug.Log($"[FlightStage] 已接管骑龙段落：悬浮龙头 {HoverHeadWorldPos:F2}，舞台朝向 {StageYaw:F1}°（不跟随玩家）");
        }

        /// <summary>
        /// 把骑龙摆到固定机位前方并保持隐藏。位置对构图不重要（上龙时会被对齐到龙头），
        /// 但朝向决定起飞后 K 帧轨迹的偏航基准，必须和舞台朝向一致、与玩家实际站位无关。
        /// </summary>
        private void PlaceRideDragon()
        {
            if (RideDragon == null) return;

            Vector3 forward = StageForward;
            Vector3 anchor = Gate != null ? Gate.transform.position : transform.position;

            Vector3 pos = anchor + forward * DragonDistance;
            pos.y = anchor.y + DragonHeight;

            // 龙头在模型局部 +Z，所以让 +Z 指向玩家回来的方向
            float yaw = Mathf.Atan2(-forward.x, -forward.z) * Mathf.Rad2Deg;
            RideDragon.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            RideDragon.gameObject.SetActive(false);
        }

        /// <summary>
        /// 云雾参数必须在 FlightCloudSpawner.Awake 之前写入：它一直是 inactive，
        /// 玩家上龙那一刻才激活。这里提前覆盖，Awake 才会按新参数建粒子池。
        /// </summary>
        private void ApplyCloudOverrides()
        {
            var spawner = FindFirstObjectByType<FlightCloudSpawner>(FindObjectsInactive.Include);
            if (spawner == null)
            {
                Debug.LogWarning("[FlightStage] 找不到 FlightCloudSpawner，云雾参数未覆盖。", this);
                return;
            }

            if (CloudDistantCount > 0) spawner.DistantCloudCount = CloudDistantCount;
            if (CloudDistantSizeMul > 0f) spawner.DistantSizeMul = CloudDistantSizeMul;
            if (CloudDistantMinDist > 0f) spawner.DistantMinDist = CloudDistantMinDist;
            if (CloudDistantMaxDist > 0f) spawner.DistantMaxDist = CloudDistantMaxDist;
            if (CloudMaxCount > 0) spawner.MaxCloudCount = CloudMaxCount;
            if (CloudPreWarm > 0) spawner.PreWarmClouds = CloudPreWarm;
            if (CloudMinSpawnDistance > 0f) spawner.MinSpawnDistance = CloudMinSpawnDistance;
            if (CloudSpawnInterval > 0f) spawner.SpawnInterval = CloudSpawnInterval;
            if (CloudOpacity > 0f) spawner.CloudOpacity = CloudOpacity;
        }

        // ---------------- XR Device Simulator ----------------

        private void TransferSimulatorOffsetToRig()
        {
            if (_rider == null) return;
            if (!TryGetSimulatorHmdState(out var state, out var field)) return;

            Vector3 local = state.centerEyePosition;
            local.y = 0f;
            if (local.sqrMagnitude < 1e-8f) return;

            // 设备坐标 = 相机父级（Camera Offset）局部空间
            var cam = Camera.main;
            Quaternion trackingRot = cam != null && cam.transform.parent != null
                ? cam.transform.parent.rotation
                : _rider.transform.rotation;

            Vector3 world = trackingRot * local;
            world.y = 0f;
            _rider.transform.position += world;

            state.centerEyePosition = Vector3.zero;
            state.leftEyePosition = Vector3.zero;
            state.rightEyePosition = Vector3.zero;
            state.devicePosition = Vector3.zero;
            field.SetValue(_simulator, state);
        }

        /// <summary>
        /// 直接把模拟器内部 HMD 状态的位移清零。
        ///
        /// ⛔ 不能用 InputSystem.QueueStateEvent：模拟器每帧 Update 末尾都会
        ///    InputState.Change(m_HMDDevice, m_HMDState) 把内部状态重新发布，
        ///    队列事件只生效一帧就被覆盖。改私有字段，模拟器自己就会发布 0。
        /// </summary>
        private void ZeroSimulatorHeadOffset()
        {
            if (!TryGetSimulatorHmdState(out var state, out var field)) return;

            state.centerEyePosition = Vector3.zero;
            state.leftEyePosition = Vector3.zero;
            state.rightEyePosition = Vector3.zero;
            state.devicePosition = Vector3.zero;
            field.SetValue(_simulator, state);
        }

        private bool TryGetSimulatorHmdState(out XRSimulatedHMDState state, out System.Reflection.FieldInfo field)
        {
            state = default;
            field = null;

            if (_simulator == null || !_simulator.isActiveAndEnabled)
            {
                _simulator = null;
                foreach (var s in Resources.FindObjectsOfTypeAll<XRDeviceSimulator>())
                    if (s != null && s.isActiveAndEnabled) { _simulator = s; break; }
            }
            if (_simulator == null) return false;

            field = SimulatorHmdStateField;
            if (field == null) return false;

            state = (XRSimulatedHMDState)field.GetValue(_simulator);
            return true;
        }

        private static System.Reflection.FieldInfo _hmdStateField;

        private static System.Reflection.FieldInfo SimulatorHmdStateField
        {
            get
            {
                if (_hmdStateField == null)
                    _hmdStateField = typeof(XRDeviceSimulator).GetField(
                        "m_HMDState",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return _hmdStateField;
            }
        }

        // ---------------- 章节监听 ----------------

        /// <summary>
        /// 用反射读 StoryTimelineManager 的当前章节（它没有公开的只读入口）。
        /// 按类型名查找，不直接引用团队类，避免他们改命名空间/改类名时本脚本编译不过。
        /// </summary>
        private void ResolveChapterManager()
        {
            _chapterResolved = true;

            System.Type managerType = null;
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                managerType = assemblies[i].GetType("Greatwall.VRAnimation.Timeline.StoryTimelineManager");
                if (managerType != null) break;
            }
            if (managerType == null)
            {
                Debug.LogWarning("[FlightStage] 找不到 StoryTimelineManager，只按 timeline_03 播放触发。", this);
                return;
            }

            _chapterIndexField = managerType.GetField(
                "currentChapterIndex",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (_chapterIndexField == null || _chapterIndexField.FieldType != typeof(int))
            {
                _chapterIndexField = null;
                Debug.LogWarning("[FlightStage] StoryTimelineManager 里没有 currentChapterIndex，只按 timeline_03 播放触发。", this);
                return;
            }

            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null && behaviours[i].GetType() == managerType)
                {
                    _chapterManager = behaviours[i];
                    return;
                }
            }

            Debug.LogWarning("[FlightStage] 场景里没有 StoryTimelineManager 实例，只按 timeline_03 播放触发。", this);
        }

        private bool IsTargetChapterActive()
        {
            if (_chapterManager == null || _chapterIndexField == null) return false;
            return (int)_chapterIndexField.GetValue(_chapterManager) == ChapterIndex;
        }

        // ---------------- 查找 ----------------

        private static Transform FindSceneTransform(string name)
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || !t.gameObject.scene.IsValid()) continue;
                if (t.gameObject.name == name) return t;
            }
            return null;
        }

        private static PlayableDirector FindSceneDirector(string name)
        {
            foreach (var d in Resources.FindObjectsOfTypeAll<PlayableDirector>())
            {
                if (d == null || !d.gameObject.scene.IsValid()) continue;
                if (d.gameObject.name == name) return d;
            }
            return null;
        }
    }
}
