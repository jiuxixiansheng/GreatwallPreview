using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Greatwall.Flight;

namespace Greatwall.Flight.EditorTools
{
    /// <summary>
    /// 【K 帧烘焙工具】逐帧生成龙的飞行轨迹关键帧。
    ///
    /// ★ 与之前实时计算的区别：
    ///   之前：每帧用 Mathf.Sin / SmoothDamp 实时算位置
    ///   现在：先用同样（或更精细）的规则把整条轨迹「逐帧采样」成关键帧表，
    ///         烘焙成资产保存。运行时只按时间查表插值。
    ///
    /// ★ 为什么这样更流畅：
    ///   1. 速度曲线是设计出来的，不是公式凑出来的 —— 可以精确控制
    ///      每个时间点的速度、加速度
    ///   2. 逐帧检查时能直接看到「哪一帧速度突变」，便于针对性调整
    ///   3. 运行时零计算，多端同步只需同步一个时间值
    ///
    /// ★ 关键：全部使用 smooth（连点切线），
    ///   保证曲线在关键帧处速度连续，不会一顿一顿。
    ///
    /// 用法：菜单 Window → Flight → Bake Flight Keyframes
    /// </summary>
    public static class FlightKeyframeBake
    {
        private const string OutDir = "Assets/Flight/Anim";
        private const string OutPath = OutDir + "/FlightKeyframes.asset";

        // ===== 编排参数（两段式：拉起升空 → 直线飞远）=====

        [Header("时间轴（秒）")]
        // 起飞：滑跑加速 + 拉起爬升（13 秒完成升空）
        private const float TakeOffTime = 13f;
        // 直线远飞：平飞加速冲入云海
        private const float DepartureTime = 16f;

        [Header("空间")]
        [Tooltip("巡航高度（米）。升空段从起点爬到这个高度。")]
        private const float FlightAltitude = 65f;
        private const float DepartureDistance = 384f;

        [Header("★ 速度剖面（全程连续，pitch 由剖面实时算出）")]
        [Tooltip("巡航速度（米/秒）。前 6 秒从 0 平滑加速（滑跑）。")]
        private const float CruiseSpeed = 24f;
        [Tooltip("拉起爬升率（米/秒）。爬升角 = atan(该值/巡航速度) ≈ 17°，\n" +
                 "与真实飞机拉起仰角一致。")]
        private const float ClimbRate = 7.5f;

        [Header("起伏（唯一的动态源）")]
        private const float BobAmplitude = 0.12f;
        private const float BobFrequency = 0.28f;

        [Header("采样密度")]
        [Tooltip("每秒采样多少个关键帧。30 足够流畅，60 更精细。")]
        private static readonly float[] SampleRates = { 30f, 60f, 15f };
        private const int SampleRateIndex = 1;

        [MenuItem("Window/Flight/Bake Flight Keyframes")]
        public static void Bake()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[KFrame] 场景无效");
                return;
            }

