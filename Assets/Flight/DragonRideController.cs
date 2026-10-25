using UnityEngine;

namespace Greatwall.Flight
{
    /// <summary>
    /// 骑乘控制：把玩家 XR Origin 移到龙背座位上。
    ///
    /// 设计要点：
    ///   - 玩家自己的 XR Origin 由本人控制，不需要网络同步
    ///     （Main Camera 上的 NetworkTransformUnreliable 已经处理了位置同步）。
    ///   - 上龙过程用平滑移动，避免瞬移引起眩晕。
    ///   - 下龙时恢复原来的移动系统。
    ///
    /// ★ 接入步骤：
    ///   1. 把本脚本挂到玩家 XR Origin 根物体上。
    ///      ⚠️ 场景里有两套 XR Origin：
    ///        - `XR Origin (XR Rig)`      —— 已禁用（activeSelf=False）
    ///        - `sza_XR Origin (XR Rig)`  —— ★ 启用中，带 XRButtonEvent
    ///      玩家实际用的是 **sza_ 那套**，所以挂它上面。
    ///
    ///   2. `Seat Anchor` → 拖入龙身上的 DragonSeatAnchor
    ///      （建议挂在 `part3/骑龙飞上天空/SKM_Dragon.001` 或其子骨骼）
    ///
    ///   3. `Director` → 拖入 `part3/骑龙飞上天空` 上的 FlightDirector
    ///
    ///   4. `Movement Behaviours To Disable` → 拖入要禁用的移动组件
    ///      （Move 下的 DynamicMoveProvider、Teleportation 下的 TeleportationProvider 等）
    ///      飞行中玩家不该能自己走动。
    ///
    /// ★ 关于两套 XR Origin：
    ///   场景里有 `XR Origin (XR Rig)`（已禁用）和 `sza_XR Origin (XR Rig)`（启用中）。
    ///   玩家实际使用的是 **sza_ 那套**（带 XRButtonEvent），
    ///   所以本脚本应挂在 sza_XR Origin 上。
    /// </summary>
    [DisallowMultipleComponent]
    public class DragonRideController : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("龙背座位锚点。拖入龙身上挂了 DragonSeatAnchor 的物体。")]
        public DragonSeatAnchor SeatAnchor;

        [Tooltip("飞行总控。拖入龙根物体上的 FlightDirector。")]
        public FlightDirector Director;

        [Header("移动控制")]
        [Tooltip("上龙后需要禁用的移动组件（DynamicMoveProvider / TeleportationProvider 等）。\n" +
                 "留空则不处理。飞行中玩家不该能自己走动。")]
        public Behaviour[] MovementBehavioursToDisable;

        [Header("上龙过渡")]
        [Tooltip("★ 瞬移上龙（本次新增）：骑龙触发后直接把玩家放到龙背上，\n" +
                 "龙此时静止悬浮在原地，等人齐才起飞。\n" +
                 "关闭则用下面的平滑过渡（旧模式）。")]
        public bool TeleportOnMount = true;

        [Tooltip("上龙所需时间（秒）。仅平滑模式（TeleportOnMount=off）使用。")]
        [Range(0.3f, 6f)]
        public float MountDuration = 2f;

        [Tooltip("上龙时是否禁用移动组件。建议开启。")]
        public bool DisableMovementOnMount = true;

        [Tooltip("★ 自动扫描禁用：按名字匹配 XR Origin 下的移动组件\n" +
                 "（Move / Teleport / Locomotion / DynamicMove 等），\n" +
                 "与上面的手动列表叠加使用。飞行中玩家完全不能自己走动。")]
        public bool AutoDisableLocomotion = true;

        [Tooltip("★ 每帧防重启用：如果外部脚本重新激活了移动组件，\n" +
                 "自动再次禁用。保证飞行全程锁死移动。")]
        public bool KeepDisabledEveryFrame = true;

        [Tooltip("★ 龙的 Animator 设为 AlwaysAnimate。\n" +
                 "防止 VR 中龙身离开视野后被剔除（CullUpdateTransforms），\n" +
                 "骨骼停更会让座位锚点停摆——这也是座位与龙分离的隐患之一。")]
        public bool ForceAnimatorAlwaysAnimate = true;

        // ---------------- 运行时 ----------------

        private bool _mounted;
        private bool _mounting;
        private float _mountTimer;
        private Vector3 _unmountPosition;
        private Quaternion _unmountRotation;
        private bool[] _behaviourEnabledCache;

        /// <summary>玩家是否已上龙</summary>
        public bool IsMounted => _mounted;

        private void Update()
        {
            if (_mounting)
            {
                UpdateMounting();
            }

            // ★ 飞行全程锁死移动。
            //   XRI 的移动组件（Locomotion/Turn/GrabMove/Teleport 等）在
            //   Play 启动数秒后才初始化完毕，Mount 时刻扫描会扑空——
            //   所以除了帧级强制禁用，还要定期重扫补漏。
            if (_mounted && KeepDisabledEveryFrame && DisableMovementOnMount)
            {
                KeepMovementDisabled();
                _rescanTimer += Time.deltaTime;
                if (_rescanTimer > 0.5f)
                {
                    _rescanTimer = 0f;
                    ScanLocomotionBehaviours();
                    KeepMovementDisabled();
                }
            }
        }

        private float _rescanTimer;

        /// <summary>
        /// ★ 座位跟随必须在 LateUpdate（本次修复 0.4m 一帧滞后）：
        ///   座位挂在 Head 骨骼上，骨骼由 Animator 在 Update 之后的
        ///   动画更新阶段才写入最终位置。跟随若放在 Update，锁到的是
        ///   上一帧的骨骼位置 → 24m/s × 1/60s ≈ 0.4m 的恒定偏差，
        ///   高速下表现为玩家与龙若即若离。
        ///   LateUpdate 在骨骼更新完毕后执行，锁到的就是本帧最终座位。
        /// </summary>
        private void LateUpdate()
        {
            if (!_mounting && _mounted && SeatAnchor != null)
            {
                SeatAnchor.AlignTo(transform);
            }
        }

        /// <summary>名字含这些关键字的组件视为移动/传送组件</summary>
        private static readonly string[] LocomotionKeywords =
            { "Move", "Teleport", "Locomotion", "SnapTurn", "TurnProvider", "Character" };

        private readonly System.Collections.Generic.List<Behaviour> _autoFound
            = new System.Collections.Generic.List<Behaviour>();

        /// <summary>
        /// 自动扫描 XR Origin 下的移动组件（按名字关键字匹配）。
        /// 与 Inspector 手动列表叠加。
        /// </summary>
        private void ScanLocomotionBehaviours()
        {
            _autoFound.Clear();
            if (!AutoDisableLocomotion) return;

            foreach (var b in GetComponentsInChildren<Behaviour>(true))
            {
                if (b == null) continue;
                string n = b.GetType().Name;
                foreach (var kw in LocomotionKeywords)
                {
                    if (n.Contains(kw))
                    {
                        _autoFound.Add(b);
                        break;
                    }
                }
            }
        }

        /// <summary>禁用手动列表 + 自动扫描到的全部移动组件</summary>
        private void KeepMovementDisabled()
        {
            if (MovementBehavioursToDisable != null)
            {
                foreach (var b in MovementBehavioursToDisable)
                {
                    if (b != null && b.enabled) b.enabled = false;
                }
            }
            foreach (var b in _autoFound)
            {
                if (b != null && b.enabled) b.enabled = false;
            }
        }

        /// <summary>
        /// 上龙。由交互、按钮或剧情触发调用。
        ///
        /// ★ 两种模式：
        ///   瞬移（TeleportOnMount=true，默认）——骑龙触发后直接把玩家
        ///   放到龙背座位上。龙此刻静止悬浮在原地（人没齐前不起飞），
        ///   所以瞬移目标位置是确定的，不存在"追着移动的龙瞬移"的问题。
        ///   平滑（TeleportOnMount=false）——旧的缓动过渡。
        /// </summary>
        [ContextMenu("上龙")]
        public void Mount()
        {
            if (_mounted || _mounting || SeatAnchor == null)
            {
                return;
            }

            _unmountPosition = transform.position;
            _unmountRotation = transform.rotation;
            _mounting = true;
            _mountTimer = 0f;

            if (DisableMovementOnMount)
            {
                DisableMovement(true);
            }

            // ★ 自动扫描 + 记录移动组件（含手动列表之外的）
            ScanLocomotionBehaviours();
            if (DisableMovementOnMount)
            {
                KeepMovementDisabled();
            }

            // ★ 龙的 Animator 设为 AlwaysAnimate：
            //   VR 里龙身大部分时间在视野外，默认 CullUpdateTransforms
            //   会让骨骼停更 → 座位锚点停摆 → 玩家与龙分离（隐患）
            if (ForceAnimatorAlwaysAnimate && Director != null)
            {
                var dragonAnim = Director.GetComponent<Animator>();
                if (dragonAnim != null)
                {
                    dragonAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }
            }

            if (TeleportOnMount)
            {
                // ★ 瞬移：一帧完成，随后进入正常的座位跟随
                transform.SetPositionAndRotation(
                    SeatAnchor.SeatWorldPosition,
                    SeatAnchor.SeatWorldRotation);
                _mounting = false;
                _mounted = true;
                SeatAnchor.ResetSmoothing();

                // 通知飞行总控：我已就位（人齐前龙保持静止悬浮）
                if (Director != null)
                {
                    Director.BindPlayerRoot(transform);
                    Director.ReportSeated();
                }

                Debug.Log("[Ride] ⚡ 瞬移上龙完成，移动已锁定，等待其他玩家…", this);
            }
        }

        /// <summary>
        /// 下龙。回到上龙前的位置。
        /// </summary>
        [ContextMenu("下龙")]
        public void Unmount()
        {
            if (!_mounted && !_mounting)
            {
                return;
            }

            _mounted = false;
            _mounting = false;
            SeatAnchor?.ResetSmoothing();

            // 缓慢回到原位，而不是瞬移
            transform.SetPositionAndRotation(_unmountPosition, _unmountRotation);

            if (DisableMovementOnMount)
            {
                DisableMovement(false);
            }
        }

        private void UpdateMounting()
        {
            _mountTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_mountTimer / Mathf.Max(0.01f, MountDuration));
            float eased = t * t * (3f - 2f * t);

            Vector3 seatPos = SeatAnchor.SeatWorldPosition;
            Quaternion seatRot = SeatAnchor.SeatWorldRotation;

            transform.position = Vector3.Lerp(_unmountPosition, seatPos, eased);
            transform.rotation = Quaternion.Slerp(_unmountRotation, seatRot, eased);

            if (t >= 1f)
            {
                _mounting = false;
                _mounted = true;

                // 通知飞行总控：我已就位
                if (Director != null)
                {
                    Director.BindPlayerRoot(transform);
                    Director.ReportSeated();
                }
            }
        }

        private void DisableMovement(bool disable)
        {
            if (MovementBehavioursToDisable == null)
            {
                return;
            }

            if (_behaviourEnabledCache == null ||
                _behaviourEnabledCache.Length != MovementBehavioursToDisable.Length)
            {
                _behaviourEnabledCache = new bool[MovementBehavioursToDisable.Length];
                for (int i = 0; i < MovementBehavioursToDisable.Length; i++)
                {
                    _behaviourEnabledCache[i] = MovementBehavioursToDisable[i] != null
                        && MovementBehavioursToDisable[i].enabled;
                }
            }

            for (int i = 0; i < MovementBehavioursToDisable.Length; i++)
            {
                if (MovementBehavioursToDisable[i] == null)
                {
                    continue;
                }

                MovementBehavioursToDisable[i].enabled =
                    disable ? false : _behaviourEnabledCache[i];
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (SeatAnchor == null)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, SeatAnchor.SeatWorldPosition);
            Gizmos.DrawWireSphere(SeatAnchor.SeatWorldPosition, 0.06f);
        }
    }
}
