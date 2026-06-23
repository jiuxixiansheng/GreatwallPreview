using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class Controlappear : MonoBehaviour
{
    [Header("复原动画配置")]
    [Tooltip("复原动画持续的时间（秒）")]
    public float restoreDuration = 4.0f;

    [Header("进度条控制 (手动定义)")]
    [Tooltip("进度条的起始值（模型完全隐藏时的状态）")]
    public float startProgress = -1.0f;

    [Tooltip("进度条的终点值（模型完全复原时的状态）")]
    public float endProgress = 10.0f;

    // 🚨 仅保留进度的 ID
    private readonly int progressPropertyID = Shader.PropertyToID("_RestoreProgress");

    private Renderer _renderer;
    private MaterialPropertyBlock _propBlock;

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _propBlock = new MaterialPropertyBlock();

        // 初始化状态：直接应用你设置的起始进度
        UpdateShaderProperties(startProgress);
    }

    // 外部调用这个方法触发复原
    public void TriggerRestoration()
    {
        StopAllCoroutines();
        StartCoroutine(RestoreRoutine());
    }

    private IEnumerator RestoreRoutine()
    {
        float elapsedTime = 0f;

        while (elapsedTime < restoreDuration)
        {
            elapsedTime += Time.deltaTime;

            // 纯粹根据你暴露的起点和终点进行线性插值
            float currentProgress = Mathf.Lerp(startProgress, endProgress, elapsedTime / restoreDuration);

            UpdateShaderProperties(currentProgress);

            yield return null; // 等待下一帧
        }

        // 确保最终状态严丝合缝地停在终点值
        UpdateShaderProperties(endProgress);
    }

    private void UpdateShaderProperties(float currentProgress)
    {
        _renderer.GetPropertyBlock(_propBlock);

        // 只向 Shader 传递最新的进度值
        _propBlock.SetFloat(progressPropertyID, currentProgress);

        _renderer.SetPropertyBlock(_propBlock);
    }
}