            // 找龙与座位，作为轨迹的参考点
            Transform dragon = null;
            Transform seat = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "骑龙飞上天空" || t.name == "龙环绕飞行6") dragon = t;
                    if (t.name == "DragonSeat") seat = t;
                }
            }

            if (dragon == null)
            {
                Debug.LogError("[KFrame] 找不到 骑龙飞上天空（旧名 龙环绕飞行6）");
                return;
            }

            Vector3 origin = dragon.position;
            // ★ 轨迹 yaw = 飞行方向。场景摆放的是"起飞预姿态"（模型机头朝飞行方向），
            //   而模型机头在根本地 yaw≈−77°（jaw 是下颌关节在头后部），所以
            //   飞行方向 = 摆放 yaw + 103。
            //   运行时 FlightDirector 用 rootYaw = 飞行方向 − 103，
            //   t=0 时与摆放姿态完全一致（起飞瞬间零跳变）。
            const float ModelYawOffset = 0f;
            float startYaw = dragon.eulerAngles.y + ModelYawOffset;

            Debug.Log($"[KFrame] 龙起始位置 = {origin}  起始朝向 = {startYaw:F1}°");
            if (seat != null)
            {
                Debug.Log($"[KFrame] 座位当前位置 = {seat.position}（仅参考，不影响烘焙）");
            }

            var table = ScriptableObject.CreateInstance<FlightKeyframeTable>();
            table.bakedFrom = "";
            table.bakedFrom = $"Two-phase: climb-out {TakeOffTime}s (pitch up to " +
                              $"{Mathf.Atan2(ClimbRate, CruiseSpeed) * Mathf.Rad2Deg:F0}°), " +
                              $"straight cruise {DepartureTime}s @ {CruiseSpeed}m/s";

            float rate = SampleRates[SampleRateIndex];
            float total = TakeOffTime + DepartureTime;
            int totalFrames = Mathf.CeilToInt(total * rate) + 1;

            float T1 = TakeOffTime;   // 起飞（拉起升空）结束
            float T3 = total;         // 远飞结束

            // ★★★ 两段式速度剖面积分器（本次重写：取消盘旋）
            //
            //   段1 起飞（0~13s）：像飞机一样
            //     vH: 0 →平滑→ 24（前 6 秒滑跑加速）
            //     vY: 0（滑跑 2.5s）→平滑→ 7.5（拉起爬升）→ 保持 → 渐降至 2
            //     pitch = atan(vY / vH) —— 拉起仰角峰值约 17°，随改平回落
            //     龙体沿速度矢量抬头 → 玩家视角经 PitchFollow 同步上仰
            //
            //   段2 直线远飞（13~29s）：
            //     vH: 24 恒定（速度感）
            //     vY: 2 →平滑→ 0（改平），之后平飞冲入云海
            //     pitch: 4.8° → 0
            //
            //   速度剖面全程连续（无突变），pitch 由剖面直接算出，
            //   天然与轨迹吻合，不需要单独 K 帧。
            Vector3 pos = origin;
            float yaw = startYaw;

            for (int i = 0; i < totalFrames; i++)
            {
                float t = i / rate;
                float dt = 1f / rate;

                var frame = new FlightKeyframeTable.Frame
                {
                    time = t,
                    pos = pos,
                    yaw = yaw,
                    // ★ 横滚恒为 0 —— 第一人称骑乘绝不能有横滚
                    roll = 0f
                };

                if (i > 0)
                {
                    // --- 水平速度剖面 ---
                    float vH;
                    if (t < T1)
                    {
                        // 滑跑加速：0 → 24，前 6 秒完成
                        vH = CruiseSpeed * Smooth01(t / 6f);
                    }
                    else
                    {
                        vH = CruiseSpeed;   // 直线巡航，速度感
                    }

                    // --- 垂直速度剖面 ---
                    float vY;
                    if (t < T1)
                    {
                        if (t < 2.5f)
                        {
                            vY = 0f;                              // 滑跑，贴地加速
                        }
                        else if (t < 5.5f)
                        {
                            vY = ClimbRate * Smooth01((t - 2.5f) / 3f);   // 拉起
                        }
                        else if (t < 11f)
                        {
                            vY = ClimbRate;                       // 稳定爬升
                        }
                        else
                        {
                            // 接近巡航高度，渐降至 2（段间无缝）
                            vY = Mathf.Lerp(ClimbRate, 2f, Smooth01((t - 11f) / 2f));
                        }
                    }
                    else
                    {
                        // 远飞：改平
                        vY = Mathf.Lerp(2f, 0f, Smooth01((t - T1) / 4f));
                    }

                    // --- pitch = 路径仰角（由速度剖面直接算出，天然平滑）---
                    float pitch = Mathf.Atan2(vY, Mathf.Max(1f, vH)) * Mathf.Rad2Deg;

                    // --- 积分 ---
                    float rad = yaw * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                    pos += dir * (vH * dt);
                    pos.y += vY * dt;

                    Vector3 d = pos - frame.pos;
                    d.y = 0f;

                    frame.pos = pos;
                    frame.yaw = yaw;
                    frame.pitch = pitch;
                    frame.speed = d.magnitude / dt;
                }

                table.frames.Add(frame);
            }

            table.totalTime = total;

            // ★ 阶段边界直接写进资产，运行时读取，不做推算
            table.takeOffEnd = TakeOffTime;
            table.orbitEnd = TakeOffTime;
            table.departureEnd = total;

            // ===== 自检 =====
            ReportAnomalies(table);

            // ===== 保存 =====
            // ★★★ 复用已有资产（本次修复）：DeleteAsset+CreateAsset 会改变
            //   GUID，场景里 FlightDirector.Keyframes 的引用静默变空，
            //   运行时静默回退到旧公式——已踩过两次坑。
            //   改为：资产存在就清空重填，GUID 不变，引用永远有效。
            if (!AssetDatabase.IsValidFolder(OutDir))
            {
                AssetDatabase.CreateFolder("Assets/Flight", "Anim");
            }
            var existing = AssetDatabase.LoadAssetAtPath<FlightKeyframeTable>(OutPath);
            if (existing != null)
            {
                existing.frames.Clear();
                foreach (var f in table.frames) existing.frames.Add(f);
                existing.totalTime = table.totalTime;
                existing.bakedFrom = table.bakedFrom;
                existing.takeOffEnd = table.takeOffEnd;
                existing.orbitEnd = table.orbitEnd;
                existing.departureEnd = table.departureEnd;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                Debug.Log($"[KFrame] ✅ 已更新现有资产（GUID 不变，引用保持）: {OutPath}");
            }
            else
            {
                AssetDatabase.CreateAsset(table, OutPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[KFrame] ✅ 已生成新资产: {OutPath}");
            }
            AssetDatabase.Refresh();

            Debug.Log($"[KFrame]    关键帧数 = {table.frames.Count}");
            Debug.Log($"[KFrame]    总时长   = {total:F1} 秒");
        }
        /// <summary>smoothstep：缓入缓出，一阶导在两端为 0。</summary>
        private static float Smooth01(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3f - 2f * u);
        }

        /// <summary>
        /// 自检：报告相邻帧的速度变化，找出突变点；并检查 VR 舒适度指标。
        /// </summary>
        private static void ReportAnomalies(FlightKeyframeTable table)
        {
            if (table.frames.Count < 3) return;

            float maxJump = 0f;
            int maxAt = -1;
            float avg = 0f;

            for (int i = 1; i < table.frames.Count; i++)
            {
                float a = table.frames[i - 1].speed;
                float b = table.frames[i].speed;
                if (a > 0.01f) avg += a;
            }
            avg /= (table.frames.Count - 1);

            for (int i = 1; i < table.frames.Count; i++)
            {
                float a = table.frames[i - 1].speed;
                float b = table.frames[i].speed;
                float jump = Mathf.Abs(b - a);
                if (jump > maxJump)
                {
                    maxJump = jump;
                    maxAt = i;
                }
            }

            Debug.Log($"[KFrame] --- 速度自检 ---");
            Debug.Log($"[KFrame]    平均水平速度 = {avg:F2} m/s");
            Debug.Log($"[KFrame]    最大帧间速度变化 = {maxJump:F3} m/s" +
                      (maxAt >= 0 ? $"（在 t={table.frames[maxAt].time:F2}s）" : ""));
            Debug.Log($"[KFrame]    判定 = {(maxJump < avg * 0.25f ? "✅ 平滑" : "⚠️ 有突变，建议调参数")}");

            // ★ VR 舒适度自检：垂直速度 / 角速度 / 爬升角
            //   垂直速度区分「短时峰值」（起飞/拉起段，≤4s 可接受）
            //   和「持续超标」（盘旋/巡航段长期 >2m/s，会晕）
            float maxVs = 0f, maxYawRate = 0f, maxClimbAngle = 0f;
            float vsOverDuration = 0f, vsOverPeak = 0f;
            for (int i = 1; i < table.frames.Count; i++)
            {
                float dt = table.frames[i].time - table.frames[i - 1].time;
                if (dt < 0.0001f) continue;
                float vs = (table.frames[i].pos.y - table.frames[i - 1].pos.y) / dt;
                if (Mathf.Abs(vs) > Mathf.Abs(maxVs)) maxVs = vs;

                // 统计起飞段（前 TakeOffTime 秒）之外的超标持续时间
                if (table.frames[i].time > TakeOffTime && Mathf.Abs(vs) > 2f)
                {
                    vsOverDuration += dt;
                    vsOverPeak = Mathf.Max(vsOverPeak, Mathf.Abs(vs));
                }

                // 瞬时爬升角 = 垂直速度 / 水平速度（拉起仰角的实测值）
                float vh = table.frames[i].speed;
                if (vh > 0.5f)
                {
                    float ca = Mathf.Atan2(Mathf.Abs(vs), vh) * Mathf.Rad2Deg;
                    if (ca > maxClimbAngle) maxClimbAngle = ca;
                }

                float dy = Mathf.DeltaAngle(table.frames[i - 1].yaw, table.frames[i].yaw);
                float yr = Mathf.Abs(dy) / dt;
                if (yr > maxYawRate) maxYawRate = yr;
            }
            Debug.Log($"[KFrame]    最大垂直速度 = {maxVs:F2} m/s（拉起段爬升率）");
            Debug.Log($"[KFrame]    最大偏航角速度 = {maxYawRate:F1} °/s（直线飞行应≈0）");
            Debug.Log($"[KFrame]    最大爬升仰角 ≈ {maxClimbAngle:F1}°（真实飞机拉起 10~15°）");

            // pitch 自检：最大俯仰角 + 是否平滑回 0
            float maxPitch = 0f;
            for (int i = 0; i < table.frames.Count; i++)
            {
                if (table.frames[i].pitch > maxPitch) maxPitch = table.frames[i].pitch;
            }
            float endPitch = table.frames[table.frames.Count - 1].pitch;
            Debug.Log($"[KFrame]    最大机体俯仰 = {maxPitch:F1}°，末帧俯仰 = {endPitch:F1}°（平飞应≈0）");

            // 分段报告
            Debug.Log($"[KFrame]    拉起升空 0 ~ {TakeOffTime}s（滑跑→拉起→改平）");
            Debug.Log($"[KFrame]    直线远飞 {TakeOffTime} ~ {table.totalTime}s @ {CruiseSpeed}m/s");
        }
    }
}
