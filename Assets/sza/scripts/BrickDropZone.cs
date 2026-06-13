using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

public class BrickDropZone : MonoBehaviour
{
    [Header("解谜设置")]
    [Tooltip("需要放入的砖块总数")]
    public int targetBrickCount = 3;

    [Tooltip("目标物体的 Tag 标签")]
    public string targetTag = "Brick";

    [Header("触发事件")]
    [Tooltip("当三块砖都放进去时触发什么？")]
    public UnityEvent onPuzzleSolved;

    // 使用 List 记录当前在框内的砖块，防止物理穿模导致的重复计数 Bug
    private List<Collider> bricksInside = new List<Collider>();

    // 防止解开谜题后重复触发
    private bool isSolved = false;

    void OnTriggerEnter(Collider other)
    {
        if (isSolved) return;

        // 如果碰到的是砖块
        if (other.CompareTag(targetTag))
        {
            // 确保这块砖之前没被记录过
            if (!bricksInside.Contains(other))
            {
                bricksInside.Add(other);
                Debug.Log($"[解谜] 放入了一块砖！当前进度: {bricksInside.Count} / {targetBrickCount}");

                // 检查是否凑齐了 3 块
                if (bricksInside.Count >= targetBrickCount)
                {
                    ExecuteSolveLogic(); // 调用核心通关逻辑
                }
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (isSolved) return;

        // 如果玩家又把砖块拿出来了，把它从列表里剔除
        if (other.CompareTag(targetTag))
        {
            if (bricksInside.Contains(other))
            {
                bricksInside.Remove(other);
                Debug.Log($"[解谜] 拿走了一块砖。当前进度: {bricksInside.Count} / {targetBrickCount}");
            }
        }
    }

    // ==========================================
    // 【新增调试接口】：供外部脚本或按键强制调用
    // ==========================================
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

    // 将通关的公共逻辑提取出来，保持代码整洁
    private void ExecuteSolveLogic()
    {
        isSolved = true;
        Debug.Log("[解谜] 目标达成！触发后续机关/剧情！");
        onPuzzleSolved.Invoke(); // 触发面板上的事件
    }
}
