using UnityEngine;

namespace Greatwall.Flight
{
    /// <summary>
    /// 开场盘旋龙。
    ///
    /// 完全独立于骑龙模型：它直接使用「龙环绕飞行3.fbx」自己的骨架与动画，
    /// 与飞行段那套骨架没有任何绑定关系。
    ///
    /// 动画分三段（Opening → FlyForward → Hover）：
    ///   0 ~ 7.43s       盘旋
    ///   7.43 ~ 12.40s   往前飞（身体由头到尾逐节伸直，速度曲线：起步加速→匀速→收尾减速）
    ///   12.40s 之后     原地悬浮 + 轻微摆动（16s 循环）
    ///
    /// 动画自身把盘旋圈心和悬浮龙头放在两个相距约 23m 的局部位置上，
    /// 所以盘旋段用 OrbitRelocate 把圈心搬到取景合适的位置，过渡段再收敛到 0。
    /// 就近景悬浮落点而言，圈心要留在远处（见 OrbitRelocate），否则整圈会压到观众面前。
    /// 根节点从第一帧起就钉在悬浮锚点上，不再有「盘旋点 → 悬浮点」的整体位移。
    /// </summary>
    [DisallowMultipleComponent]
    public class OpeningDragon : MonoBehaviour
    {
        /// <summary>
        /// 骨架局部空间是 FBX 原始 Z-up（与 Unity 世界差 -90°X）。
        /// 动画只驱动骨骼、不动根节点，所以这个校正必须由根节点补上，否则整条龙是仰着的。
        /// </summary>
        private static readonly Quaternion ClipToWorld = Quaternion.Euler(-90f, 0f, 0f);

        [Header("朝向")]
        [Tooltip("绕自身竖直轴的额外旋转（度）。-90 = 龙头朝画面右侧，身体横在城墙上。")]
        public float YawOffset = -90f;

        [Header("悬浮对准锚点的参考点（模型局部米）")]
        [Tooltip("悬浮段落在锚点上的参考点相对根的偏移。已按 YawOffset=-90 换算，龙头世界位置与 -90° 之前保持一致。")]
        public Vector3 HoverCenterOffset = new Vector3(-43.78f, 40.88f, 23.82f);

        [Tooltip("悬浮段龙头的世界位置相对锚点的偏移（模型局部米），用于把龙头精确放到指定点。")]
        public Vector3 HoverHeadOffset = new Vector3(31.14f, -16.46f, -3.0f);

        [Header("盘旋就地化")]
        [Tooltip("盘旋段的整体偏移（模型局部米），悬浮段收敛到 0。悬浮落点前移多少，这里就要反向补多少：否则盘旋圈会跟着龙头一起扑到观众面前。")]
        public Vector3 OrbitRelocate = new Vector3(-61.6833f, 23.2313f, 5.136f);

        [Header("时间")]
        [Tooltip("盘旋段时长（秒）。动画里的盘旋是 7.4333s。")]
        public float CircleDuration = 7.4333f;

        [Tooltip("往前飞（盘旋→悬浮）段时长（秒），要和动画里的 FlyForward 段一致。")]
        public float TransitionDuration = 1.9667f;

        [Tooltip("播放速度倍率。")]
        public float Speed = 1f;

        private Animator _anim;
        private bool _playing;
        private Vector3 _root;
        private float _elapsed;

        /// <summary>当前播放时间（秒）。正常播放由 Speed 推进；调试时可手动设置。</summary>
        public float Elapsed
        {
            get { return _elapsed; }
            set { _elapsed = Mathf.Max(0f, value); }
        }

        private void Awake()
        {
            EnsureAnimator();
        }

        private void EnsureAnimator()
        {
            if (_anim != null) return;
            _anim = GetComponentInChildren<Animator>(true);
            if (_anim != null)
            {
                _anim.applyRootMotion = false;
                _anim.speed = Mathf.Max(0.01f, Speed);
            }
        }

