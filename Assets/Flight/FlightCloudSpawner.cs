using UnityEngine;

namespace Greatwall.Flight
{
    /// <summary>
    /// 飞行段落的云生成。
    ///
    /// 为什么不用体积云：
    ///   URP 没有原生 VolumeClouds（那是 HDRP 专属），本项目是 URP 且没装 HDRP。
    ///   升级渲染管线会破坏现有全部效果，不可行。
    ///
    /// 本脚本的方案（粒子云团 + 距离衰减）：
    ///   - 用 ParticleSystem 撒云团，玩家从云中穿过 → 速度感来自"擦身而过"
    ///   - 云的疏密随阶段变化（起飞稀 → 环绕密 → 远飞变稀）
    ///   - 所有客户端用同一个随机种子，保证云的分布一致（多人同步要求）
    ///
    /// ★ 多人同步注意：
    ///   云的分布由固定种子决定，不用 Random.Range 播随机，
    ///   这样服务器和所有客户端生成完全相同的云，看起来才一致。
    ///
    /// ★ 性能注意（VR 必读）：
    ///   - Particle System 必须是 World Simulation 模式
    ///   - 不要开 Collision 模块（VR 下会造成性能断崖）
    ///   - 数量建议先从 200 起步，实测帧率后再加
    /// </summary>
    [DisallowMultipleComponent]
    public class FlightCloudSpawner : MonoBehaviour
    {
        [Header("云材质")]
        [Tooltip("云团材质。若留空则自动创建。\n" +
                 "留空时用程序生成的软边贴图；想要更好效果请配合 Cloud Texture。")]
        public Material CloudMaterial;

        [Tooltip("★ 云贴图。强烈建议指定项目里现成的无缝雾贴图：\n" +
                 "Assets/sza/tex/T_SandStorm_LargeFog_Seamless.png（1024x1024）\n" +
                 "留空则用程序生成的简易贴图。")]
        public Texture2D CloudTexture;

        [Header("生成区域")]
        [Tooltip("飞行中心点。★ 拖入 part3/骑龙飞上天空，云会跟着龙走。")]
        public Transform FlightCenter;

        [Tooltip("云团生成半径。\n" +
                 "★ 实测：这条龙 scale=5，环绕半径设 15。\n" +
                 "云的范围要比环绕半径大得多，才能有「穿云」的感觉。建议 60~120。")]
        [Range(20f, 300f)]
        public float SpawnRadius = 80f;

        [Tooltip("云团生成高度范围（相对飞行中心）。")]
        public float HeightRangeMin = -15f;
        public float HeightRangeMax = 20f;

        [Header("数量与性能")]
        [Tooltip("★ 云团数量（一朵云 = 一团 N 个粒子抱团）。改语义了：这是团数不是粒子数。\n" +
                 "总粒子数 = 团数 × 每团粒子数，由脚本自动计算。")]
        [Range(10, 400)]
        public int MaxCloudCount = 160;

        [Tooltip("每团粒子数。多个半透明大粒子叠加才蓬松，单粒子=孤立小点。")]
        [Range(4, 64)]
        public int ParticlesPerCloud = 20;

        [Tooltip("云团尺寸（单粒直径，米）。团内粒子围绕中心 ±30% 偏移、55%~100% 尺寸随机。")]
        [Range(2f, 120f)]
        public float CloudSize = 38f;

        [Tooltip("生成间隔（秒）。越小云越密。")]
        [Range(0.05f, 5f)]
        public float SpawnInterval = 0.12f;

        [Tooltip("★ 启动预撒：Play 后立即沿全轨迹撒这么多云团（否则前几秒天空空荡荡）。")]
        [Range(0, 400)]
        public int PreWarmClouds = 140;

        [Tooltip("★ 预撒的云按轨迹【弧长】每隔多少米放一团。\n" +
                 "这是关键参数：按时间随机采样会让环绕圈挤满云、\n" +
                 "远飞段直线几公里无云（实测最近云在 181m 外）。")]
        [Range(5f, 80f)]
        public float PreWarmArcStep = 22f;

        [Header("远景天际线云")]
        [Tooltip("★ 远景云团数量：撒在远离轨迹的外围环带上，构成远处天际线。\n" +
                 "没有它们时只有玩家周围有云、远处天空空荡，非常出戏。")]
        [Range(0, 150)]
        public int DistantCloudCount = 42;

        [Tooltip("远景云与轨迹的最小水平距离（米）。要明显超出近景云走廊。")]
        [Range(60f, 400f)]
        public float DistantMinDist = 150f;

        [Tooltip("远景云与轨迹的最大水平距离（米）。")]
        [Range(150f, 1200f)]
        public float DistantMaxDist = 600f;

        [Tooltip("远景云尺寸倍率。远景要足够大才看得见（38m 的云在 500m 外只有 4° 视角）。")]
        [Range(1f, 6f)]
        public float DistantSizeMul = 3.5f;

        [Tooltip("远景云相对轨迹的高度偏移范围（米，相对值）。略微偏上，让天际线云浮在山脊上方。")]
        public Vector2 DistantHeightRange = new Vector2(-5f, 30f);

        [Header("★ 手动密度曲线（覆盖自动前薄后厚）")]
        [Tooltip("★ 启用后：云雾密度完全由下方曲线控制。\n" +
                 "横轴 = 飞行进度（0~1），纵轴 = 密度（0~1）。\n" +
                 "同时控制三处：预撒概率 / 飞行中补云概率 / 材质透明度。\n" +
                 "Inspector 里直接编辑曲线关键帧，Play 中修改即时生效。")]
        public bool UseExternalCurve = false;

        [Tooltip("★ 近景/环境云密度曲线。")]
        public AnimationCurve DensityCurve = new AnimationCurve(
            new Keyframe(0f, 0.08f), new Keyframe(0.25f, 0.22f),
            new Keyframe(0.5f, 0.50f), new Keyframe(0.75f, 0.85f),
            new Keyframe(1f, 1f));

        [Tooltip("★ 远景天际线云密度曲线（1 = CloudOpacity 全值）。")]
        public AnimationCurve DistantCurve = new AnimationCurve(
            new Keyframe(0f, 0.35f), new Keyframe(1f, 0.8f));

        [Header("★ 云雾密度滑杆（拖动即调，推荐）")]
        [Tooltip("★ 勾选 = 用下方 12 个中文滑杆控制云雾密度（简单直观，拖动立即生效）。\n取消 = 回到高级模式（曲线/自动公式）。\n六个滑杆对应飞行六个阶段，点之间自动平滑过渡。")]
        public bool 使用滑杆控制 = true;

        [Space(5)]
        [Tooltip("飞行开始，龙起步滑跑阶段（进度 0%）。")]
        [Range(0f, 1f)] public float 密度_起飞滑跑 = 0.08f;
        [Tooltip("龙拉起升空、爬升阶段（进度 20%）。")]
        [Range(0f, 1f)] public float 密度_升空爬升 = 0.22f;
        [Tooltip("环绕长城飞行的前半段（进度 40%）。")]
        [Range(0f, 1f)] public float 密度_环绕前段 = 0.50f;
        [Tooltip("环绕长城飞行的后半段（进度 60%）。")]
        [Range(0f, 1f)] public float 密度_环绕后段 = 0.80f;
        [Tooltip("脱离环绕、向远处飞冲刺（进度 80%）。")]
        [Range(0f, 1f)] public float 密度_远飞冲刺 = 0.90f;
        [Tooltip("即将冲入云海、黑场收尾（进度 100%）。")]
        [Range(0f, 1f)] public float 密度_黑场之前 = 1.00f;

        [Space(8)]
        [Tooltip("以下 6 个滑杆控制【远景天际线云】的各阶段密度\n（云在远处山脊上方恒定可见，数值是 CloudOpacity 的倍率）。")]
        [Range(0f, 1f)] public float 远景_起飞滑跑 = 0.30f;
        [Range(0f, 1f)] public float 远景_升空爬升 = 0.40f;
        [Range(0f, 1f)] public float 远景_环绕前段 = 0.50f;
        [Range(0f, 1f)] public float 远景_环绕后段 = 0.60f;
        [Range(0f, 1f)] public float 远景_远飞冲刺 = 0.70f;
        [Range(0f, 1f)] public float 远景_黑场之前 = 0.75f;

