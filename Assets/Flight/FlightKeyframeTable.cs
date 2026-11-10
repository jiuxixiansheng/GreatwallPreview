using System.Collections.Generic;
using UnityEngine;

namespace Greatwall.Flight
{
    /// <summary>
    /// 飞行 K 帧表。由编辑器工具 FlightKeyframeBake 烘焙生成。
    ///
    /// ★ 为什么用 K 帧表而不是实时计算：
    ///   实时计算（Mathf.Sin / SmoothDamp）虽然平滑，但每一帧的目标值都在变，
    ///   高速运动时容易出现速度突变与曲线不连续。
    ///   烘焙成 K 帧后，轨迹是「一条确定的曲线」，可以逐帧检查与微调，
    ///   多端同步也只需同步一个时间值。
    ///
    /// ★ 关键：所有切线必须用「连点切线」(smooth)，
    ///   否则曲线在关键帧处会速度突变，看起来就是一顿一顿的。
    /// </summary>
    [System.Serializable]
    public class FlightKeyframeTable : ScriptableObject
    {
        [System.Serializable]
        public struct Frame
        {
            public float time;      // 秒（从起飞开始）
            public Vector3 pos;     // 世界位置
            public float yaw;       // 朝向（度，0 = 世界 Z 轴方向）
            public float pitch;     // ★ 俯仰（度）——起飞拉起时为正（抬头），平飞为 0
            public float roll;      // 横滚（度）—— 骑乘时恒为 0

            /// <summary>
            /// 该帧的水平速度（米/秒）。仅用于烘焙时的自检，
            /// 运行时不需要，可以忽略。
            /// </summary>
            public float speed;
        }

        public List<Frame> frames = new List<Frame>();
        public float totalTime = 1f;
        public string bakedFrom = "";

        // ===== 阶段边界（烘焙时写入，运行时直接用，不做推算）=====

        [Header("阶段时间边界（秒）")]
        [Tooltip("起飞段结束时刻")]
        public float takeOffEnd = 6f;
        [Tooltip("盘旋段结束时刻")]
        public float orbitEnd = 36f;
        [Tooltip("远飞段结束时刻 = 总时长")]
        public float departureEnd = 56f;

        /// <summary>
        /// 按时间采样位置与朝向。相邻关键帧之间线性插值
        /// （60fps 烘焙帧足够密，线性即平滑——勿用 SmoothStep，
        ///  会造成 60Hz 速度波动即"一卡一卡"）。
        /// </summary>
        public void Sample(float t, out Vector3 pos, out float yaw, out float roll)
        {
            float pitch;
            SampleFull(t, out pos, out yaw, out pitch, out roll);
        }

        /// <summary>含俯仰的完整采样。</summary>
        public void SampleFull(float t, out Vector3 pos, out float yaw,
                               out float pitch, out float roll)
        {
            if (frames == null || frames.Count == 0)
            {
                pos = Vector3.zero; yaw = 0f; pitch = 0f; roll = 0f;
                return;
            }

            if (frames.Count == 1 || t <= frames[0].time)
            {
                pos = frames[0].pos; yaw = frames[0].yaw;
                pitch = frames[0].pitch; roll = frames[0].roll;
                return;
            }

            int last = frames.Count - 1;
            if (t >= frames[last].time)
            {
                pos = frames[last].pos; yaw = frames[last].yaw;
                pitch = frames[last].pitch; roll = frames[last].roll;
                return;
            }

            // 二分查找所在区间
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (frames[mid].time <= t) lo = mid;
                else hi = mid;
            }

            Frame a = frames[lo];
            Frame b = frames[hi];
            float span = b.time - a.time;
            if (span <= 0.00001f)
            {
                pos = a.pos; yaw = a.yaw; pitch = a.pitch; roll = a.roll;
                return;
            }

            // ★ 线性插值（密集帧间只该用线性，SmoothStep 会抖）
            float u = (t - a.time) / span;

            pos = Vector3.Lerp(a.pos, b.pos, u);
            yaw = Mathf.LerpAngle(a.yaw, b.yaw, u);
            pitch = Mathf.Lerp(a.pitch, b.pitch, u);
            roll = Mathf.Lerp(a.roll, b.roll, u);
        }

        public float GetDuration() => totalTime;
    }
}