        /// <summary>开始播盘旋（从头播）。</summary>
        public void Play()
        {
            EnsureAnimator();
            gameObject.SetActive(true);
            _playing = true;
            _elapsed = 0f;
            ApplyOrbitOffset(1f);

            if (_anim != null)
            {
                _anim.speed = Mathf.Max(0.01f, Speed);
                _anim.Play("Opening", 0, 0f);
            }
            else
            {
                Debug.LogWarning("[开场龙] 没有找到 Animator，动画不会播放", this);
            }
        }

        /// <summary>直接隐藏。上龙瞬间调用：玩家同一帧被瞬移到龙背，看不到切换。</summary>
        public void Hide()
        {
            _playing = false;
            gameObject.SetActive(false);
        }

        /// <summary>把悬浮参考点对齐到世界锚点，朝向对齐给的 forward。</summary>
        public void AlignCenterTo(Vector3 worldAnchor, Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();

            Quaternion rot = Quaternion.LookRotation(forward, Vector3.up) * ClipToWorld * Quaternion.Euler(0f, 0f, YawOffset);
            _root = worldAnchor - rot * HoverCenterOffset;
            transform.SetPositionAndRotation(_root, rot);
        }

        /// <summary>悬浮段龙头的世界位置相对锚点的偏移。</summary>
        public Vector3 HoverHeadWorldOffset(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            return Quaternion.LookRotation(forward, Vector3.up) * ClipToWorld * Quaternion.Euler(0f, 0f, YawOffset) * HoverHeadOffset;
        }

        /// <summary>把悬浮段龙头对齐到世界点，朝向对齐 forward。</summary>
        public void AlignHoverHeadTo(Vector3 worldHead, Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();

            Quaternion rot = Quaternion.LookRotation(forward, Vector3.up) * ClipToWorld * Quaternion.Euler(0f, 0f, YawOffset);
            AlignCenterTo(worldHead - rot * HoverHeadOffset, forward);
        }

        private void ApplyOrbitOffset(float weight)
        {
            transform.position = _root + transform.rotation * (OrbitRelocate * weight);
        }

        private void LateUpdate()
        {
            if (!_playing) return;

            // 自己计时，不读 Animator 状态：
            //   Opening → FlyForward → Hover 的过渡期间 GetCurrentAnimatorStateInfo 的 length 是 Infinity，
            //   拿它算位置会直接把龙甩到终点。
            _elapsed += Time.deltaTime * Mathf.Max(0f, Speed);
            float circle = Mathf.Max(0.01f, CircleDuration);
            float trans = Mathf.Max(0.01f, TransitionDuration);

            float raw = Mathf.Clamp01((_elapsed - circle) / trans);
            ApplyOrbitOffset(1f - Cruise(raw));
        }

        // 与 Blender 里 build_opening_v9.py 的 prof() 必须保持一致：
        // 起步加速 16% → 匀速 → 收尾减速 22%，返回归一化位移 0..1。
        // 动画自带的前飞位移和这里的 OrbitRelocate 衰减用同一条曲线，
        // 两者在世界空间严格抵消，龙才会沿直线飞、不会走出折线。
        private const float CruiseAccel = 0.16f;
        private const float CruiseDecel = 0.22f;

        private static float Cruise(float u)
        {
            u = Mathf.Clamp01(u);
            float a = CruiseAccel, d = CruiseDecel;
            float norm = 1f - 0.5f * a - 0.5f * d;
            float cum;
            if (u < a)
                cum = 0.5f * u - (a / (2f * Mathf.PI)) * Mathf.Sin(Mathf.PI * u / a);
            else if (u <= 1f - d)
                cum = 0.5f * a + (u - a);
            else
            {
                float s = 1f - u;
                cum = norm - (0.5f * s - (d / (2f * Mathf.PI)) * Mathf.Sin(Mathf.PI * s / d));
            }
            return Mathf.Clamp01(cum / norm);
        }
    }
}
