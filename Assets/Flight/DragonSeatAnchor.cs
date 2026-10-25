using UnityEngine;

namespace Greatwall.Flight
{
    /// <summary>
    /// 龙背座位锚点。
    ///
    /// ★ 挂载位置（重要）：
    ///   本项目的飞行龙是 `part3/龙环绕飞行6`，不是 `Dragon_root`。
    ///   它的层级是：
    ///     part3 (天幕球)
    ///     └─ 龙环绕飞行6        ← Animator + BigDragonBehavior 在这层
    ///        ├─ SKM_Dragon      (SkinnedMeshRenderer, scale 5.0)
    ///        └─ SKM_Dragon.001  (Transform)
    ///           └─ Head         (22 个子骨骼，骨骼树入口)
    ///
    ///   推荐把本脚本挂在 **SKM_Dragon.001** 或其子骨骼上，
    ///   这样锚点会跟随骨骼动画一起动，座位自然贴合龙背。
    ///
    ///   ★★ 单位说明（重要，别被 scale=5 误导）：
    ///   下面的偏移量用 TransformDirection 计算，它**忽略缩放**，
    ///   所以 Seat Height = 0.7 就是「离骨骼原点 0.7 米」。
    ///   虽然这条龙整体 scale=5，但座位偏移不受影响，单位就是世界米。
    ///
    ///   ★ 手动调整方法：
    ///   1. 选中场景里的 DragonSeat 对象
    ///   2. 在 Inspector 调 Seat Height（上下）/ Seat Forward（前后）/ Seat Left（左右）
    ///   3. Scene 视图里有橙色标记 + 三个箭头，直接看位置对不对
    ///   4. 或者直接拖 DragonSeat 的 Transform 组件（会覆盖上面的参数）
    ///
    ///   经验值（成人坐姿）：
    ///   Seat Height  0.75 ~ 0.85  ← 眼睛离臀部的高度
    ///   Seat Forward -0.5 ~ 0.5   ← 负值靠后，正值靠前（朝龙头）
    ///   Seat Left    0           ← 居中即可
    ///
    /// 用法：
    ///   1. 选中 SKM_Dragon.001（或合适的骨骼），挂上本脚本。
    ///   2. 用 Seat Height / Seat Forward / Seat Left 调座位。
    ///   3. 选中锚点，Scene 视图会显示橙色座位标记与朝向线。
    /// </summary>
    [DisallowMultipleComponent]
    public class DragonSeatAnchor : MonoBehaviour
    {
        [Header("座位位置（单位：米）")]
        [Tooltip("★ 相机高度补偿（米）。\n" +
                 "XR Origin 下有 Camera Offset（本地 Y=1.0），\n" +
                 "所以玩家的眼睛会比 XR Origin 高 1 米。\n" +
                 "座位要放在玩家臀部而非眼睛，\n" +
                 "因此需要把座位下调约 1 米。\n" +
                 "设为 0 表示座位 = 眼睛高度（不建议）。")]
        public float CameraHeightCompensation = 1.0f;
        [Tooltip("★ 上下高度（米）。负值沉入龙背，正值浮在背上。\n"
                 + "成人坐姿参考：0.75 ~ 0.85。")]
        public float SeatHeight = 0.7f;

        [Tooltip("★ 前后偏移（米）。正值向前（朝龙头方向），负值靠后。\n"
                 + "想看清龙身就调负，想看远景就调正。")]
        public float SeatForward = -0.4f;

        [Tooltip("★ 左右偏移（米）。正值向右。居中骑乘填 0。")]
        public float SeatLeft = 0f;

        [Header("座位姿态")]
        [Tooltip("★ 纵向跟随龙头的程度（1.0 = 完全跟随，0 = 不跟随）。\n" +
                 "之前设 0 导致龙抬头低头时视角不动，缺少骑乘的起伏感。\n" +
                 "0.6~0.8 比较自然，太大会晕。")]
        [Range(0f, 1f)]
        public float PitchFollow = 0.7f;
        [Tooltip("★ 俯仰角（度）。第一人称骑乘建议保持 0 —— 俯仰会明显不适。")]
        public float SeatTilt = 0f;

        [Tooltip("朝向角。0 表示面朝龙头方向。")]
        public float SeatYaw = 0f;

        [Header("朝向平滑（位置已硬锁）")]
        [Tooltip("朝向平滑时间占位参数。位置现在每帧硬锁（零滞后），\n" +
                 "只有朝向用角速度限制过滤骨骼抖动（240°/s 上限）。")]
        [Range(0.02f, 1f)]
        public float FollowSmoothTime = 0.18f;

        [Tooltip("朝向平滑开关。开启后玩家视角的旋转被限制角速度，\n" +
                 "过滤 Head 骨骼动画的抖动；位置不受影响（始终硬锁）。")]
        public bool UseSmoothing = true;

