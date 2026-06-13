using UnityEngine;
using UnityEngine.Splines; // 必须引入 Splines 命名空间

public class SplineSequenceController : MonoBehaviour
{
    [Tooltip("第一条路径的动画组件")]
    public SplineAnimate firstPath;
    [Tooltip("第二条路径的动画组件")]
    public SplineAnimate secondPath;

    void Start()
    {
        // 确保一开始第二个路径不播放
        if (secondPath != null)
        {
            secondPath.Pause();
        }

        // 监听第一条路径播放完成的事件
        if (firstPath != null)
        {
            firstPath.Completed += OnFirstPathFinished;
        }
    }

    private void OnFirstPathFinished()
    {
        // 养成好习惯，触发后注销事件，防止重复调用
        firstPath.Completed -= OnFirstPathFinished;

        // 让小龙瞬间传送到第二条曲线的起点，并开始播放
        if (secondPath != null)
        {
            secondPath.Restart(true);
            // 注意：Restart(true) 会重置时间并播放，比单纯的 Play() 更稳，能防止初始位置错误
        }
    }
}