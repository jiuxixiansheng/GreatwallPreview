using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Greatwall.Flight.EditorTools
{
    /// <summary>
    /// 【诊断】实测各飞行动画 clip 的骨骼运动强度。
    ///
    /// ★ 为什么需要这个：
    ///   用户反馈「飞行速度太慢，几乎静止」。
    ///   光看 clip 时长/帧数不够 —— 必须测「骨骼实际每秒转多少度」，
    ///   才能判断哪个动画真的有速度感。
    ///
    /// ★ 关键：四元数在 Unity 里是 4 条独立曲线（x/y/z/w），
    ///   单看一条无法还原旋转。正确做法是用 AnimationCurve 逐分量重建四元数，
    ///   再累相邻帧的角度。
    ///
    /// 用法：菜单 Window → Flight → 对比飞行动画强度
    /// </summary>
    public static class FlightAnimCompare
    {
        [MenuItem("Window/Flight/对比飞行动画强度")]
        public static void Compare()
        {
            string[] paths =
            {
                "Assets/sza/model/bigdragon/向前飞行动画 Trim.fbx",
                "Assets/sza/model/bigdragon/龙环绕飞行6.fbx",
                "Assets/sza/model/bigdragon/龙环绕飞行3.fbx",
                "Assets/sza/model/bigdragon/flyforward.FBX",
                "Assets/Flight/Anim/FlightRide_Loop.anim"
            };

            var sb = new StringBuilder();

            foreach (var path in paths)
            {
                if (!System.IO.File.Exists(path))
                {
                    sb.AppendLine($"{System.IO.Path.GetFileName(path)} —— 文件不存在");
                    continue;
                }

                var objs = AssetDatabase.LoadAllAssetsAtPath(path);
                AnimationClip clip = null;
                foreach (var o in objs)
                {
                    if (o is AnimationClip c && !c.name.Contains("__preview__"))
                    {
                        clip = c;
                        break;
                    }
                }

                if (clip == null)
                {
                    sb.AppendLine($"{System.IO.Path.GetFileName(path)} —— 无 clip");
                    continue;
                }

                var result = Measure(clip);
                sb.AppendLine($"=== {System.IO.Path.GetFileName(path)} ===");
                sb.AppendLine($"  clip = {clip.name}");
                sb.AppendLine($"  时长 = {clip.length:F2}s  帧率 = {clip.frameRate}");
                sb.AppendLine($"  曲线数 = {result.curveCount}  骨骼数 = {result.boneCount}");
                sb.AppendLine($"  根节点每秒位移 = {result.rootSpeed:F2} m/s");
                sb.AppendLine($"  骨骼平均每秒旋转 = {result.avgRot:F1}°/s");
                sb.AppendLine($"  骨骼最大每秒旋转 = {result.maxRot:F1}°/s  ({result.maxBone})");
                sb.AppendLine($"  根节点总位移 = {result.rootTotal:F1} 米");
                sb.AppendLine();
            }

            System.IO.File.WriteAllText("Assets/Flight/anim_compare.txt",
                                        sb.ToString());
            Debug.Log("[Compare] 已写入 Assets/Flight/anim_compare.txt");
        }

        private struct Result
        {
            public int curveCount;
            public int boneCount;
            public float rootSpeed;
            public float rootTotal;
            public float avgRot;
            public float maxRot;
            public string maxBone;
        }

        private static Result Measure(AnimationClip clip)
        {
            var r = new Result();
            r.maxBone = "-";

            var bindings = AnimationUtility.GetCurveBindings(clip);
            r.curveCount = bindings.Length;

            // 按骨骼路径收集四元数的 4 条曲线
            var quats = new Dictionary<string, Dictionary<string, AnimationCurve>>();
            var positions = new Dictionary<string, AnimationCurve>();

            foreach (var b in bindings)
            {
                if (b.propertyName.StartsWith("m_LocalRotation"))
                {
                    if (!quats.TryGetValue(b.path, out var m))
                    {
                        m = new Dictionary<string, AnimationCurve>();
                        quats[b.path] = m;
                    }
                    string axis = b.propertyName.Substring(
                        "m_LocalRotation".Length);
                    m[axis] = AnimationUtility.GetEditorCurve(clip, b);
                }
                else if (b.propertyName == "m_LocalPosition.x")
                {
                    positions[b.path] = AnimationUtility.GetEditorCurve(clip, b);
                }
            }

            r.boneCount = quats.Count;
            if (quats.Count == 0) return r;

            // ---- 根节点位移 ----
            foreach (var kv in positions)
            {
                var c = kv.Value;
                if (c == null || c.length < 2) continue;
                float total = 0f;
                float prev = c.Evaluate(c.keys[0].time);
                for (int i = 1; i < c.length; i++)
                {
                    float v = c.Evaluate(c.keys[i].time);
                    total += Mathf.Abs(v - prev);
                    prev = v;
                }
                r.rootTotal = Mathf.Max(r.rootTotal, total);
                r.rootSpeed = r.rootTotal / Mathf.Max(0.01f, clip.length);
                break;
            }

            // ---- 骨骼旋转 ----
            float sum = 0f;
            int n = 0;

            foreach (var kv in quats)
            {
                var m = kv.Value;
                if (m.Count < 4) continue;
                if (!m.TryGetValue(".x", out var cx) || !m.TryGetValue(".y", out var cy)
                    || !m.TryGetValue(".z", out var cz) || !m.TryGetValue(".w", out var cw))
                    continue;

                // 用第一段关键帧区间累加角度
                float acc = 0f;
                int samples = Mathf.Min(cx.length, 120);
                if (samples < 2) continue;

                Quaternion prev = SampleQuat(cx, cy, cz, cw, cx.keys[0].time);
                for (int i = 1; i < samples; i++)
                {
                    float t = cx.keys[i].time;
                    Quaternion cur = SampleQuat(cx, cy, cz, cw, t);
                    float dt = cx.keys[i].time - cx.keys[i - 1].time;
                    if (dt > 0.00001f)
                    {
                        acc += Quaternion.Angle(prev, cur);
                    }
                    prev = cur;
                }

                float perSec = acc / clip.length;
                sum += perSec;
                n++;

                if (perSec > r.maxRot)
                {
                    r.maxRot = perSec;
                    r.maxBone = kv.Key;
                }
            }

            r.avgRot = n > 0 ? sum / n : 0f;
            return r;
        }

        private static Quaternion SampleQuat(AnimationCurve cx, AnimationCurve cy,
                                             AnimationCurve cz, AnimationCurve cw,
                                             float t)
        {
            var q = new Quaternion(cx.Evaluate(t), cy.Evaluate(t),
                                   cz.Evaluate(t), cw.Evaluate(t));
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (mag < 0.0001f) return Quaternion.identity;
            return new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag);
        }
    }
}