        private bool _initialized;

        // ============================================================
        //  Scene 视图可视化
        //  选中考点时能看到座位位置、朝向、以及三个偏移方向，
        //  不用进 Play 就能判断参数对不对。
        // ============================================================

        [Header("Scene 视图辅助")]
        [Tooltip("在 Scene 视图显示座位标记与方向箭头。")]
        public bool ShowGizmos = true;

        [Tooltip("龙背的参考平面（显示为半透明线框），用于判断座位是否骑在背上。")]
        public bool ShowBackPlane = true;

        private void OnDrawGizmos()
        {
            if (!ShowGizmos) return;

            // 座位实际世界位置（与运行时一致）
            Vector3 seatPos = transform.position
                + transform.TransformDirection(
                    new Vector3(SeatLeft, SeatHeight, SeatForward));

            // 朝向：与 SeatWorldRotation 保持一致（只取水平偏航）
            Vector3 fwd = transform.forward;
            Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
            if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(flat.normalized, Vector3.up)
                             * Quaternion.Euler(SeatTilt, SeatYaw, 0f);

            // ---- 座位标记：一个橙色球 ----
            Gizmos.color = new Color(1f, 0.55f, 0f, 1f);
            Gizmos.DrawSphere(seatPos, 0.28f);

            // ---- 朝向箭头（橙黄）----
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 1f);
            Gizmos.DrawRay(seatPos, rot * Vector3.forward * 2.5f);

            // ---- 三个偏移轴（帮助理解参数方向）----
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);   // 绿 = 上下
            Gizmos.DrawRay(transform.position, transform.up * 1.2f);

