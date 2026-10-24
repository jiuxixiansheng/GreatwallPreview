using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Greatwall.Flight.EditorTools
{
    /// <summary>
    /// 【K 帧重编排】为第一人称骑乘重做龙身动画 —— 平稳版。
    ///
    /// ★★★ 为什么必须彻底重做（运行时实测数据）：
    ///   原 clip `SKM_Dragon.001|Action` 是**翻滚飞行**动作，不是平稳巡航。
    ///   实测（每 2 秒采样，统计各骨骼世界朝向变化）：
    ///       Head   每秒 46~70°    roll 77~82°
    ///       Neck   每秒 36~60°    roll 56~76°
    ///       Spine6 每秒 33~58°    roll 62~78°
    ///       Spine4 每秒 38~51°    roll 83~84°
    ///       Spine1 每秒 45~73°    roll 84~131°   ← 最剧烈
    ///   一个 39.7 秒周期累积约 2879° = **8 圈完整翻滚**。
    ///   玩家骑在背上完全无法承受。
    ///
    /// ★ 重做策略（逐帧写 K 帧，不用公式实时算）：
    ///   1. 横滚（Roll）**恒为 0** —— 玩家视角绝不能有 roll
    ///   2. 偏航（Yaw）**恒为 0** —— 朝向由代码的 K 帧轨迹控制
    ///   3. 俯仰（Pitch）只留 ±7.5°，频率 0.5Hz
    ///      —— 这是「龙在飞行中微微调整高度」的感觉
    ///   4. 头颈段俯仰额外衰减 —— 玩家视角挂在这里，不能点头
    ///   5. ★ 尾巴改为【单向偏置 + 摆动调制】
    ///      偏置恒定同向、摆幅小于偏置，数学保证尾巴永不越过身体中线，
    ///      从根上解决「尾巴摆到龙头前方挡视线」的问题。
    ///
    /// 用法：菜单 Window → Flight → Rebuild Ride Animation (Calm)
    /// </summary>
    public static class FlightRideAnimCalm
    {
        private const string DragonPath =
            "Assets/sza/model/bigdragon/龙环绕飞行6.fbx";
        private const string OutDir = "Assets/Flight/Anim";
        private const string OutPath = OutDir + "/FlightRide_Loop.anim";
        private const string CtrlPath = "Assets/Flight/AC_dragon_flight.controller";

        // ===== 编排参数 =====

        private const float LoopDuration = 12f;   // 一个循环 16 秒
        private const int SampleRate = 30;         // 30fps 逐帧

        // ★★★ 参数标定依据（实测「龙环绕飞行3」= 真实飞行动画）：
        //     骨骼平均旋转 13.0°/s，最大 124.7°/s（那是翻滚，要避开）
        //     根节点位移 12.15 m/s
        //   「向前飞行动画 Trim」只有 0.7°/s —— 几乎是静态 pose，不可用。
        //   所以目标：让我们的动画平均达到 8~14°/s，与真飞行动画同量级。

        // 俯仰：整体姿态动态
        // ★ 三个分级：脊柱给身体起伏感，颈部/头部必须极小
        //   （颈部是长杠杆，串联会甩尾横扫 —— 实测 2.77m 侧向摆幅）
        private const float CruisePitch = 3.0f;       // 脊柱幅度（度）
        private const float CruiseNeckPitch = 0.5f;   // 颈部幅度（度）—— 刻意极小
        private const float CruiseHeadPitch = 0.8f;   // 头部幅度（度）—— 玩家视角，仅微起伏
        private const float PitchFrequency = 0.5f;   // 频率（Hz）

        // 偏航：左右摆动，增加「在飞」的感觉
        private const float CruiseYaw = 6.0f;
        private const float YawFrequency = 0.42f;

        // 横向摆动：模拟气流中的左右偏移。
        // ★ 幅度必须小 —— 累积多了就是横滚，玩家会晕
        private const float SideSway = 2.4f;
        private const float SideFrequency = 0.33f;

        // ★★★ 尾巴：单向偏置 + 摆动调制（本次重做的核心）
        //
        //   【实测根因】用 AnimationMode 逐帧采样真实 clip，测量尾尖(Tail21)
        //   相对龙头(Head)的空间位置，得到：
        //       现状（纯对称正弦摆动）侧向范围 = -0.87 ~ +1.26 m  ← 跨过身体中线
        //   尾巴来回穿过龙身轴线，甩到龙头前方挡住视线。
        //
        //   【为什么减小摆幅没用】
        //   只要偏置为 0，sin() 必然正负交替，无论幅度多小都对称地
        //   越过中线。所以必须引入一个**恒定同向的偏置角**，
        //   让整条尾巴固定偏向身体一侧。
        //
        //   【硬约束】|wave| < |bias| 时，合成角 = bias + wave*sin()
        //   符号恒为 bias 的符号 → 数学上保证永不跨中线。
        //
        //   ★★★【第二次翻案——视觉验证后的最终方案】★★★
        //   第一版"单向偏置"完成后截图验证发现：玩家视线是 **Head 骨骼
        //   的 forward**，而 bindpose 下尾巴本来就在 Head 本地坐标的
        //   「右 3.5m / 上 0.9m / 前 2.4m」——即尾巴天生翘在玩家前方。
        //   每节小幅侧偏根本改变不了"尾巴悬在身前"的事实。
        //
        //   正解：在【尾巴根部一次性掰一个大角度】（绕局部 Z 轴 +60°），
        //   整条尾巴像被压下去一样垂到玩家右下方/右后方——
        //   FK 扫描验证（Head 本地坐标，y=上 z=前）：
        //       根部 +60° → 尾尖 y=-0.90 z=-0.11，Tail10 y=-1.93  ✓低于视线
        //       根部 +90° → 尾尖 y=-0.53 z=-1.90，Tail10 y=-2.42  ✓更低更靠后
        //   中段保持小幅【同侧】摆动（bias 恒正 + wave < bias），
        //   相位延迟形成行波，鞭子感保留，尾巴绝不会摆回视野。
        private const float TailRootBendX = 15f;     // ★ 根部抬头分量（度）：让尾巴整体位置上移
        private const float TailRootBendZ = 55f;     // ★ 根部侧弯分量（度）：压到右后侧
        // 实测（Head 本地坐标，rx=15/rz=55）：尾尖 右5.62 / 上-0.42 / 水平角93°
        // 对比旧参数(rx0/rz60)：上-0.90 → 抬高 0.5m，更靠右 0.4m
        // 摆动全程 y峰-0.05、角谷91°（正右方远处，前方视野绝对安全）
        private const float TailSideBias = 1.2f;     // ★ 中段摆动偏置（度/节），恒正=同侧
        private const float TailSideWave = 0.8f;     // ★ 摆动幅度，必须 < TailSideBias
        private const float TailPhaseLag = 0.30f;    // 每节相位延迟（弧度）→ 鞭状行波
        private const float TailFrequency = 0.5f;

        /// <summary>尾巴节数（Tail ~ Tail21）</summary>
        private const int TailSegmentCount = 22;

        [MenuItem("Window/Flight/Rebuild Ride Animation (Calm)")]
        public static void Rebuild()
        {
            var dragon = AssetDatabase.LoadAssetAtPath<GameObject>(DragonPath);
            if (dragon == null)
            {
                Debug.LogError($"[Anim] 找不到 {DragonPath}");
                return;
            }

            var bones = CollectBones(dragon);
            if (bones.Count == 0)
            {
                Debug.LogError("[Anim] FBX 里找不到可用骨骼");
                return;
            }

            Debug.Log($"[Anim] 编排 {bones.Count} 根骨骼");
            foreach (var b in bones)
            {
                if (b.path.EndsWith("/Head") || b.path.EndsWith("Head"))
                {
                    Vector3 e = b.baseRotation.eulerAngles;
                    Debug.Log($"[Anim]   基准姿态 {b.path}: " +
                              $"({e.x:F1}, {e.y:F1}, {e.z:F1})");
                }
            }

            var clip = new AnimationClip
            {
                name = "FlightRide_Loop",
                frameRate = SampleRate,
                legacy = false
            };

            int totalFrames = Mathf.RoundToInt(LoopDuration * SampleRate) + 1;

            // ★★★ 关键修复：每根骨骼建 4 条曲线，逐帧填关键帧。
            //   之前的实现用 Flat() 每条曲线只放 2 个点（时间差 0.0001 秒），
            //   等于常量曲线 → 动画完全不播（实测旋转 = 0.0°/s）。
            //   现在每帧一个 Keyframe，是真正的 K 帧动画。
            var xs = new List<AnimationCurve>[bones.Count];
            var ys = new List<AnimationCurve>[bones.Count];
            var zs = new List<AnimationCurve>[bones.Count];
            var ws = new List<AnimationCurve>[bones.Count];
            var keyX = new List<Keyframe>[bones.Count];
            var keyY = new List<Keyframe>[bones.Count];
            var keyZ = new List<Keyframe>[bones.Count];
            var keyW = new List<Keyframe>[bones.Count];

            for (int b = 0; b < bones.Count; b++)
            {
                keyX[b] = new List<Keyframe>(totalFrames);
                keyY[b] = new List<Keyframe>(totalFrames);
                keyZ[b] = new List<Keyframe>(totalFrames);
                keyW[b] = new List<Keyframe>(totalFrames);
            }

            for (int i = 0; i < totalFrames; i++)
            {
                float t = i / (float)SampleRate;

                for (int b = 0; b < bones.Count; b++)
                {
                    Quaternion rot = ComposeCalmPose(t, bones[b]);
                    keyX[b].Add(new Keyframe(t, rot.x));
                    keyY[b].Add(new Keyframe(t, rot.y));
                    keyZ[b].Add(new Keyframe(t, rot.z));
                    keyW[b].Add(new Keyframe(t, rot.w));
                }
            }

            int curves = 0;
            for (int b = 0; b < bones.Count; b++)
            {
                clip.SetCurve(bones[b].path, typeof(Transform),
                              "localRotation.x", Smooth(keyX[b]));
                clip.SetCurve(bones[b].path, typeof(Transform),
                              "localRotation.y", Smooth(keyY[b]));
                clip.SetCurve(bones[b].path, typeof(Transform),
                              "localRotation.z", Smooth(keyZ[b]));
                clip.SetCurve(bones[b].path, typeof(Transform),
                              "localRotation.w", Smooth(keyW[b]));
                curves += 4;
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (!AssetDatabase.IsValidFolder(OutDir))
            {
                AssetDatabase.CreateFolder("Assets/Flight", "Anim");
            }
            AssetDatabase.DeleteAsset(OutPath);
            AssetDatabase.CreateAsset(clip, OutPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Anim] ✅ 已生成 {OutPath}");
            Debug.Log($"[Anim]    {LoopDuration}s · {SampleRate}fps · " +
                      $"{totalFrames} 帧 · {curves} 条曲线");
            int tailCount = 0;
            float tailAccum = 0f;
            foreach (var b in bones)
            {
                if (b.layer != 2) continue;
                tailCount++;
                // 累积最大偏角 = 偏置 + 摆幅（两者同向叠加）
                tailAccum += Mathf.Abs(TailSideBias) + TailSideWave;
            }

            Debug.Log($"[Anim]    俯仰 ±{CruisePitch}° @ {PitchFrequency}Hz（唯一保留的运动）");
            Debug.Log($"[Anim]    尾巴 {tailCount} 节 ★根部弯折 + 同侧摆动：");
            Debug.Log($"[Anim]      根部一次性弯折 X={TailRootBendX}° Z={TailRootBendZ}°（压到玩家右下方，低于视线）");
            Debug.Log($"[Anim]      中段摆动 = {TailSideBias}° + {TailSideWave}°·sin （|wave|<|bias| 恒正，只在一侧摆）");
            Debug.Log($"[Anim]      每节相位延迟 {TailPhaseLag} rad（行波鞭子感）  累积偏角 = {tailAccum:F1}°");

            BuildControllerAndAssign(clip);
        }

        /// <summary>
        /// 编排某一帧的姿态。
        /// </summary>
        private static Quaternion ComposeCalmPose(float time, BoneInfo bone)
        {
            float w = time * Mathf.PI * 2f;

            // ---- 俯仰：低频正弦，整条脊椎链共用同一相位 ----
            //
            // ★★★ 关键：俯仰同样会带动尾巴横扫，必须严格限制。
            //   逐骨骼隔离实测（偏航/横滚已全部归零后）：
            //       仅脊柱俯仰 → 侧向摆幅 0.89 m
            //       仅颈部俯仰 → 侧向摆幅 2.77 m  ← 仍会挡视线
            //   原因：颈部是长杠杆，6 根 Neck 串联的俯仰会把尾巴
            //   甩出弧线，横扫过身体中线。
            //
            //   而玩家骑在龙背，躯干俯仰**本来就看不见**
            //   （视角在 Head，感知到的是轻微起伏）。
            //   所以：脊柱保留极小俯仰（身体起伏感），
            //   颈部与头部几乎归零（避免长杠杆甩尾）。
            float pitch;
            if (bone.neckWeight >= 1f)
            {
                // Head：玩家视角所在，只留极轻微起伏
                pitch = Mathf.Sin(w * PitchFrequency) * CruiseHeadPitch;
            }
            else if (bone.neckWeight > 0f)
            {
                // 颈部：长杠杆，压到最小
                pitch = Mathf.Sin(w * PitchFrequency) * CruiseNeckPitch;
            }
            else
            {
                // 脊柱：身体起伏感的主要来源
                pitch = Mathf.Sin(w * PitchFrequency) * CruisePitch;
            }

            // ★★★ 关键修正（实测定位到的真正元凶）：
            //   骨骼链是 Head → Neck → … → Spine1 → Tail，
            //   **Head 是所有骨骼的祖先**。
            //   逐骨骼隔离测试（只保留部分骨骼曲线，采样尾尖轨迹）：
            //       仅尾巴曲线   → 侧向摆幅 0.52 m  ✓
            //       仅脊柱曲线   → 侧向摆幅 4.42 m
            //       仅颈/头曲线  → 侧向摆幅 6.39 m  ← 元凶
            //   头颈的偏航/横摆会带着整条尾巴一起扫过身体中线，
            //   从龙头前方经过并挡住视线。
            //
            //   实测增量角（相对 bindpose，12 秒极差）：
            //       Head  yaw 6.00°   Neck  yaw 7.50°   Spine  yaw 12.00°
            //   这些偏航必须**全部归零**。
            //
            //   ★ 用显式判断而不是 Mathf.Lerp —— 插值会残留
            //     (1→0, w=0.75) 仍得 0.25 的量，白白留下摆动。
            //
            // ★★★ 脊柱的偏航/横摆同样必须归零。
            //   实测「仅脊柱曲线」贡献 4.42 m 侧向摆幅 —— 脊柱是尾巴的
            //   直接父级链，它左右扭会把整条尾巴甩过身体中线。
            //   而玩家骑在龙背，脊柱的左右扭动**本来就看不见**
            //   （被自己身体与龙背遮挡），唯一下的作用就是
            //   让尾巴扫到眼前挡视线。
            //
            //   结论：整条脊椎链（Head→Neck→Spine）**只保留俯仰**。
            //   俯仰带来上下起伏的体感，是真实观感；
            //   偏航/横滚只产生副作用，必须为 0。
            float yaw = 0f;
            float roll = 0f;

            // ---- 尾巴：根部大弯折 + 中段同侧摆动 ----
            //
            //   玩家视线 = Head 骨骼的 forward。bindpose 下尾巴天生
            //   翘在玩家前上方——根部一次性掰大角度（X 抬头 15° +
            //   Z 侧弯 55°）把整条尾巴压到玩家右后下方，
            //   FK 实测尾尖 y=-0.42m，全程低于视线、水平角 ≥91°。
            //   中段小幅同侧摆动（|wave| < |bias| 恒正，不跨中线），
            //   相位延迟保留行波（鞭子感）。
            if (bone.layer == 2)
            {
                float phase = w * TailFrequency - bone.tailIndex * TailPhaseLag;

                if (bone.tailIndex == 0)
                {
                    // 根部：固定大弯折（X 抬头分量 + Z 侧弯分量），
                    // 把整条尾巴压到玩家右后侧、视线高度之下
                    pitch = 0f; roll = 0f; yaw = 0f;
                    return bone.baseRotation
                         * Quaternion.Euler(TailRootBendX, 0f, TailRootBendZ);
                }

                // 中段/末段：同侧小幅摆动（恒正，不跨中线）
                float sideAngle = TailSideBias
                              + TailSideWave * Mathf.Sin(phase);

                // 尾巴不参与身体的整体俯仰
                pitch = 0f;
                roll = 0f;
                yaw = 0f;

                return bone.baseRotation
                     * Quaternion.Euler(0f, 0f, sideAngle);
            }

            // ★★★ 在骨骼初始姿态上叠加增量，不能从零开始。
            Quaternion delta = Quaternion.Euler(pitch, yaw, roll);
            return bone.baseRotation * delta;
        }

        /// <summary>
        /// 由逐帧关键帧列表构建曲线，并设平滑切线。
        /// ★ 用平滑切线（不是线性），否则帧间会有折角。
        /// </summary>
        private static AnimationCurve Smooth(List<Keyframe> keys)
        {
            var c = new AnimationCurve(keys.ToArray());
            for (int i = 0; i < c.length; i++)
            {
                c.SmoothTangents(i, 0f);
            }
            return c;
        }

        private class BoneInfo
        {
            public string path;
            public int layer;        // 0=躯干 1=颈胸 2=尾
            public float neckWeight; // 0=躯干 0.75=颈 1=头
            /// <summary>尾巴节的序号（0 = 根部 Tail，21 = 尾尖 Tail21）。
            /// 用于计算相位延迟，形成鞭状行波。</summary>
            public int tailIndex = -1;
            /// <summary>
            /// ★ 骨骼在 FBX 里的初始局部旋转（模型的自然姿态）。
            ///   必须在基准上叠加动画，不能从零开始 ——
            ///   实测：从零开始会让 Head 变成 pitch = -90°（垂直向下）。
            /// </summary>
            public Quaternion baseRotation;
        }

        private static List<BoneInfo> CollectBones(GameObject dragon)
        {
            var result = new List<BoneInfo>();

            string[] spine = { "Spine6", "Spine5", "Spine4",
                               "Spine3", "Spine2", "Spine1" };
            string[] neck = { "Neck", "Neck1", "Neck2",
                              "Neck3", "Neck4", "Neck5" };

            foreach (Transform t in dragon.GetComponentsInChildren<Transform>(true))
            {
                if (t == dragon.transform) continue;

                string p = PathOf(dragon.transform, t);
                if (string.IsNullOrEmpty(p)) continue;

                string name = t.name;

                // ★ 记录初始局部旋转作为基准
                Quaternion baseRot = t.localRotation;

                if (System.Array.IndexOf(spine, name) >= 0)
                {
                    result.Add(new BoneInfo { path = p, layer = 0,
                                              neckWeight = 0f, baseRotation = baseRot });
                }
                else if (name == "Head")
                {
                    result.Add(new BoneInfo { path = p, layer = 1,
                                              neckWeight = 1f, baseRotation = baseRot });
                }
                else if (System.Array.IndexOf(neck, name) >= 0)
                {
                    result.Add(new BoneInfo { path = p, layer = 1,
                                              neckWeight = 0.75f, baseRotation = baseRot });
                }
                else if (name.StartsWith("Tail"))
                {
                    // ★ 解析尾巴序号：Tail=0, Tail1=1, ... Tail21=21
                    int idx = 0;
                    string numPart = name.Substring(4);
                    if (!string.IsNullOrEmpty(numPart)
                        && int.TryParse(numPart, out int n))
                    {
                        idx = n;
                    }
                    result.Add(new BoneInfo
                    {
                        path = p, layer = 2, neckWeight = 0f,
                        baseRotation = baseRot, tailIndex = idx
                    });
                }
            }

            return result;
        }

        private static string PathOf(Transform root, Transform t)
        {
            var sb = new System.Text.StringBuilder(t.name);
            var cur = t.parent;
            while (cur != null && cur != root)
            {
                sb.Insert(0, cur.name + "/");
                cur = cur.parent;
            }
            return cur == root ? sb.ToString() : null;
        }

        private static void BuildControllerAndAssign(AnimationClip clip)
        {
            AssetDatabase.DeleteAsset(CtrlPath);
            var controller =
                UnityEditor.Animations.AnimatorController
                    .CreateAnimatorControllerAtPath(CtrlPath);

            var sm = controller.layers[0].stateMachine;
            var state = sm.AddState("FlyForward");
            state.motion = clip;
            state.writeDefaultValues = true;
            sm.defaultState = state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "龙环绕飞行6") continue;
                    var anim = t.GetComponent<Animator>();
                    if (anim == null) continue;

                    anim.runtimeAnimatorController = controller;
                    anim.applyRootMotion = false;
                    EditorUtility.SetDirty(anim);
                    Debug.Log("[Anim] ✅ 已赋给龙，applyRootMotion = false");
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
        }
    }
}
