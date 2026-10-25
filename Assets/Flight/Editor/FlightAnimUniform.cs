using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Greatwall.Flight.EditorTools
{
    /// <summary>
    /// 【动画匀速化】对飞龙 FBX 内嵌动画做时间重映射（retiming），
    /// 使龙身摆动速率全程均匀 —— 从动画数据层面根治
    /// 「升空段摆动过快、巡航段较慢」的速率不均问题。
    ///
    /// ★ 原理（时间重映射，非关键帧改值）：
    ///   1. 对 clip 的全部 rotation 曲线按 30fps 网格采样，
    ///      计算每帧的「摆动速率」（各分量变化绝对值之和）
    ///   2. 速率曲线做滑动平均（0.6s 窗）避免逐帧噪声
    ///   3. 累积摆动角 A(t)；构造重映射 φ(t)：使新动画在时间 t 播放
    ///      原动画 φ(t) 时刻的姿态，且 dφ/dt 处处使摆动速率恒等于均值
    ///      —— 升空段自动放慢、较慢段自动微加速，总时长不变
    ///   4. 每条曲线的关键帧【值不变、时间重映射】，生成新 clip
    ///      —— 姿态细节（扑翼形状等）完全保留
    ///
    /// ★ 产出：Assets/Flight/Anim/DragonFlight_Uniform.anim
    ///   并自动替换 AC_dragon_flight.controller 中 FlyForward 状态的 motion，
    ///   因此所有播放路径（包括直接用 Animator 播 FBX 动画的人）
    ///   都会使用匀速化版本。
    ///
    /// 用法：菜单 Window → Flight → 匀速化飞行动画 (Uniform)
    /// </summary>
    public static class FlightAnimUniform
    {
        private const string DragonPath =
            "Assets/sza/model/bigdragon/dragon_flight_v2.fbx";
        private const string ControllerPath =
            "Assets/Flight/AC_dragon_flight.controller";
        private const string OutDir = "Assets/Flight/Anim";
        private const string OutPath = OutDir + "/DragonFlight_Uniform.anim";

        private const float SampleFps = 30f;      // 分析采样率
        private const float SmoothWindow = 0.6f;  // 速率滑动平均窗（秒）
        private const float OutFps = 15f;         // ★ 输出重采样率（摆动≈2Hz，15fps 足够）
        private const float SimplifyTol = 0.0005f;// ★ 关键帧精简容差（RDP 思想）

        [MenuItem("Window/Flight/匀速化飞行动画 (Uniform)")]
        public static void Run()
        {
            // ---------- 1. 读取源 clip ----------
            var clips = AssetDatabase.LoadAllAssetsAtPath(DragonPath);
            AnimationClip src = null;
            foreach (var c in clips)
            {
                if (c is AnimationClip ac && !ac.name.Contains("__preview__")
                    && (src == null || ac.length > src.length))
                {
                    src = ac;
                }
            }
            if (src == null)
            {
                Debug.LogError($"[AnimUniform] {DragonPath} 里没有动画 clip。");
                return;
            }
            float T = src.length;
            Debug.Log($"[AnimUniform] 源 clip: {src.name} ({T:F1}s)", src);

            // ---------- 2. 提取 rotation 曲线 ----------
            var bindings = AnimationUtility.GetCurveBindings(src);
            var rotCurves = new List<AnimationCurve>();
            var rotKeys = new List<EditorCurveBinding>();
            foreach (var b in bindings)
            {
                if (b.type == typeof(Transform)
                    && b.propertyName.StartsWith("m_LocalRotation"))
                {
                    rotCurves.Add(AnimationUtility.GetEditorCurve(src, b));
                    rotKeys.Add(b);
                }
            }
            if (rotCurves.Count == 0)
            {
                Debug.LogError("[AnimUniform] clip 中没有 Transform 旋转曲线。");
                return;
            }
            Debug.Log($"[AnimUniform] rotation 曲线 {rotCurves.Count} 条（骨骼数 ≈ {rotCurves.Count / 4}）");

            // ---------- 3. 网格采样摆动速率 ----------
            int n = Mathf.Max(2, Mathf.CeilToInt(T * SampleFps) + 1);
            float dt = T / (n - 1);
            var rate = new float[n];
            var prev = new float[rotCurves.Count];
            for (int ci = 0; ci < rotCurves.Count; ci++)
            {
                prev[ci] = rotCurves[ci].Evaluate(0f);
            }
            for (int k = 1; k < n; k++)
            {
                float t = k * dt;
                float sum = 0f;
                for (int ci = 0; ci < rotCurves.Count; ci++)
                {
                    float v = rotCurves[ci].Evaluate(t);
                    sum += Mathf.Abs(v - prev[ci]);
                    prev[ci] = v;
                }
                rate[k] = sum;   // 该帧的全骨骼摆动量（相对单位）
            }

            // ---------- 4. 滑动平均平滑速率 ----------
            int half = Mathf.Max(1, Mathf.RoundToInt(SmoothWindow * 0.5f * SampleFps));
            var rateS = new float[n];
            for (int k = 0; k < n; k++)
            {
                float sum = 0f; int cnt = 0;
                for (int j = Mathf.Max(0, k - half);
                     j <= Mathf.Min(n - 1, k + half); j++)
                {
                    sum += rate[j]; cnt++;
                }
                rateS[k] = sum / Mathf.Max(1, cnt);
            }

            // ---------- 5. 累积摆动角 A(t) 与重映射 ----------
            var A = new float[n];
            for (int k = 1; k < n; k++)
            {
                A[k] = A[k - 1] + (rateS[k - 1] + rateS[k]) * 0.5f * dt;
            }
            float totalA = A[n - 1];
            if (totalA <= 0.0001f)
            {
                Debug.LogWarning("[AnimUniform] 摆动量几乎为零，无需匀速化。");
                return;
            }
            float avgRate = totalA / T;
            Debug.Log($"[AnimUniform] 总摆动量={totalA:F1} 平均速率={avgRate:F2}/s");

            // φ(t)：新时间 t → 原时间。使摆动速率处处 = avgRate。
            //   即对每个 t_new，找 A(t_old) = avgRate × t_new 的 t_old。
            var mapOld = new float[n];   // mapOld[k] = t_old（新时间 k*dt 对应）
            mapOld[0] = 0f;
            {
                int cursor = 0;
                for (int k = 1; k < n; k++)
                {
                    float target = avgRate * (k * dt);
                    while (cursor < n - 1 && A[cursor + 1] < target) cursor++;
                    float a0 = A[cursor];
                    float a1 = A[Mathf.Min(cursor + 1, n - 1)];
                    float f = (a1 - a0) > 0.0001f
                        ? Mathf.Clamp01((target - a0) / (a1 - a0)) : 0f;
                    mapOld[k] = Mathf.Lerp(cursor * dt,
                                           (cursor + 1) * dt, f);
                }
            }

            // ---------- 6. 生成匀速化 clip ----------
            var uniform = new AnimationClip
            {
                name = "DragonFlight_Uniform",
                frameRate = src.frameRate
            };
            var settings = AnimationUtility.GetAnimationClipSettings(src);
            settings.loopTime = false;   // 运行时为逐帧对时播放，不循环
            AnimationUtility.SetAnimationClipSettings(uniform, settings);

            int remapped = 0;
            long totalKeys = 0;
            foreach (var b in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(src, b);
                var keys = curve.keys;
                // 步骤 A：原关键帧【时间重映射、值不变】
                var mapped = new Keyframe[keys.Length];
                for (int i = 0; i < keys.Length; i++)
                {
                    var k = keys[i];
                    float tn = Mathf.Clamp(k.time, 0f, T) / dt;
                    int i0 = Mathf.Min((int)tn, n - 2);
                    float f = tn - i0;
                    float timeNew = Mathf.Lerp(mapOld[i0], mapOld[i0 + 1], f);
                    var nk = new Keyframe(timeNew, k.value,
                        k.inTangent, k.outTangent);
                    nk.weightedMode = k.weightedMode;
                    mapped[i] = nk;
                }
                var mappedCurve = new AnimationCurve(mapped);

                // 步骤 B：★ 15fps 均匀重采样（控文件体积：原 60fps 密键 ÷4）
                int m = Mathf.CeilToInt(T * OutFps) + 1;
                var dense = new List<Keyframe>(m);
                for (int i = 0; i < m; i++)
                {
                    float t = Mathf.Min(i / OutFps, T);
                    dense.Add(new Keyframe(t, mappedCurve.Evaluate(t)));
                }

                // 步骤 C：★ 关键帧精简（RDP 思想：偏离连线小于容差的键删除）
                var simp = new List<Keyframe> { dense[0] };
                for (int i = 1; i < dense.Count - 1; i++)
                {
                    var kPrev = simp[simp.Count - 1];
                    var kCur = dense[i];
                    var kNext = dense[i + 1];
                    float span = kNext.time - kPrev.time;
                    if (span < 1e-6f) continue;
                    float tt = (kCur.time - kPrev.time) / span;
                    if (Mathf.Abs(kCur.value - Mathf.Lerp(kPrev.value, kNext.value, tt))
                        > SimplifyTol)
                    {
                        simp.Add(kCur);
                    }
                }
                simp.Add(dense[dense.Count - 1]);
                totalKeys += simp.Count;

                var nc = new AnimationCurve(simp.ToArray());
                nc.preWrapMode = curve.preWrapMode;
                nc.postWrapMode = curve.postWrapMode;
                AnimationUtility.SetEditorCurve(uniform, b, nc);
                remapped++;
            }
            Debug.Log($"[AnimUniform] 输出关键帧总数={totalKeys}（15fps 重采样+精简）");

            // ---------- 7. 保存资产 ----------
            if (!System.IO.Directory.Exists(OutDir))
            {
                System.IO.Directory.CreateDirectory(OutDir);
            }
            AssetDatabase.DeleteAsset(OutPath);
            AssetDatabase.CreateAsset(uniform, OutPath);
            AssetDatabase.SaveAssets();

            // ---------- 8. 替换 Controller 的 FlyForward motion ----------
            var controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller != null)
            {
                foreach (var layer in controller.layers)
                {
                    foreach (var st in layer.stateMachine.states)
                    {
                        if (st.state.name == "FlyForward")
                        {
                            st.state.motion = uniform;
                        }
                    }
                }
                EditorUtility.SetDirty(controller);
            }
            else
            {
                Debug.LogWarning($"[AnimUniform] 未找到 {ControllerPath}，" +
                                 "请手动把 FlyForward 的 motion 换成新 clip。");
            }

            Debug.Log($"[AnimUniform] ✅ 匀速化完成：{remapped} 条曲线重映射 → " +
                      $"{OutPath}（时长 {uniform.length:F1}s 不变）；" +
                      $"Controller FlyForward 已替换。" +
                      "升空段放慢、较快段微加速，摆动速率全程均匀。");
        }
    }
}