        [Header("阶段化密度")]
        [Tooltip("起飞阶段云密度倍率。起飞时视野未稳，云少些更安全。")]
        [Range(0f, 2f)]
        public float TakeOffDensity = 0.5f;

        [Tooltip("环绕阶段云密度倍率。环绕时需要最强的速度感。")]
        [Range(0f, 2f)]
        public float OrbitDensity = 1f;

        [Tooltip("远飞阶段云密度倍率。")]
        [Range(0f, 2f)]
        public float DepartureDensity = 0.7f;

        [Header("云海模式（冲入云层）")]
        [Tooltip("★ 远飞阶段在龙前方生成一片云海，龙会冲进去。\n" +
                 "这是「腾云驾雾」手感的关键。")]
        public bool EnableCloudSea = true;

        [Tooltip("云海中心距离龙多远（米）。太小会一开始就撞上，太大会错过。\n" +
                 "建议 60~150。")]
        [Range(10f, 400f)]
        public float CloudSeaDistance = 110f;

        [Tooltip("云海的半径（米）。玩家要能真的「进入」里面。")]
        [Range(20f, 400f)]
        public float CloudSeaRadius = 90f;

        [Tooltip("云海的整体不透明度 0~1。\n" +
                 "随着龙冲入，密度会逐渐增大到黑场。")]
        [Range(0f, 1f)]
        public float CloudSeaOpacity = 0.95f;

        [Tooltip("云海的粒子数量。上一版 500 太薄看不出云，加厚到 900。")]
        [Range(50, 2000)]
        public int CloudSeaCount = 900;

        [Tooltip("★ 关闭一切飞行中的实时生成（推荐）：环境补云、滚动云海、收尾白化云全部停用。\n" +
                 "云雾完全由预撒沿轨迹铺好 —— 云在到达之前就固定在空间里，没有生成过程，就不会在眼前突现。\n" +
                 "关掉后云量完全由 PreWarmClouds（含末段云墙）决定。")]
        public bool DisableRuntimeSpawn = true;

        [Header("收尾云墙（纯预撒）")]
        [Tooltip("黑场前云墙的云团数：在最后 14% 轨迹 + 终点之外一次性铺好。\n" +
                 "数值越大，冲进黑场前的云越厚；全部预先生成，飞行中不会再冒新云。")]
        public int EndingWallCount = 120;

        [Tooltip("云墙向轨迹终点之外延伸的距离（米）。让黑场那一刻相机已经在云里，而不是刚好穿出云层。")]
        public float EndingWallForward = 80f;

        [Tooltip("云墙云团的尺寸倍率（相对普通云）。越大越厚实，少量团就能填满视野。")]
        public float EndingWallSizeMul = 1.35f;

        [Tooltip("★ 云团生成的最小距离（米，相对头显）。小于这个距离的云会被推到外面，\n" +
                 "避免在眼前「凭空冒出来」。近处只保留预撒时就已存在的云。")]
        [Range(10f, 200f)]
        public float MinSpawnDistance = 60f;

        [Tooltip("★ 云的整体不透明度 0~1。Play 中可实时调，改完右键「应用参数」。")]
        [Range(0.2f, 1f)]
        public float CloudOpacity = 0.85f;

        [Header("随机种子（多人一致）")]
        [Tooltip("随机种子。相同种子在所有端生成完全相同的云。\n" +
                 "多人同步时不要改这个值。")]
        public int RandomSeed = 20260922;

        [Header("调试")]
        [Tooltip("在 Scene 视图显示云团生成范围。")]
        public bool ShowGizmos = true;

        // ---------------- 运行时 ----------------

        private ParticleSystem _system;
        /// <summary>★ 远景天际线云独立系统：独立配额不被近景挤占、
        ///   alpha 恒定可见（天际线不参与前薄后厚的大幅渐变）。</summary>
        private ParticleSystem _distantSystem;
        private Material _distantMat;
        private ParticleSystem.Particle[] _alive;
        private int _aliveCount;
        private int _maxParticles;

        /// <summary>
        /// 粒子池大小。按预撒团数兜底：纯预撒模式下云量由 PreWarmClouds 决定，
        /// 若 MaxCloudCount（环境云配额）比它小，池子会静默挤掉最老的云。
        /// </summary>
        private int PoolSize
        {
            get
            {
                int clusters = Mathf.Max(MaxCloudCount, PreWarmClouds)
                             + Mathf.Max(0, EndingWallCount);
                return clusters * ParticlesPerCloud + CloudSeaCount + 200;
            }
        }

        private float _spawnTimer;
        private System.Random _random;
        private FlightDirector _director;
        private Material _cloudMat;

        private void Awake()
        {
            _random = new System.Random(RandomSeed);
            // ★ 必须包含未激活对象：本组件挂在 FlightClouds 上，它在「上龙」那一刻才被激活，
            //   而飞行总控（FlightDirector）所在的 part3/骑龙飞上天空 可能同一帧还没启用。
            //   FindFirstObjectByType 默认只查活动对象 → 返回 null → 预撒云整块逻辑被跳过，
            //   表现就是「天上几乎没有云雾」。
            _director = FindFirstObjectByType<FlightDirector>(FindObjectsInactive.Include);
            SetupParticleSystem();
            TryPreWarm();
        }

        private void SetupParticleSystem()
        {
            Debug.Log("[Cloud] SetupParticleSystem 开始");
            _system = GetComponent<ParticleSystem>();
            if (_system == null)
            {
                _system = gameObject.AddComponent<ParticleSystem>();
            }

            // ★ 总粒子配额 = 环境云团数 × 每团粒子数 + 云海粒子数。
            //   之前 maxParticles=500 而预撒 150团×48粒=7200，
            //   Emit 超配额会挤掉最老的粒子 → 天上只剩 500 个散点。
            _maxParticles = PoolSize;
            _alive = new ParticleSystem.Particle[_maxParticles];

            var main = _system.main;
            main.loop = true;
            // ★ 150s：29s 飞行全程 + 缓冲。之前 60s，预撒的云在
            //   飞行中后段陆续蒸发，远飞段天空被清空。
            main.startLifetime = 150f;
            main.startSpeed = 0f;              // 云本身几乎不动，靠玩家穿过产生相对运动
            main.startSize = CloudSize;
            main.startColor = new Color(1f, 1f, 1f, CloudOpacity);
            main.maxParticles = _maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;  // ★ VR 关键
            main.playOnAwake = true;
            main.gravityModifier = 0f;
            // ★ AlwaysSimulate：VR 大空间里龙会飞出单个相机视锥，
            //   默认 PauseAndClear 会在视锥外【清空全部粒子】——
            //   实测快进后粒子数直接归零就是它干的。
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            // ★★★ 尺寸缩放必须用 Hierarchy（lossyScale=1）。
            //   FlightClouds 物体 localScale=0.0005（父级 part3=1823 抵消），
            //   默认 Local 模式按 localScale 缩 → 38m 云实际渲染 1.9cm——
            //   这就是"满屏非常小的颗粒"的终极根因。
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = _system.emission;
            emission.rateOverTime = 0f;         // 由脚本手动 Emit
            emission.enabled = false;

            // ★ Shape 模块必须禁用：位置全部由 EmitCloud 手工计算。
            //   之前 shape.radius=80 且 Emit 时 applyShapeToPosition=true，
            //   每个粒子落点再被随机偏移 ±80m —— 一团 48 个粒子被
            //   打散成孤立小点，这就是"满屏小颗粒"的根因。
            var shape = _system.shape;
            shape.enabled = false;

            var renderer = _system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // ★ 不要启用碰撞模块，VR 下会严重掉帧。
            //   注意：未启用碰撞时 GetComponent 返回 null，不能直接 .enabled。
            //   CollisionModule 是结构体（ValueType）。
            //   属性返回的是副本，要改字段得先取出来再赋值（CS1612）。
            var collision = _system.collision;
            collision.enabled = false;

            // 用软边贴图做云团（没有指定材质时给个默认的柔和外观）
            if (CloudMaterial == null)
            {
                _cloudMat = CreateDefaultCloudMaterial();
                renderer.sharedMaterial = _cloudMat;
            }
            else
            {
                _cloudMat = CloudMaterial;
                renderer.sharedMaterial = CloudMaterial;
            }

            // 排序与混合设置，让云看起来像雾
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.minParticleSize = 0.001f;
            // ★ 0.6 会把近处大粒子截到视口 60%，导致云"缩水"。
            //   放宽到 1.5，让近距离穿云时粒子保持巨大。
            renderer.maxParticleSize = 1.5f;

            _system.Play();

            // ★ 出生渐入：寿命 150s，把前 2%（约 3 秒）做成 alpha 0→1 的斜坡。
            //   作用只有刚 Emit 出来的粒子 —— 预撒的老云早已在满不透明段，
            //   所以飞行中新增的云是「淡入」，不会在视野里凭空闪现。
            {
                var col = _system.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.02f), new GradientAlphaKey(1f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);
            }

