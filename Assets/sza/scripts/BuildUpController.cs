using UnityEngine;

[ExecuteAlways] // 【TA核心技巧】允许脚本在不运行游戏的 Edit Mode 下也能实时预览！
public class BuildUpController : MonoBehaviour
{
    [Header("组件引用")]
    [Tooltip("如果留空，脚本会自动抓取当前物体及其所有子物体身上的所有 Renderer")]
    public Renderer[] houseRenderers;

    [Header("高度与动画配置")]
    [Range(0f, 1f)] public float progress = 0f;
    public float minHeight = 0f;
    public float maxHeight = 5f;
    public float duration = 4f;

    private bool isPlaying = false;
    private MaterialPropertyBlock propBlock;

    void OnEnable()
    {
        // 每次激活脚本或修改代码后，确保安全初始化
        Initialize();
    }

    private void Initialize()
    {
        if (houseRenderers == null || houseRenderers.Length == 0)
        {
            houseRenderers = GetComponentsInChildren<Renderer>();
        }
        if (propBlock == null)
        {
            propBlock = new MaterialPropertyBlock();
        }
    }

    void Start()
    {
        // 只有在真正运行游戏时，才自动播放动画
        if (Application.isPlaying)
        {
            UpdateShaderProperties();
        }
    }

    [ContextMenu("播放出现动画")]
    public void AnimateAppearance()
    {
        progress = 0f;
        isPlaying = true;
    }

    void Update()
    {
        // 1. 只有在运行游戏并且处于播放状态时，才自动走进度条
        if (Application.isPlaying && isPlaying)
        {
            progress += Time.deltaTime / duration;
            if (progress >= 1f)
            {
                progress = 1f;
                isPlaying = false;
            }
        }

        // 2. 【核心修复】无论是否在播放，也无论是否在运行游戏，只要 Update 跑了，就强制同步参数给 Shader
        UpdateShaderProperties();
    }

    private void UpdateShaderProperties()
    {
        // 多加一层保险，防止在编辑器里各种诡异状态下报空引用的错
        if (propBlock == null) Initialize();
        if (houseRenderers == null || houseRenderers.Length == 0) return;

        propBlock.SetFloat("_Progress", progress);
        propBlock.SetFloat("_MinHeight", minHeight);
        propBlock.SetFloat("_MaxHeight", maxHeight);

        foreach (var r in houseRenderers)
        {
            if (r != null)
            {
                r.SetPropertyBlock(propBlock);
            }
        }
    }
}