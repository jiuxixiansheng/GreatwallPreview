using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

//[RequireComponent(typeof(SplineAnimate))]
public class SplineTimeController : MonoBehaviour
{
    private SplineAnimate splineAnimate;

    [System.Serializable]
    public struct Waypoint
    {
        [Range(0f, 1f)]
        [Tooltip("触发位置 (0是起点，1是终点)")]
        public float position;

        [Header("停顿与手持道具")]
        [Tooltip("走到这里是否要停一下？")]
        public bool shouldPause;
        [Tooltip("停顿几秒？")]
        public float pauseDuration;
        [Tooltip("停顿的这段时间里，手里要不要出现方块和灯？")]
        public bool showHandItems;

        [Header("场景物品出现")]
        [Tooltip("勾选后，走到这里会让下方的【四个物体】同时出现")]
        public bool showAllTargetObjects;

        [Header("单个物品消失")]
        [Tooltip("走到这里时，需要延迟消失的特定物体（留空则不触发）")]
        public GameObject objectToHide;
        [Tooltip("延迟几秒消失？")]
        public float hideDelay;
    }

    [Header("道具设置")]
    [Tooltip("把角色手里的道具拖到这里")]
    public GameObject Prop;

    [Header("裂缝")]
    [Tooltip("把你想要同时出现的四个裂缝物体拖到这里")]
    public GameObject[] targetObjects;

    [Header("路径事件序列")]
    public List<Waypoint> waypoints = new List<Waypoint>();

    private int nextIndex = 0;
    
    void Start()
    {
        
        // 自动获取物体上的 SplineAnimate 组件
        splineAnimate = GetComponent<SplineAnimate>();
        // 自动按位置先后顺序排序
        waypoints.Sort((a, b) => a.position.CompareTo(b.position));
        // 初始状态下，先把道具藏起来
        SetHandItemsActive(false);

        // 初始化：隐藏场景里的四个物体
        foreach (var obj in targetObjects)
        {
            if (obj != null) obj.SetActive(false);
        }

        if (splineAnimate != null)
        {
            splineAnimate.Play();
            StartCoroutine(CheckSplineRoutine());
        }
    }

    IEnumerator CheckSplineRoutine()
    {
        while (nextIndex < waypoints.Count)
        {
            var wp = waypoints[nextIndex];

            if (splineAnimate.NormalizedTime >= wp.position)
            {
                // 1. 处理【全部出现】逻辑
                if (wp.showAllTargetObjects)
                {
                    foreach (var obj in targetObjects)
                    {
                        if (obj != null) obj.SetActive(true);
                    }
                }

                // 2. 处理【延迟消失】逻辑 (独立协程，不卡顿)
                if (wp.objectToHide != null)
                {
                    StartCoroutine(HideObjectRoutine(wp.objectToHide, wp.hideDelay));
                }

                // 3. 处理【角色停顿与手持道具】逻辑
                if (wp.shouldPause)
                {
                    splineAnimate.NormalizedTime = wp.position;
                    splineAnimate.Pause(); // 停下脚步

                    // 如果勾选了显示手持道具，就在停下时把它掏出来
                    if (wp.showHandItems) SetHandItemsActive(true);

                    // 站着等几秒
                    yield return new WaitForSeconds(wp.pauseDuration);

                    splineAnimate.Play(); // 继续走
                }

                nextIndex++;
            }

            yield return null;
        }
    }

    // 辅助函数：控制手持道具显隐
    private void SetHandItemsActive(bool state)
    {
        if (Prop != null) Prop.SetActive(state);
    }

    // 独立倒计时器：处理延迟隐藏
    IEnumerator HideObjectRoutine(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (obj != null)
        {
            obj.SetActive(false);
        }
    }
}
