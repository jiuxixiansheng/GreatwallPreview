using UnityEngine;
using System.Collections.Generic;

public class BrickDropZone : MonoBehaviour
{
    [Header("解谜设置")]
    [Tooltip("需要放入的砖块总数")]
    public int targetBrickCount = 3;

    [Tooltip("目标物体的 Tag 标签")]
    public string targetTag = "Brick";

    private readonly List<Collider> bricksInside = new List<Collider>();
    private bool isSolved = false;

    public List<GameObject> targetObjects = new List<GameObject>();

    private void OnTriggerEnter(Collider other)
    {
        if (isSolved || !other.CompareTag(targetTag)) return;
        if (bricksInside.Contains(other)) return;

        bricksInside.Add(other);
        Debug.Log(
            $"[解谜] 放入了一块砖！当前进度: {bricksInside.Count} / {targetBrickCount}");

        if (bricksInside.Count >= targetBrickCount)
        {
            ExecuteSolveLogic();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (isSolved || !other.CompareTag(targetTag)) return;

        if (bricksInside.Remove(other))
        {
            Debug.Log(
                $"[解谜] 拿走了一块砖。当前进度: {bricksInside.Count} / {targetBrickCount}");
        }
    }

    public void ForceSolvePuzzle()
    {
        if (isSolved)
        {
            Debug.LogWarning("[解谜调试] 谜题已经解开过了，无需重复触发。");
            return;
        }

        Debug.Log("[解谜调试] 收到外部强制放行指令！跳过搬砖过程。");
        ExecuteSolveLogic();
    }

    private void ExecuteSolveLogic()
    {
        if (isSolved) return;

        isSolved = true;
        Debug.Log("[解谜] 目标达成！发送修复城墙结束事件。");
        EventDispatcher.Instance.Dispatch("修复城墙结束");
        foreach (GameObject obj in targetObjects)
        {
            if (obj != null)
            {
                obj.SetActive(false);
            }
        }
        gameObject.SetActive(false);
    }
}