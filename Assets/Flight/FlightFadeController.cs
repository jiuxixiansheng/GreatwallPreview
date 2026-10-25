using System.Collections;
using UnityEngine;

namespace Greatwall.Flight
{
    /// <summary>
    /// 飞行段落黑场控制器（自足式，不依赖团队 UI 接线）。
    ///
    /// ★ VR 黑场的正确做法（为什么不用 Screen Space Overlay）：
    ///   XR 下 Screen Space Overlay Canvas 不会渲染到头显。
    ///   这里用「跟随相机的世界空间黑幕」：一个黑色 Quad 挂在主相机下，
    ///   距眼睛 0.5m，覆盖整个视场。淡入 = alpha 0→1，
    ///   双目渲染天然一致，且不依赖场景里有任何 Canvas。
    ///
    /// ★ 使用：
    ///   - 场景里放一个空物体挂本脚本即可（运行时自动建黑幕）。
    ///   - FlightDirector 进入 Ending 阶段时自动调用 FadeOut()。
    ///   - 也可手动调用 FadeOut / FadeIn 用于调试或复用。
    /// </summary>
    public class FlightFadeController : MonoBehaviour
    {
        [Header("黑幕外观")]
        [Tooltip("黑幕颜色。默认纯黑。")]
        public Color FadeColor = Color.black;

        [Tooltip("黑幕与眼睛的距离（米）。0.4~0.6 都可以，太近会穿帮近裁剪面。")]
        [Range(0.2f, 1.5f)]
        public float EyeDistance = 0.5f;

        [Header("行为")]
        [Tooltip("淡出完成后保持全黑（等待外部切换场景/传送）。")]
        public bool HoldBlackAfterFade = true;

        [Tooltip("调试：Start 时立即测试一轮淡入淡出。")]
        public bool TestOnStart = false;

        // ---------------- 运行时 ----------------

        private MeshRenderer _renderer;
        private Material _mat;
        private float _alpha;
        private float _target;
        private float _speed = 1f;
        private Transform _eye;
        private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            EnsureBlackout();
        }

        private void Start()
        {
            if (TestOnStart)
            {
                StartCoroutine(TestSequence());
            }
        }

        /// <summary>淡出到全黑。duration 秒内完成。</summary>
        public void FadeOut(float duration)
        {
            _target = 1f;
            _speed = duration > 0.01f ? 1f / duration : 999f;
            Debug.Log($"[Fade] 淡出开始（{duration:F1}s）", this);
        }

        /// <summary>淡入回可见。duration 秒内完成。</summary>
        public void FadeIn(float duration)
        {
            _target = 0f;
            _speed = duration > 0.01f ? 1f / duration : 999f;
            Debug.Log($"[Fade] 淡入开始（{duration:F1}s）", this);
        }

        /// <summary>当前是否全黑。</summary>
        public bool IsBlack => _alpha > 0.995f;

        /// <summary>
        /// ★ 立即全黑（跳过渐变）。启动淡入的起点、循环复位时用。
        /// </summary>
        public void SnapBlack()
        {
            _alpha = 1f;
            _target = 1f;
            ApplyAlpha();
            if (_renderer != null)
            {
                _renderer.enabled = true;
            }
        }

        private void Update()
        {
            if (_renderer == null)
            {
                return;
            }

            // 黑幕跟随眼睛（挂在相机下自动跟随；相机可能运行时切换）
            EnsureParentedToEye();

            if (Mathf.Abs(_alpha - _target) > 0.001f)
            {
                _alpha = Mathf.MoveTowards(_alpha, _target, _speed * Time.deltaTime);
                ApplyAlpha();
            }

            // 完全透明时关掉 renderer，避免透明 Quad 干扰射线
            bool shouldEnable = _alpha > 0.001f;
            if (_renderer.enabled != shouldEnable)
            {
                _renderer.enabled = shouldEnable;
            }
        }

        private void EnsureBlackout()
        {
            if (_renderer != null) return;

            // 找眼睛：优先 XR 主相机，其次任意 Camera
            Camera cam = Camera.main;
            if (cam == null)
            {
                cam = FindFirstObjectByType<Camera>();
            }
            if (cam != null)
            {
                _eye = cam.transform;
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "FlightBlackoutQuad";
            // 关掉碰撞器：黑幕不应被射线/物理打到
            var col = quad.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (_mat == null)
            {
                _mat = new Material(Shader.Find("Unlit/Color"));
            }
            _mat.name = "FlightBlackoutMat";
            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _renderer = mr;

            ApplyAlpha();
            _renderer.enabled = false;   // 初始不挡视线

            EnsureParentedToEye();

            Debug.Log($"[Fade] ✅ 黑幕已创建（eye={(_eye != null ? _eye.name : "无相机")}）", this);
        }

        private void EnsureParentedToEye()
        {
            if (_eye == null || _renderer == null) return;

            // 眼睛可能运行时才就绪（XR 加载晚），定期补挂
            if (_renderer.transform.parent != _eye)
            {
                _renderer.transform.SetParent(_eye, false);
            }

            _renderer.transform.localPosition = new Vector3(0f, 0f, EyeDistance);
            _renderer.transform.localRotation = Quaternion.identity;

            // 覆盖整个视场：0.5m 处 FOV 110° 需要约 1.5m，取 4m 绝对安全
            float size = EyeDistance * 8f;
            _renderer.transform.localScale = new Vector3(size, size, 1f);
        }

        private void ApplyAlpha()
        {
            if (_mat == null) return;

            Color c = FadeColor;
            if (_mat.HasProperty(BaseColorID))
            {
                // URP/Unlit：透明需要切 Surface Type，直接用带 alpha 的颜色
                // 并保证材质是 Transparent（运行时设置）
                c.a = _alpha;
                _mat.SetColor(BaseColorID, c);

                if (!_mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                {
                    _mat.SetFloat("_Surface", 1f);           // Transparent
                    _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    _mat.SetInt("_ZWrite", 0);
                    _mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    _mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
            }
            else
            {
                c.a = _alpha;
                _mat.color = c;
            }
        }

        private IEnumerator TestSequence()
        {
            yield return new WaitForSeconds(1f);
            FadeOut(0.8f);
            yield return new WaitForSeconds(2f);
            FadeIn(0.8f);
        }
    }
}
