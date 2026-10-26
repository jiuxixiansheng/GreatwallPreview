using System.Collections;
using UnityEngine;

public class VolumeLayerRefresh : MonoBehaviour
{
    [Header("刷新设置")]
    [Tooltip("运行时临时切换到的 Layer。默认用 Ignore Raycast。")]
    [SerializeField] private string temporaryLayerName = "Ignore Raycast";

    [Tooltip("等待几帧后切回原 Layer。通常 1 帧就够。")]
    [SerializeField, Min(1)] private int waitFramesBeforeRestore = 1;

    private int originalLayer;

    private void Start()
    {
        originalLayer = gameObject.layer;
        StartCoroutine(RefreshLayer());
    }

    private IEnumerator RefreshLayer()
    {
        int temporaryLayer = LayerMask.NameToLayer(temporaryLayerName);

        if (temporaryLayer < 0)
        {
            Debug.LogWarning(
                $"VolumeLayerRefresh: 找不到临时 Layer '{temporaryLayerName}'，将使用 Default Layer 作为临时切换。",
                this);

            temporaryLayer = 0;
        }

        if (temporaryLayer == originalLayer)
        {
            Debug.LogWarning(
                "VolumeLayerRefresh: 临时 Layer 和原 Layer 相同，无法通过切换 Layer 刷新 Volume。",
                this);

            yield break;
        }

        gameObject.layer = temporaryLayer;

        for (int i = 0; i < waitFramesBeforeRestore; i++)
        {
            yield return null;
        }

        gameObject.layer = originalLayer;
    }
}