            // ★ 远景天际线云独立系统：独立配额（不与近景/云海互挤）、
            //   独立材质（alpha 恒定可见，仅轻微随进度变浓）。
            if (_distantSystem == null)
            {
                var dgo = new GameObject("DistantClouds");
                dgo.transform.SetParent(transform, false);
                _distantSystem = dgo.AddComponent<ParticleSystem>();
                var dmain = _distantSystem.main;
                dmain.loop = false;
                dmain.startLifetime = 300f;              // 天际线恒存
                dmain.startSpeed = 0f;
                dmain.startSize = CloudSize * DistantSizeMul;
                dmain.startColor = new Color(1f, 1f, 1f, CloudOpacity * 0.8f);
                dmain.maxParticles =
                    Mathf.Max(60, DistantCloudCount * ParticlesPerCloud);
                dmain.simulationSpace = ParticleSystemSimulationSpace.World;
                dmain.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                var dem = _distantSystem.emission;
                dem.rateOverTime = 0f;
                dem.enabled = false;
                var dsh = _distantSystem.shape;
                dsh.enabled = false;
                var dr = _distantSystem.GetComponent<ParticleSystemRenderer>();
                dr.renderMode = ParticleSystemRenderMode.Billboard;
                dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                dr.sortMode = ParticleSystemSortMode.Distance;
                dr.minParticleSize = 0.0001f;
                dr.maxParticleSize = 5f;                 // 远景大团不裁剪
                _distantMat = new Material(_cloudMat);
                _distantMat.name = "M_FlightCloud_Distant";
                dr.sharedMaterial = _distantMat;

                // ★ 必须显式 Play：AddComponent 出来的 ParticleSystem 停在 Stopped 状态，
                //   在停止的系统上 Emit() 会被直接丢弃 —— 远景云整套流程里粒子数恒为 0，
                //   天空只剩玩家周围一小圈云。
                _distantSystem.Play();

                // 远景同样做出生渐入（寿命 300s，前 1% ≈ 3 秒）
                {
                    var dcol = _distantSystem.colorOverLifetime;
                    dcol.enabled = true;
                    var dg = new Gradient();
                    dg.SetKeys(
                        new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.01f), new GradientAlphaKey(1f, 1f) });
                    dcol.color = new ParticleSystem.MinMaxGradient(dg);
                }
            }

            Debug.Log($"[Cloud] ✅ 初始化完成：maxParticles={_system.main.maxParticles} " +
                      $"simulationSpace={_system.main.simulationSpace} " +
                      $"material={(renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "NULL")} " +
                      $"FlightCenter={(FlightCenter != null ? FlightCenter.name : "NULL")}");
        }

        private float _cloudSeaSpawned;
        private float _seaTimer;
        private float _cloudFade = 1f;
        /// <summary>★ 前薄后厚全局淡入因子：起飞段 ≈0.08 → 后段 1.0。</summary>
        private float _globalFade = 0.08f;

        /// <summary>
        /// ★ 统一写材质透明度：CloudOpacity × 前薄后厚 × 云海冲入。
        ///   之前多处各自写材质 alpha 互相打架（Ending 卡死 0.25）。
        /// </summary>
        private void ApplyMaterialAlpha()
        {
            Material m = CloudMaterial != null ? CloudMaterial : _cloudMat;
            if (m != null && m.HasProperty("_BaseColor"))
            {
                Color c = m.color;
                c.a = Mathf.Clamp01(CloudOpacity * _globalFade * _cloudFade);
                m.color = c;
            }
        }

        /// <summary>
        /// ★ 前薄后厚（材质侧）：随飞行进度把云雾从峰值 12% 淡入到 100%。
        ///   用户要求：初始云雾 ≈ 最浓阶段的 10% 左右，之后逐渐增加。
        ///   70% 进度处达到全浓；与预撒密度梯度（40%/70%/100%）配合，
        ///   起飞段综合观感 ≈ 峰值的 10%。
        /// </summary>
        private void HandleGlobalFade()
        {
            if (_director == null || _director.Keyframes == null) return;
            float total = Mathf.Max(0.01f, _director.Keyframes.totalTime);
            float p = Mathf.Clamp01(_director.KeyframeTime / total);
            // ★ 统一取样（滑杆/曲线/自动）。滑杆与曲线模式直接赋值
            //   （拖动立即生效）；自动模式保留 MoveTowards 平滑。
            float target = SampleNearDensity(p);
            bool direct = 使用滑杆控制 || UseExternalCurve;
            _globalFade = direct
                ? target
                : Mathf.MoveTowards(_globalFade, target,
                                    Time.deltaTime * 0.5f);
            ApplyMaterialAlpha();

            // ★ 远景天际线：恒定可见，仅 0.55→0.9 倍轻微渐浓——
            //   远处天际线不参与近景的前薄后厚大幅渐变。
            if (_distantMat != null && _distantMat.HasProperty("_BaseColor"))
            {
                var dc = _distantMat.color;
                dc.a = Mathf.Clamp01(
                    CloudOpacity * SampleDistantDensity(p));
                _distantMat.color = dc;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// ★ 编辑模式云预览：右键组件标题 → "编辑模式预览云"。
        ///   沿轨迹模拟 8 秒的撒布结果，Scene 视图直接可见——
        ///   调 MaxCloudCount / CloudSize / 贴图 后点一下即可刷新预览。
        /// </summary>
        [ContextMenu("编辑模式预览云（Emit 预览）")]
        public void EditorPreviewClouds()
        {
            // ★ 粒子由代码 Emit 驱动（rateOverTime=0），
            //   Simulate() 对它无效——必须走 EmitCloud。
            if (_system == null)
            {
                _system = GetComponent<ParticleSystem>();
                if (_system == null)
                {
                    SetupParticleSystem();
                }
            }
            if (_director == null)
            {
                _director = GetComponent<FlightDirector>();
            }
            // 每次预览都清空重撒（弧长缓存也重建）
            _system.Clear(true);
            _arcPoints = null;
            _arcProgress = null;
            _cloudSeaSpawned = 0f;
            _maxParticles = PoolSize;
            var main = _system.main;
            main.maxParticles = _maxParticles;
            main.startLifetime = 150f;
            for (int i = 0; i < PreWarmClouds; i++)
            {
                EmitCloud(1, spreadAcrossTrack: true);
            }
            for (int i = 0; i < DistantCloudCount; i++)
            {
                EmitCloud(1, spreadAcrossTrack: true, distant: true);
            }
            Debug.Log($"[Cloud] 编辑模式预览：近景 {PreWarmClouds} 团 + 远景 " +
                      $"{DistantCloudCount} 团（粒子数={_system.particleCount}）", this);
        }

#endif

        /// <summary>
        /// ★ 清空并按当前参数重新撒云（改 CloudSize / MaxCloudCount /
        ///   ParticlesPerCloud 后用这个，比"应用参数"更彻底——
        ///   存量粒子尺寸随机分布，直接改 startSize 会破坏蓬松感）。
        /// </summary>
        [ContextMenu("重新撒云（清空+预撒）")]
        public void RespawnClouds()
        {
            if (_system == null)
            {
                _system = GetComponent<ParticleSystem>();
            }
            _system.Clear(true);
            _arcPoints = null;
            _arcProgress = null;
            _cloudSeaSpawned = 0f;
            _seaTimer = 0f;
            _prewarmed = false;
            // ★ 重新取一次路线偏移（改完参数重撒时飞行总控可能已重新起飞）
            _routeOffsetResolved = false;
            _routeOffsetValid = false;
            // ★ 复位渐浓因子：循环体验从"前薄"重新开始
            _globalFade = SampleNearDensity(0f);
            _cloudFade = 1f;
            ApplyMaterialAlpha();
            _maxParticles = PoolSize;
            _alive = new ParticleSystem.Particle[_maxParticles];
            var main = _system.main;
            main.maxParticles = _maxParticles;
            main.startSize = CloudSize;
            main.startColor = new Color(1f, 1f, 1f, CloudOpacity);
            Debug.Log($"[Cloud] 重新撒云：maxParticles={_maxParticles}", this);
        }


#if UNITY_EDITOR
        /// <summary>
        /// ★ Play 中实时调参：改 Inspector 的 CloudSize / MaxCloudCount /
        ///   CloudOpacity 后右键执行，立即生效（不用重启 Play）。
        /// </summary>
        [ContextMenu("应用 Inspector 参数（实时）")]
        public void ApplyInspectorParams()
        {
            if (_system == null)
            {
                _system = GetComponent<ParticleSystem>();
            }
            var main = _system.main;
            main.maxParticles = PoolSize;
            main.startColor = new Color(1f, 1f, 1f, CloudOpacity);
            // ★ 存量粒子只调 alpha，不动 startSize——
            //   尺寸是 55%~100% 随机分布的，统一重设会破坏蓬松感。
            //   改 CloudSize 请用"重新撒云"。
            var alive = new ParticleSystem.Particle[_system.main.maxParticles];
            int n = _system.GetParticles(alive);
            for (int i = 0; i < n; i++)
            {
                Color32 c = alive[i].startColor;
                alive[i].startColor = new Color32(255, 255, 255,
                                                  (byte)(CloudOpacity * 255f));
            }
            _system.SetParticles(alive, n);
            Debug.Log($"[Cloud] 参数已应用: opacity={CloudOpacity} " +
                      $"max={MaxCloudCount * ParticlesPerCloud + CloudSeaCount}" +
                      $"（改尺寸请用「重新撒云」）", this);
        }

#endif

        private void Update()
        {
            // ★ 启动预撒：一次性沿全轨迹撒云（之前逐帧慢慢撒，
            //   前十几秒天空空荡荡，用户反馈"只有一点粒子"）
            TryPreWarm();

            HandleCloudSea();
            HandleEndingWhiteout();
            HandleAmbientClouds();
            HandleGlobalFade();
        }

        // ---------------- 收尾「冲进云里」 ----------------
        private int _whiteoutClusters;
        private bool _whiteoutStarted;
        private float _whiteoutTimer;

        /// <summary>
        /// 黑场前的几秒：在相机周围快速铺满云团，让视野被云完全包住再黑场。
        /// 这一段是刻意的「冲进云层」，所以不受 MinSpawnDistance / 关闭实时补云的限制。
        /// </summary>
        private void HandleEndingWhiteout()
        {
            // ★ 纯预撒模式：收尾不临时喷云。原来这里会在黑场前 1~2 秒
            //   往相机四周 Emit 上百团云（并关掉出生渐入），看起来就是
            //   「黑场前瞬间冒出一大堆云」；收尾的云墙由预撒铺好。
            if (DisableRuntimeSpawn) return;

            if (_director == null) return;
            if (_director.CurrentPhase != FlightPhase.Ending) return;

            // ★ 收尾阶段关掉「出生渐入」：白化要立刻生效，
            //   否则新云还在 3 秒的 alpha 斜坡里，黑场前根本来不及变白。
            if (!_whiteoutStarted)
            {
                _whiteoutStarted = true;
                var col = _system.colorOverLifetime;
                if (col.enabled) col.enabled = false;

                // ★ 粒子池已接近上限，不扩容的话 Emit 会被直接丢弃（实测只进得去 52 颗），
                //   白化根本铺不出来。这里临时把上限抬高，专门给收尾这一段用。
                var m = _system.main;
                m.maxParticles = _maxParticles + 4000;
            }

            _whiteoutTimer += Time.deltaTime;
            if (_whiteoutTimer < 0.08f) return;
            _whiteoutTimer = 0f;

            var cam = Camera.main;
            if (cam == null) return;
            Vector3 origin = cam.transform.position;
            Vector3 fwd = cam.transform.forward;

            for (int i = 0; i < 4; i++)
            {
                float ahead = Mathf.Lerp(6f, 34f, (float)_random.NextDouble());
                float side = Mathf.Lerp(-22f, 22f, (float)_random.NextDouble());
                float up = Mathf.Lerp(-14f, 14f, (float)_random.NextDouble());
                Vector3 center = origin + fwd * ahead
                               + cam.transform.right * side
                               + Vector3.up * up;

                float clusterR = CloudSize * 0.42f;
                Color col = new Color(1f, 1f, 1f, CloudOpacity);
                for (int k = 0; k < ParticlesPerCloud; k++)
                {
                    float rr = clusterR * Mathf.Pow((float)_random.NextDouble(), 0.5f);
                    float a = (float)_random.NextDouble() * Mathf.PI * 2f;
                    float b = Mathf.Acos(2f * (float)_random.NextDouble() - 1f);
                    Vector3 off = new Vector3(
                        rr * Mathf.Sin(b) * Mathf.Cos(a),
                        rr * Mathf.Cos(b) * 0.55f,
                        rr * Mathf.Sin(b) * Mathf.Sin(a));

                    _system.Emit(new ParticleSystem.EmitParams
                    {
                        position = center + off,
                        startSize = CloudSize * 1.25f * Mathf.Lerp(0.6f, 1f, (float)_random.NextDouble()),
                        startColor = col,
                        applyShapeToPosition = false
                    }, 1);
                }
                _whiteoutClusters++;
                if (_whiteoutClusters > 120) return;
            }
        }

        private bool _prewarmed;

        // ---------------- 路线起点对齐 ----------------
        // ★ 预撒必须套用 FlightDirector 起飞时记录的「路线偏移」：
        //   K 帧表是烘焙时的绝对坐标，实际飞行会把整条轨迹搬到骑龙
        //   起飞那一刻的位姿（平移 + 偏航）。若预撒直接用原始表坐标，
        //   云会整段偏离真实航线——实测黑场前正前方晴空万里，
        //   云墙全落在了别处。
        //   另外预撒不能在上龙瞬间就做：Board() 激活云雾后，
        //   MountNextFrame 下一帧才把骑龙对齐到开场龙头，此时位姿还不是起飞位姿。
        private bool _routeOffsetResolved;
        private bool _routeOffsetValid;
        private Vector3 _routeOrigin;
        private Vector3 _routeRefPos;
        private float _routeYawOffset;

        private bool TryResolveRouteOffset()
        {
            if (_routeOffsetResolved) return true;
            if (_director == null) return false;

            var t = _director.GetType();
            var fReady = t.GetField("_routeOffsetReady",
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);
            if (fReady == null)
            {
                // 旧版总控没有路线偏移 → 直接按原始坐标撒
                _routeOffsetResolved = true;
                return true;
            }
            if (!(bool)fReady.GetValue(_director)) return false;   // 还没起飞

            var fOrigin = t.GetField("_routeOrigin",
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);
            var fRef = t.GetField("_routeRefPos",
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);
            var fYaw = t.GetField("_routeYawOffset",
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);
            if (fOrigin == null || fRef == null || fYaw == null)
            {
                _routeOffsetResolved = true;
                return true;
            }

            _routeOrigin = (Vector3)fOrigin.GetValue(_director);
            _routeRefPos = (Vector3)fRef.GetValue(_director);
            _routeYawOffset = (float)fYaw.GetValue(_director);
            _routeOffsetValid = true;
            _routeOffsetResolved = true;
            Debug.Log($"[Cloud] 预撒套用路线偏移：起点 {_routeOrigin:F1}，" +
                      $"yaw 偏移 {_routeYawOffset:F1}°", this);
            return true;
        }

        /// <summary>把 K 帧表的原始坐标映射到实际飞行世界坐标。</summary>
        private Vector3 RouteToWorld(Vector3 raw)
        {
            if (!_routeOffsetValid) return raw;
            return _routeOrigin
                 + Quaternion.Euler(0f, _routeYawOffset, 0f) * (raw - _routeRefPos);
        }

        /// <summary>
        /// 一次性预撒：近景云沿全轨迹按弧长排布 + 远景天际线云。
        /// 需要飞行总控才能拿到 K 帧轨迹，找不到就下一帧再试（不要直接放弃）。
        /// </summary>
        private void TryPreWarm()
        {
            if (_prewarmed || _system == null) return;
            if (_director == null)
                _director = FindFirstObjectByType<FlightDirector>(FindObjectsInactive.Include);
            if (_director == null) return;

            // ★ Play 中必须等飞行总控捕获「路线原点」后再撒（见上面说明）；
            //   编辑模式预览没有起飞流程，按原始坐标撒即可。
            if (Application.isPlaying && !TryResolveRouteOffset()) return;

            _prewarmed = true;
            // ★ 近景云按「飞行进度」加权铺开，而不是均匀铺：
            //   p = u^0.45 会把采样压向后段（前段疏、后段密）；
            //   再额外把 30% 的团压在最后 12% 进度上，形成黑场前的云墙。
            // 末段轻云（原 30% → 15%）：只在最后 10% 铺一层薄的，
            // 真正的「遮盖视野」交给下面的收尾云墙，避免两处叠加导致后段过密。
            int wallCount = Mathf.RoundToInt(PreWarmClouds * 0.15f);
            int spreadCount = Mathf.Max(0, PreWarmClouds - wallCount);
            for (int i = 0; i < spreadCount; i++)
            {
                float p = Mathf.Pow((float)_random.NextDouble(), 0.45f);
                EmitCloudAtProgress(p, 6f, SpawnRadius * 0.5f);
            }
            for (int i = 0; i < wallCount; i++)
            {
                float p = Mathf.Lerp(0.90f, 1f, (float)_random.NextDouble());
                // 云墙贴得近一些，并延伸过终点 ~40m：黑场那一刻相机是在云里，
                // 而不是刚好穿出云层。全部在进入段落时一次性铺好，之后不再生成。
                float ahead = Mathf.Lerp(0f, 40f, (float)_random.NextDouble());
                EmitCloudAtProgress(p, 2f, SpawnRadius * 0.28f, ahead);
            }
            // ★ 收尾云墙（加厚）：原来"冲进云里"靠 Ending 阶段每 0.08s 在相机前
            //   实时喷云，会在黑场前瞬间冒出一大片。现在把同样的云量提前摆在
            //   最后 14% 轨迹 + 终点之外 80m 的空间里，飞行中不再生成任何粒子。
            int wrapWall = Mathf.Max(0, EndingWallCount);
            for (int i = 0; i < wrapWall; i++)
            {
                float p = Mathf.Lerp(0.85f, 1f, (float)_random.NextDouble());
                float ahead = Mathf.Lerp(0f, Mathf.Max(0f, EndingWallForward),
                                         Mathf.Pow((float)_random.NextDouble(), 0.8f));
                EmitCloudAtProgress(p, 0f, SpawnRadius * 0.45f, ahead, EndingWallSizeMul);
            }
            // ★ 远景天际线云：外围环带大尺寸云团，
            //   消除"只有玩家周围有云、远处天空空荡"的出戏感
            if (DistantCloudCount > 0)
            {
                if (_distantSystem != null && !_distantSystem.isPlaying) _distantSystem.Play();
                for (int i = 0; i < DistantCloudCount; i++)
                {
                    EmitCloud(1, spreadAcrossTrack: true, distant: true);
                }
            }
            if (PreWarmClouds > 0 || DistantCloudCount > 0)
            {
                Debug.Log($"[Cloud] ✅ 预撒近景 {PreWarmClouds} 团 + 远景 " +
                          $"{DistantCloudCount} 团（近景粒子={_system.particleCount} " +
                          $"远景粒子={(_distantSystem != null ? _distantSystem.particleCount : -1)}）", this);
            }
        }

        /// <summary>
        /// 在指定【飞行进度】处撒一团云。预撒专用：可按进度精确控制密度分布
        /// （EmitCloud 内部是随机取弧长点，做不出"前疏后密"）。
        /// </summary>
        private void EmitCloudAtProgress(float p, float lateralMin, float lateralMax,
                                         float forwardOffset = 0f, float sizeMul = 1f)
        {
            if (_arcPoints == null || _arcPoints.Count == 0) BuildArcPoints();
            if (_arcPoints == null || _arcPoints.Count == 0) return;

            int idx = Mathf.Clamp(
                Mathf.RoundToInt(Mathf.Clamp01(p) * (_arcPoints.Count - 1)),
                0, _arcPoints.Count - 1);
            Vector3 basePos = _arcPoints[idx];
            if (forwardOffset != 0f)
            {
                // 沿末段切线往前铺：收尾云墙要盖住轨迹终点之外，
                // 否则龙会在黑场前几秒「飞出云层」、前方只剩晴空。
                int prev = Mathf.Max(0, idx - 1);
                Vector3 tan = basePos - _arcPoints[prev];
                tan.y = 0f;
                if (tan.sqrMagnitude < 0.0001f) tan = Vector3.forward;
                basePos += tan.normalized * forwardOffset;
            }

            float ang = (float)_random.NextDouble() * Mathf.PI * 2f;
            float dist = Mathf.Lerp(lateralMin, lateralMax, (float)_random.NextDouble());
            float h = Mathf.Lerp(HeightRangeMin, HeightRangeMax, (float)_random.NextDouble());
            Vector3 center = basePos + new Vector3(
                Mathf.Cos(ang) * dist, h, Mathf.Sin(ang) * dist);

            float clusterR = CloudSize * 0.30f;
            Color col = new Color(1f, 1f, 1f, CloudOpacity);
            for (int i = 0; i < ParticlesPerCloud; i++)
            {
                float rr = clusterR * Mathf.Pow((float)_random.NextDouble(), 0.5f);
                float a = (float)_random.NextDouble() * Mathf.PI * 2f;
                float b = Mathf.Acos(2f * (float)_random.NextDouble() - 1f);
                Vector3 off = new Vector3(
                    rr * Mathf.Sin(b) * Mathf.Cos(a),
                    rr * Mathf.Cos(b) * 0.55f,
                    rr * Mathf.Sin(b) * Mathf.Sin(a));

                _system.Emit(new ParticleSystem.EmitParams
                {
                    position = center + off,
                    startSize = CloudSize * sizeMul
                              * Mathf.Lerp(0.55f, 1f, (float)_random.NextDouble()),
                    startColor = col,
                    applyShapeToPosition = false
                }, 1);
            }
        }

        /// <summary>
        /// ★ 弧长均匀采样点缓存：沿 K 帧表每 PreWarmArcStep 米取一个点。
        ///   按时间随机采样的坑：环绕段 30 秒绕小圈 → 云全挤在圈上；
        ///   远飞段同是几十秒却是上公里直线 → 几乎无云。
        ///   按弧长采样后全程空间密度一致。
        /// </summary>
        private System.Collections.Generic.List<Vector3> _arcPoints;
        /// <summary>与 _arcPoints 一一对应的轨迹进度（0~1），做前薄后厚梯度用。</summary>
        private System.Collections.Generic.List<float> _arcProgress;

        private void BuildArcPoints()
        {
            _arcPoints = new System.Collections.Generic.List<Vector3>(256);
            _arcProgress = new System.Collections.Generic.List<float>(256);
            var frames = _director.Keyframes.frames;
            if (frames == null || frames.Count < 2) return;
            float total = Mathf.Max(0.01f, _director.Keyframes.totalTime);
            float step = Mathf.Max(5f, PreWarmArcStep);
            float acc = step;   // 强制首点附近也有一个
            _arcPoints.Add(RouteToWorld(frames[0].pos));
            _arcProgress.Add(frames[0].time / total);
            for (int i = 1; i < frames.Count; i++)
            {
                acc += UnityEngine.Vector3.Distance(frames[i - 1].pos,
                                                    frames[i].pos);
                if (acc >= step)
                {
                    _arcPoints.Add(RouteToWorld(frames[i].pos));
                    _arcProgress.Add(frames[i].time / total);
                    acc = 0f;
                }
            }
            Debug.Log($"[Cloud] 弧长采样点: {_arcPoints.Count} 个 " +
                      $"（每 {step:F0}m 一点，轨迹 {frames.Count} 帧）", this);
        }

        /// <summary>
        /// ★ 前薄后厚：按轨迹进度决定这团云是否保留。
        ///   起飞段（前 25%）只留 40%，中段 70%，后段全保——
        ///   配合材质淡入（初始 12%），起飞段综合观感约为峰值 10%。
        /// </summary>
        private readonly float[] _sliderNear = new float[6];
        private readonly float[] _sliderDistant = new float[6];

        /// <summary>★ 六点平滑插值取样（p: 0~1 → 滑杆数组）。</summary>
        private static float SampleSix(float p, float[] v)
        {
            p = Mathf.Clamp01(p);
            if (p <= 0f) return v[0];
            if (p >= 1f) return v[5];
            float seg = p * 5f;
            int i = Mathf.Min(4, (int)seg);
            float t = seg - i;
            t = t * t * (3f - 2f * t);   // smoothstep 过渡
            return Mathf.Lerp(v[i], v[i + 1], t);
        }

        /// <summary>
        /// ★ 近景密度统一取样入口（优先级：滑杆 > 曲线 > 自动公式）。
        ///   返回值同时决定：预撒/补云概率 与 材质透明度。
        /// </summary>
        private float SampleNearDensity(float p)
        {
            if (使用滑杆控制)
            {
                _sliderNear[0] = 密度_起飞滑跑;
                _sliderNear[1] = 密度_升空爬升;
                _sliderNear[2] = 密度_环绕前段;
                _sliderNear[3] = 密度_环绕后段;
                _sliderNear[4] = 密度_远飞冲刺;
                _sliderNear[5] = 密度_黑场之前;
                return SampleSix(p, _sliderNear);
            }
            if (UseExternalCurve)
            {
                return Mathf.Clamp01(DensityCurve.Evaluate(p));
            }
            return Mathf.Lerp(0.12f, 1f, Mathf.SmoothStep(0.02f, 0.70f, p));
        }

        /// <summary>★ 远景天际线密度统一取样入口（同上优先级）。</summary>
        private float SampleDistantDensity(float p)
        {
            if (使用滑杆控制)
            {
                _sliderDistant[0] = 远景_起飞滑跑;
                _sliderDistant[1] = 远景_升空爬升;
                _sliderDistant[2] = 远景_环绕前段;
                _sliderDistant[3] = 远景_环绕后段;
                _sliderDistant[4] = 远景_远飞冲刺;
                _sliderDistant[5] = 远景_黑场之前;
                return SampleSix(p, _sliderDistant);
            }
            if (UseExternalCurve)
            {
                return Mathf.Clamp01(DistantCurve.Evaluate(p));
            }
            return Mathf.Lerp(0.25f, 0.75f, p);
        }

        private float DensityGate(float progress01)
        {
            // ★ 统一取样（滑杆/曲线/自动），值即该进度的保留概率
            float keep = SampleNearDensity(progress01);
            return ((float)_random.NextDouble() <= keep) ? 1f : 0f;
        }

        /// <summary>
        /// 云海：远飞阶段在龙前方生成一大片云，龙冲进去后逐渐被吞没。
        /// </summary>
        private void HandleCloudSea()
        {
            // ★ 纯预撒模式：云海也不实时生成。滚动云海是在龙前方 119m 处
            //   一次次凭空 Emit 出来的（0.35s 一批），在后半段看起来就是
            //   「云在眼前浮现」；收尾的云墙改由预撒铺好（见 TryPreWarm）。
            if (DisableRuntimeSpawn) return;

            if (!EnableCloudSea || _system == null || _director == null)
            {
                return;
            }

            // ★ 云海阶段：Departure 全程 + Ending（黑场前）——
            //   旧逻辑只在进入 Departure 时一次性撒 900 粒（1 秒内发完），
            //   龙速度快、很快穿过那坨固定云海，黑场前前方几百米全空。
            //   新逻辑：滚动云墙——持续在龙前方生成小批云团，
            //   云海粒子短寿命（18s）自然消散，形成跟随龙前进的云墙。
            bool seaPhase = _director.CurrentPhase == FlightPhase.Departure
                         || (_director.CurrentPhase == FlightPhase.Ending
                             && _director.KeyframeTime
                                < _director.Keyframes.totalTime - 0.3f);

            if (!seaPhase)
            {
                // 非云海阶段：云海因子恢复常态
                if (_cloudFade < 0.999f)
                {
                    _cloudFade = 1f;
                    ApplyMaterialAlpha();
                }
                return;
            }

            // 阶段进度：进入云层后逐渐加大不透明度
            float prog = _director.CurrentProgress;
            float entryPoint = _director.CloudEntryPoint;
            float entry = _director.CurrentPhase == FlightPhase.Ending
                ? 1f     // Ending：保持全浓冲入黑场
                : Mathf.Clamp01(
                    (prog - entryPoint) / Mathf.Max(0.01f, 1f - entryPoint));

            // 密度渐增：冲入云中后整体变浓。
            // ★ 下限用 CloudOpacity（当前环境云浓度）而非旧版 0.25——
            //   旧值是 56s 长轨迹"远观云海很淡"的设计；现在 29s 轨迹
            //   一进 Departure 就把云拉淡，冲入云海反而变透明。
            float targetAlpha = Mathf.Lerp(CloudOpacity, CloudSeaOpacity,
                                            Mathf.SmoothStep(0f, 1f, entry));
            _cloudFade = Mathf.MoveTowards(_cloudFade, targetAlpha, Time.deltaTime * 0.8f);
            ApplyMaterialAlpha();

            // ★ 滚动生成：每 0.4s 在龙前方补一小批（12 粒 ≈ 30 粒/s），
            //   18s 寿命 → 稳态约 540 粒，在云海配额内。
            //   粒子池接近满时跳过（环境云优先用池）。
            _seaTimer += Time.deltaTime;
            if (_seaTimer >= 0.35f)
            {
                _seaTimer = 0f;
                if (_system.particleCount < _maxParticles - 60)
                {
                    EmitCloudSea(20);
                }
            }
        }

        /// <summary>
        /// 在龙的前方生成云团，形成一片可以「冲进去」的云海。
        /// ★ 同样抱团：每 12 个粒子一个小团，禁用 Shape 偏移。
        /// </summary>
        private void EmitCloudSea(int count)
        {
            Transform center = FlightCenter != null ? FlightCenter : transform;
            // ★ 云海方向用 FlightDirection（机头指向的飞行方向），
            //   而不是根 forward——模型机头天生右偏 103°，
            //   用根 forward 云海会生成在机头右侧。
            Vector3 fwd = _director != null
                ? _director.FlightDirection
                : center.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();

            const int perCluster = 12;   // 每小团粒子数
            int clusters = Mathf.CeilToInt((float)count / perCluster);
            Color col = new Color(1f, 1f, 1f, CloudOpacity);

            for (int c = 0; c < clusters; c++)
            {
                // 团中心：云海球体积内随机分布
                float u = (float)_random.NextDouble();
                float v = (float)_random.NextDouble();
                float w = (float)_random.NextDouble();

                float theta = u * Mathf.PI * 2f;
                float phi = Mathf.Acos(2f * v - 1f);
                float r = CloudSeaRadius * Mathf.Lerp(0.15f, 1f, Mathf.Pow(w, 0.4f));

                Vector3 seaCenter = center.position + fwd * CloudSeaDistance
                                  + Vector3.up * (FlightAltitudeOffset());
                Vector3 cpos = seaCenter + new Vector3(
                    r * Mathf.Sin(phi) * Mathf.Cos(theta),
                    r * Mathf.Cos(phi) * 0.55f,     // 压扁一点，像云层
                    r * Mathf.Sin(phi) * Mathf.Sin(theta));

                // 团内粒子围绕 cpos 小半径抱团
                float cr = CloudSize * 0.30f;
                for (int i = 0; i < perCluster; i++)
                {
                    float rr = cr * Mathf.Pow((float)_random.NextDouble(), 0.5f);
                    float a = (float)_random.NextDouble() * Mathf.PI * 2f;
                    float b = Mathf.Acos(2f * (float)_random.NextDouble() - 1f);
                    Vector3 off = new Vector3(
                        rr * Mathf.Sin(b) * Mathf.Cos(a),
                        rr * Mathf.Cos(b) * 0.55f,
                        rr * Mathf.Sin(b) * Mathf.Sin(a));

                    _system.Emit(new ParticleSystem.EmitParams
                    {
                        position = cpos + off,
                        startSize = CloudSize
                                  * Mathf.Lerp(0.55f, 1f,
                                               (float)_random.NextDouble()),
                        startColor = col,
                        // ★ 滚动云墙：18s 短寿命——旧云消散、新云持续在
                        //   龙前方生成（环境云用 main 的 150s 长寿命）
                        startLifetime = 20f,
                        applyShapeToPosition = false   // ★ 禁止 Shape 再打散
                    }, 1);
                }
            }
        }

        private float FlightAltitudeOffset()
        {
            return 0f;
        }

        /// <summary>
        /// 常规环境云（制造穿越感与速度感）。
        ///
        /// ★★★ 本次重写：沿【轨迹前方】撒云，而不是以龙为中心的球。
        ///
        ///   旧问题：球状撒云以龙当前位置为中心，远飞 190m 后龙冲出
        ///   云区，后半段完全没有速度感。
        ///
        ///   新做法：采样 K 帧轨迹「未来 2~10 秒」的位置，在轨迹两侧
        ///   5~40m、上下 ±8m 处生成云团。龙全程都在云里穿行，
        ///   云从身边快速掠过 → 持续的速度感（VR 速度感知主要靠
        ///   近处物体的相对运动）。
        ///
        ///   仍用固定种子 System.Random，多端分布一致。
        /// </summary>
        private void HandleAmbientClouds()
        {
            // ★ 默认关闭实时补云：云雾完全由「预撒」铺好。
            //   预撒的云在玩家到达之前就已经在空间里固定位置，
            //   没有任何「生成过程」，因此不可能在眼前突现；
            //   代价是云的分布是固定的，不会随飞行随机变化。
            if (DisableRuntimeSpawn) return;

            _spawnTimer += Time.deltaTime;
            if (_spawnTimer < SpawnInterval) return;
            _spawnTimer = 0f;

            // 环境云只占用自己的配额（团数×每团），给云海留空间——
            // 之前配额互相挤占，粒子池满后飞行中补云全部静默失败。
            if (_system.particleCount >= MaxCloudCount * ParticlesPerCloud)
                return;

            float density = GetPhaseDensity();
            if (density <= 0.01f) return;

            // ★ 每 0.12s 补 1~3 个【云团】（每团自带 ParticlesPerCloud 个粒子）
            // ★ 补云也按当前进度密度门槛筛（曲线/梯度），与预撒一致
            float pNow = (_director != null && _director.Keyframes != null)
                ? Mathf.Clamp01(_director.KeyframeTime /
                    Mathf.Max(0.01f, _director.Keyframes.totalTime))
                : 1f;
            if ((float)_random.NextDouble() > SampleNearDensity(pNow)) return;

            int want = Mathf.Max(1, Mathf.RoundToInt(2f * density));
            EmitCloud(want);
        }

        private float GetPhaseDensity()
        {
            if (_director == null)
            {
                return OrbitDensity;
            }

            switch (_director.CurrentPhase)
            {
                case FlightPhase.TakeOff: return TakeOffDensity;
                case FlightPhase.Orbit: return OrbitDensity;
                case FlightPhase.Departure: return DepartureDensity;
                // ★ 黑场前的收尾段也要继续补云：之前 Ending 返回 0，
                //   预撒云被穿过之后最后几秒附近一片空白。
                case FlightPhase.Ending: return DepartureDensity;
                default: return 0f;
            }
        }

        /// <summary>
        /// 撒 N 个【云团】。一朵云 = 团中心 + ParticlesPerCloud 个粒子
        /// 紧密抱团（中心密、边缘疏），叠出蓬松体积感。
        ///
        /// ★ applyShapeToPosition 必须为 false：位置全部手工计算，
        ///   绝不能让 Shape 模块再随机偏移（打散成小颗粒的根因）。
        ///
        /// spreadAcrossTrack=true 时团中心沿【整条轨迹】均匀采样
        /// （预撒用，铺满全程）；false 时撒在【当前进度未来几秒】
        /// 的路径两侧（飞行中持续补云用）。
        /// </summary>
        private void EmitCloud(int clusterCount, bool spreadAcrossTrack = false,
                               bool distant = false)
        {
            for (int c = 0; c < clusterCount; c++)
            {
                Vector3 center;
                float dist;
                // ★ 远景天际线云：尺寸放大、不受密度门槛筛（天上一直有云）
                float sizeMul = distant ? DistantSizeMul : 1f;
                bool hasTrack = _director != null
                             && _director.Keyframes != null
                             && _director.Keyframes.frames != null
                             && _director.Keyframes.frames.Count > 1;

                if (hasTrack)
                {
                    // 团中心采样（distant=远景环带 / 预撒=弧长均匀 /
                    // 飞行中=未来路径两侧）
                    Vector3 basePos;
                    if (distant)
                    {
                        // ★ 远景团：轨迹弧长点外围环带 150~600m，
                        //   不受 DensityGate 筛（天际线全程有云），
                        //   高度略偏上（浮在山脊上方）。
                        if (_arcPoints == null || _arcPoints.Count == 0)
                        {
                            BuildArcPoints();
                        }
                        if (_arcPoints == null || _arcPoints.Count == 0)
                        {
                            continue;
                        }
                        int idx = (int)((float)_random.NextDouble()
                                        * _arcPoints.Count);
                        basePos = _arcPoints[idx];
                        dist = Mathf.Lerp(DistantMinDist, DistantMaxDist,
                                          (float)_random.NextDouble());
                        float angD = (float)_random.NextDouble() * Mathf.PI * 2f;
                        float hD = Mathf.Lerp(DistantHeightRange.x,
                                              DistantHeightRange.y,
                                              (float)_random.NextDouble());
                        center = basePos + new Vector3(
                            Mathf.Cos(angD) * dist,
                            hD,
                            Mathf.Sin(angD) * dist);
                    }
                    else
                    {
                        if (!spreadAcrossTrack)
                        {
                        // 团中心采样：沿轨迹【弧长均匀】取点 + 按进度概率筛
                        //（前 25% 留 40%、中段 70%、后段全保 → 前薄后厚）
                        if (_arcPoints == null || _arcPoints.Count == 0)
                        {
                            BuildArcPoints();
                        }
                        if (_arcPoints == null || _arcPoints.Count == 0)
                        {
                            continue;   // 无轨迹可用
                        }
                        int idx = (int)((float)_random.NextDouble()
                                        * _arcPoints.Count);
                        // ★ 前薄后厚：进度门槛不过就跳过这团
                        if (DensityGate(_arcProgress[idx]) <= 0f)
                        {
                            continue;
                        }
                        basePos = _arcPoints[idx];
                        dist = Mathf.Lerp(6f, SpawnRadius * 0.5f,
                                          (float)_random.NextDouble());
                    }
                    else
                    {
                        // 近身层 20%：12~25m（贴脸云保持距离感）
                        bool near = (float)_random.NextDouble() < 0.2f;
                        float ahead;
                        if (near)
                        {
                            ahead = 1f + (float)_random.NextDouble() * 2f;
                            dist = Mathf.Lerp(12f, 25f,
                                              (float)_random.NextDouble());
                        }
                        else
                        {
                            ahead = 2f + (float)_random.NextDouble() * 8f;
                            dist = Mathf.Lerp(6f, SpawnRadius * 0.5f,
                                              (float)_random.NextDouble());
                        }
                        float at = Mathf.Min(_director.KeyframeTime + ahead,
                                             _director.Keyframes.totalTime);
                        Vector3 tp; float tyaw, troll;
                        _director.Keyframes.Sample(at, out tp, out tyaw,
                                                   out troll);
                        basePos = RouteToWorld(tp);
                        }

                        // 近景：高度带=相对轨迹的绝对高度偏移（Inspector 可调），
                        // 当前 -7~+9m：云贴着轨迹上下分布（用户确认的原高度）。
                        float ang = (float)_random.NextDouble() * Mathf.PI * 2f;
                        float hOff = Mathf.Lerp(HeightRangeMin, HeightRangeMax,
                                                (float)_random.NextDouble());
                        center = basePos + new Vector3(
                            Mathf.Cos(ang) * dist,
                            hOff,
                            Mathf.Sin(ang) * dist);
                    }
                }
                else
                {
                    // 回退：旧球状分布（无 K 帧表时）
                    float u = (float)_random.NextDouble();
                    float v = (float)_random.NextDouble();
                    float w = (float)_random.NextDouble();
                    float theta = u * Mathf.PI * 2f;
                    float phi = Mathf.Acos(2f * v - 1f);
                    float radius = SpawnRadius * Mathf.Lerp(0.35f, 1f, w);
                    center = (FlightCenter != null ? FlightCenter : transform).position
                             + new Vector3(
                        radius * Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Lerp(HeightRangeMin, HeightRangeMax,
                                   (float)_random.NextDouble()),
                        radius * Mathf.Sin(phi) * Mathf.Sin(theta));
                }

                // ★ 太近的云会在眼前「凭空冒出来」：
                //   环境云原来的横向偏移是 Lerp(6, SpawnRadius*0.5) → 最近只有 6m，
                //   等于在镜头前生成一团 38m 的云。这里把团心推到最小距离之外，
                //   近处只保留预撒时就已经在那里的云（玩家是「飞进去」而不是「冒出来」）。
                if (!distant)
                {
                    var cam = Camera.main;
                    Vector3 from = cam != null
                        ? cam.transform.position
                        : (FlightCenter != null ? FlightCenter.position : transform.position);
                    Vector3 dd = center - from;
                    float len = dd.magnitude;
                    if (len > 0.01f && len < MinSpawnDistance)
                        center = from + dd / len * MinSpawnDistance;
                }

                // ★ 团内粒子：围绕团中心，30% 半径内分布（pow 让中心更密），
                //   尺寸 55%~100% 随机 —— 多粒叠加才是云，单粒只是孤点。
                //   远景团整体放大 sizeMul 倍（散布+尺寸）。
                float clusterR = CloudSize * 0.30f * sizeMul;
                // 远景团走独立系统与独立 alpha（恒定可见的天际线）
                var targetSystem = distant ? _distantSystem : _system;
                Color col = distant
                    ? new Color(1f, 1f, 1f, CloudOpacity * 0.8f)
                    : new Color(1f, 1f, 1f, CloudOpacity);
                for (int i = 0; i < ParticlesPerCloud; i++)
                {
                    float rr = clusterR
                             * Mathf.Pow((float)_random.NextDouble(), 0.5f);
                    float a = (float)_random.NextDouble() * Mathf.PI * 2f;
                    float b = Mathf.Acos(2f * (float)_random.NextDouble() - 1f);
                    Vector3 off = new Vector3(
                        rr * Mathf.Sin(b) * Mathf.Cos(a),
                        rr * Mathf.Cos(b) * 0.55f,      // 稍压扁，像云不像球
                        rr * Mathf.Sin(b) * Mathf.Sin(a));

                    targetSystem.Emit(new ParticleSystem.EmitParams
                    {
                        position = center + off,
                        startSize = CloudSize * sizeMul
                                  * Mathf.Lerp(0.55f, 1f,
                                               (float)_random.NextDouble()),
                        startColor = col,
                        applyShapeToPosition = false    // ★ 关键：禁止 Shape 再偏移
                    }, 1);
                }
            }
        }

        private void OnDestroy()
        {
            if (_system != null)
            {
                _system.Stop();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!ShowGizmos)
            {
                return;
            }

            Transform center = FlightCenter != null ? FlightCenter : transform;

            Gizmos.color = new Color(0.6f, 0.8f, 1f, 0.25f);
            Gizmos.DrawWireSphere(center.position, SpawnRadius);

            Gizmos.color = new Color(0.6f, 0.8f, 1f, 0.15f);
            Gizmos.DrawWireSphere(center.position + Vector3.up * HeightRangeMin, SpawnRadius * 0.5f);
            Gizmos.DrawWireSphere(center.position + Vector3.up * HeightRangeMax, SpawnRadius * 0.5f);
        }

        /// <summary>
        /// 程序生成一张软边云贴图（径向渐变 + 噪声起伏）。
        /// 这样不依赖外部贴图也能有云的样子。
        /// </summary>
        private Material CreateDefaultCloudMaterial()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.name = "FlightCloudTexture";
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[size * size];
            // 简单的确定性伪随机（保证每次生成的云一样）
            var rng = new System.Random(20260922);
            // 预生成低频噪声
            var offsets = new float[8];
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] = (float)rng.NextDouble() * 100f;
            }

            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 径向距离（中心为 0，边缘为 1）
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // 径向衰减
                    float alpha = Mathf.Clamp01(1f - dist);
                    alpha = alpha * alpha * (3f - 2f * alpha);   // smoothstep

                    // 加一点噪声让边缘不规则
                    float n = Mathf.PerlinNoise(
                        x * 0.08f + offsets[0],
                        y * 0.08f + offsets[1]) * 0.35f
                        + Mathf.PerlinNoise(
                        x * 0.21f + offsets[2],
                        y * 0.21f + offsets[3]) * 0.18f;
                    alpha *= Mathf.Clamp01(0.55f + n);

                    if (dist > 1f) alpha = 0f;

                    byte a = (byte)(Mathf.Clamp01(alpha) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            // 找一个 URP 可用的粒子 Shader
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var mat = new Material(shader) { name = "M_FlightCloud" };

            // ★ 透明混合必须先设置——两种贴图分支都需要
            //   （之前只在"程序贴图"分支里设，指定 CloudTexture 时
            //    提前 return 跳过 → 材质 Opaque → 粒子渲染成白色方块）
            mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);   // 1 = Transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);       // 0 = Alpha
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // ★ 优先用 Inspector 上指定的贴图（Blender 云模型渲染的云朵贴图）
            if (CloudTexture != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", CloudTexture);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", CloudTexture);
                // ★ URP 只认 keyword：SetFloat(_Surface,1) 不开
                //   _SURFACE_TYPE_TRANSPARENT 时 blend 依然走 Opaque——
                //   实测贴脸粒子渲染成贴图透明区的红棕实心方块。
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                Debug.Log($"[Cloud] 使用指定贴图: {CloudTexture.name}（透明混合已配置）");
                return mat;
            }

            Debug.Log("[Cloud] 未指定 CloudTexture，使用程序生成的贴图");
            if (mat != null && mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", tex);
            }
            if (mat != null && mat.HasProperty("_MainTex"))
            {
                mat.SetTexture("_MainTex", tex);
            }

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            return mat;
        }

        /// <summary>
        /// 清空所有云。切场景或重置时用。
        /// </summary>
        public void ClearClouds()
        {
            if (_system != null)
            {
                _system.Clear();
                _aliveCount = 0;
            }
        }
    }
}