            Gizmos.color = new Color(0.4f, 0.6f, 1f, 0.9f);   // 蓝 = 前后
            Gizmos.DrawRay(transform.position, transform.forward * 1.2f);

            Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.9f);   // 红 = 左右
            Gizmos.DrawRay(transform.position, transform.right * 1.2f);

            // ---- 骨骼原点（座位偏移的参考点）----
            Gizmos.color = new Color(0.7f, 0.7f, 0.7f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.12f);

            // ---- 龙背参考平面：画一个网格外框，直观看出座位高低 ----
            if (ShowBackPlane)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.8f, 0.5f);
                Vector3 center = transform.position;
                // 3×3 米的面，贴合龙背
                const float s = 1.5f;
                const float y = 0f;
                // 十字线
                Gizmos.DrawLine(center + new Vector3(-s, y, 0), center + new Vector3(s, y, 0));
                Gizmos.DrawLine(center + new Vector3(0, y, -s), center + new Vector3(0, y, s));
                // 外框
                Vector3 a = center + new Vector3(-s, y, -s);
                Vector3 b = center + new Vector3(s, y, -s);
                Vector3 c = center + new Vector3(s, y, s);
                Vector3 d = center + new Vector3(-s, y, s);
                Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c);
                Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);
            }
        }

        /// <summary>
        /// 世界坐标下的座位位置。
        /// 用 TransformDirection 而非 TransformVector，
        /// 这样忽略父级缩放，座位尺寸在 scale=5 的龙身上也能按直觉调节。
        /// </summary>
        public Vector3 SeatWorldPosition
        {
            get
            {
                // ★ 减去相机高度补偿：玩家眼睛 = XR Origin + 1 米，
                //   座位应该对应臀部，所以要下调。
                float h = SeatHeight - CameraHeightCompensation;
                Vector3 localOffset = new Vector3(SeatLeft, h, SeatForward);
                return transform.position + transform.TransformDirection(localOffset);
            }
        }

        /// <summary>
        /// 世界坐标下的座位朝向。
        ///
        /// ★★★ 机体轴取自龙根（本次修复「拉起时视角不上仰」）：
        ///   旧版用锚点自身 transform.forward 算俯仰——但 DragonSeat 挂在
        ///   Head 骨骼下且 localX=64.7°，它的 forward 沿骨骼斜向下方，
        ///   龙体绕 X 抬头 17° 时这个方向向量的 y 分量几乎不变
        ///   （在 cos 顶点附近），实测视角只跟到 3.5°/17.4°。
        ///
        ///   正确做法：俯仰/偏航来自**龙根的机体轴**
        ///   （FlightDirector.transform.forward = 轨迹的 pitch/yaw），
        ///   这才是「龙头指向」。PitchFollow 控制跟随比例（1=与龙头一致）。
        /// </summary>
        public Quaternion SeatWorldRotation
        {
            get
            {
                // 机体轴：龙根 forward（含轨迹 pitch/yaw）
                Vector3 bodyFwd = BodyForward;

                // 只保留水平分量 → 纯偏航
                Vector3 flat = new Vector3(bodyFwd.x, 0f, bodyFwd.z);
                if (flat.sqrMagnitude < 0.0001f)
                {
                    flat = new Vector3(transform.up.x, 0f, transform.up.z);
                    if (flat.sqrMagnitude < 0.0001f) flat = Vector3.forward;
                }

                Quaternion level = Quaternion.LookRotation(flat.normalized, Vector3.up);

                // ★ 俯仰跟随：机体轴俯仰角（抬头为正）× PitchFollow
                float pitch = PitchFollow
                    * Mathf.Asin(Mathf.Clamp(bodyFwd.y, -1f, 1f))
                    * Mathf.Rad2Deg;

                // 低头衰减：抬头全跟，低头只跟 30%（低头看龙身易眩晕）
                if (pitch < 0f) pitch *= 0.3f;

                return level * Quaternion.Euler(pitch + SeatTilt, SeatYaw, 0f);
            }
        }

        /// <summary>
        /// 龙的机体轴（= 当前飞行方向）。运行时缓存 FlightDirector 引用。
        /// ★ 用 FlightDirection（轨迹飞行方向）而不是龙根 forward——
        ///   模型机头天生右偏 103°，龙根 forward 不是机头方向。
        /// 找不到时回退到锚点自身 forward（旧行为）。
        /// </summary>
        private Transform _bodyRoot;
        private Greatwall.Flight.FlightDirector _directorCache;

        private Vector3 BodyForward
        {
            get
            {
                if (_directorCache == null)
                {
                    _directorCache = GetComponentInParent<Greatwall.Flight.FlightDirector>();
                }
                if (_directorCache != null)
                {
                    return _directorCache.FlightDirection;
                }
                if (_bodyRoot == null)
                {
                    // 回退：向上找挂 FlightDirector 的祖先
                    var t = transform;
                    while (t != null && _bodyRoot == null)
                    {
                        if (t.GetComponent<Greatwall.Flight.FlightDirector>() != null)
                            _bodyRoot = t;
                        t = t.parent;
                    }
                }
                return _bodyRoot != null ? _bodyRoot.forward : transform.forward;
            }
        }

        /// <summary>
        /// 把挂载者对齐到座位位置。
        ///
        /// ★★★ 位置硬锁（本次修复「玩家与龙分离」的根因）：
        ///   旧版位置用 SmoothDamp(0.18s) 平滑跟随——平滑跟随存在
        ///   固有滞后 = 速度 × 平滑时间。轨迹提速到 24m/s 后，
        ///   滞后 ≈ 24 × 0.18 ≈ 4.3 米，龙一加速/转弯玩家就飘在
        ///   座位后面好几米，看起来就是「飞着飞着和龙分开了」。
        ///
        ///   正确做法：**位置每帧硬锁**（玩家预期自己坐在龙背上，
        ///   身体随龙动是天经地义，就像坐在真龙背上）；真正需要
        ///   平滑的只有**朝向**（Head 骨骼动画的抖动会直接转动
        ///   玩家视角，这才是 VR 眩晕来源）。
        /// </summary>
        /// <param name="target">要移动并旋转的物体，通常是 XR Origin 根物体</param>
        public void AlignTo(Transform target)
        {
            if (target == null)
            {
                return;
            }

            Vector3 targetPos = SeatWorldPosition;
            Quaternion targetRot = SeatWorldRotation;

            // 位置：零滞后硬锁（不分模式）
            // 朝向：UseSmoothing=true 时限制角速度，过滤骨骼动画抖动
            if (UseSmoothing && _initialized)
            {
                Quaternion cur = target.rotation;
                Quaternion next = Quaternion.RotateTowards(
                    cur, targetRot, 240f * Time.deltaTime);
                // 位置硬锁 + 朝向平滑：位置与朝向分离设置
                target.SetPositionAndRotation(targetPos, next);
            }
            else
            {
                target.SetPositionAndRotation(targetPos, targetRot);
                if (!_initialized)
                {
                    _initialized = true;
                }
            }
        }

        /// <summary>
        /// 重置对齐状态。玩家上龙时应调用，下一帧直接吸附到座位。
        /// </summary>
        public void ResetSmoothing()
        {
            _initialized = false;
        }

        /// <summary>
        /// 场景中在 Inspector 预览座位位置，方便摆放。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Vector3 pos = SeatWorldPosition;
            Gizmos.color = new Color(1f, 0.75f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(pos, 0.08f);

            Quaternion rot = SeatWorldRotation;
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.9f);
            Gizmos.DrawLine(pos, pos + rot * Vector3.forward * 0.35f);
            Gizmos.DrawLine(pos, pos + rot * Vector3.up * 0.12f);
        }
    }
}
