using System;
using UnityEditor;
using UnityEngine;

namespace Greatwall.Flight.EditorTools
{
    /// <summary>
    /// 【一次性工具】把座位锚点从 Head 移到躯干 Spine3。
    ///
    /// ★ 为什么必须移：
    ///   原 clip 里 `SKM_Dragon.001/Head` 带巨大位移曲线（X 8.0m / Y 8.8m / Z 5.1m），
    ///   玩家座位挂在头部会被甩得剧烈晃动。
    ///   把座位移到躯干中段的 Spine3，龙身摆动仍能传导，但不会被甩头带动。
    ///
    /// 用法：菜单 Window → Flight → Move Seat To Spine
    /// </summary>
    public static class FlightSeatReposition
    {
        private const float UpOffsetMeters = 6.5f;

        private const float ForwardOffsetMeters = -1.5f;

        [MenuItem("Window/Flight/Move Seat To Spine")]
        public static void Move()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[Seat] 场景无效");
                return;
            }

            // 找座位
            Transform seat = null;
            // 找躯干目标：优先 Spine3，退化到 Spine4 / Spine2
            Transform target = null;
            string[] candidates = { "Spine3", "Spine4", "Spine2" };

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "DragonSeat") seat = t;
                    if (target == null && Array.IndexOf(candidates, t.name) >= 0)
                    {
                        // 必须确认它属于龙（往上找有 SKM_Dragon.001 祖先）
                        var cur = t;
                        bool isDragon = false;
                        while (cur != null)
                        {
                            if (cur.name == "SKM_Dragon.001") { isDragon = true; break; }
                            cur = cur.parent;
                        }
                        if (isDragon) target = t;
                    }
                }
            }

            if (seat == null)
            {
                Debug.LogError("[Seat] 找不到 DragonSeat");
                return;
            }
            if (target == null)
            {
                Debug.LogError("[Seat] 找不到躯干骨骼（Spine2~5）");
                return;
            }

            Debug.Log($"[Seat] 当前父级链：{Chain(seat)}");
            Debug.Log($"[Seat] 目标骨骼：{target.name}");

            // 记住世界位置与朝向，改父级后尽量保持
            Vector3 worldPos = seat.position;
            Quaternion worldRot = seat.rotation;

            Undo.SetTransformParent(seat, target, "Move Seat To Spine");

            // 换父级后世界位置会变，重置为干净的值。
            // ★ 骨骼空间的原点不在龙背上（实测 Spine3 世界 Y=29.1，
            //   而模型背部在 Y≈40 附近），所以必须给一个向上的偏移。
            //   UpOffset 用世界单位换算成局部单位，跨缩放层级也准确。
            // ★ 直接在骨骼空间里放到"模型背部"对应的局部高度。
            //   做法：把座位临时对齐到目标骨骼的世界原点，
            //   再用世界空间向上偏移固定米数，最后读回局部坐标。
            //   这样跨任意缩放层级都准确。
            seat.localPosition = Vector3.zero;
            seat.localRotation = Quaternion.identity;

            // ★ 找龙头与颈部，算出"背朝龙、脸朝龙头"的正确位置与朝向
            Transform headBone = FindBone(scene, "Head");
            Transform neckBone = FindBone(scene, "Neck");

            Vector3 boneWorld = target.position;
            Vector3 seatWorld;

            if (headBone != null)
            {
                // 躯干中段取 Neck 与 Spine4 之间，龙头方向 = 从 Neck 指向 Head
                Vector3 towardHead = (headBone.position - neckBone.position).normalized;

                // 座位放在 Spine4 背上，朝向与龙头方向一致
                seatWorld = boneWorld + Vector3.up * UpOffsetMeters
                                   + towardHead * ForwardOffsetMeters;

                // 朝向：面朝龙头，但保持水平（不随龙头俯仰）
                Vector3 faceDir = new Vector3(towardHead.x, 0f, towardHead.z);
                if (faceDir.sqrMagnitude > 0.0001f)
                {
                    faceDir.Normalize();
                    seat.rotation = Quaternion.LookRotation(faceDir, Vector3.up);
                }

                Debug.Log($"[Seat] 龙头 Head  = {headBone.position}");
                Debug.Log($"[Seat] 颈部 Neck  = {neckBone.position}");
                Debug.Log($"[Seat] 指向龙头的方向 = {towardHead}");
                Debug.Log($"[Seat] 龙身朝向（应与上方向一致）= {faceDir}");
                Debug.Log($"[Seat] 座位世界朝向 euler = {seat.eulerAngles}");
            }
            else
            {
                seatWorld = boneWorld + Vector3.up * UpOffsetMeters
                                   + target.forward * ForwardOffsetMeters;
                Debug.LogWarning("[Seat] 找不到 Head/Neck，退回用骨骼朝向");
            }

            seat.position = seatWorld;

            // ★ 骨骼本身带 52° 的 Z 轴扭转（模型本身的自然姿态）。
            //   如果直接用局部旋转，视角会歪着。做法：
            //   先记下世界朝向，再把局部旋转清零，最后按世界朝向重设。
            //   这样无论骨骼怎么扭转，视角始终水平正对龙头。
            if (headBone != null)
            {
                Vector3 faceDir = new Vector3(
                    (headBone.position - neckBone.position).x, 0f,
                    (headBone.position - neckBone.position).z);
                if (faceDir.sqrMagnitude > 0.0001f)
                {
                    faceDir.Normalize();
                    seat.localRotation = Quaternion.identity;
                    seat.rotation = Quaternion.LookRotation(faceDir, Vector3.up);
                }
            }

            Debug.Log($"[Seat] 目标骨骼 {target.name} 世界原点 = {boneWorld}");
            Debug.Log($"[Seat] 座位世界位置 = {seatWorld}");
            Debug.Log($"[Seat] 换算后局部位置 = {seat.localPosition}");
            Debug.Log($"[Seat] 换算后局部旋转 = {seat.localEulerAngles}");

            EditorUtility.SetDirty(seat);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Seat] ✅ 已移到 {target.name} 下");
            Debug.Log($"[Seat] 新父级链：{Chain(seat)}");
            Debug.Log($"[Seat] 世界坐标：{seat.position}");
            Debug.Log($"[Seat] 世界缩放：{seat.lossyScale}");
            Debug.Log($"[Seat] （原世界坐标 {worldPos}，已按躯干空间重置）");

            // ★ 测量全身骨骼的世界坐标，用来定位真正的龙背高度
            //   骨骼空间的原点不在龙背，需要实测而不是靠算。
            Debug.Log("[Seat] --- 骨骼世界坐标实测 ---");
            Transform body = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "SKM_Dragon") body = t;
                }
            }
            if (body != null)
            {
                Debug.Log($"[Seat] 龙身体 SKM_Dragon 世界坐标 = {body.position}");
                Debug.Log($"[Seat] 龙身体 包围盒中心 = {body.GetComponentInChildren<SkinnedMeshRenderer>().bounds.center}");
                Debug.Log($"[Seat] 龙身体 包围盒大小 = {body.GetComponentInChildren<SkinnedMeshRenderer>().bounds.size}");
            }

            string[] probe = { "Spine6", "Spine5", "Spine4", "Spine3",
                               "Spine2", "Spine1", "Neck", "Neck1", "Head" };
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (Array.IndexOf(probe, t.name) < 0) continue;
                    var cur = t;
                    bool isDragon = false;
                    while (cur != null)
                    {
                        if (cur.name == "SKM_Dragon.001") { isDragon = true; break; }
                        cur = cur.parent;
                    }
                    if (!isDragon) continue;
                    Debug.Log($"[Seat]   {t.name,-8} 世界 = {t.position}");
                }
            }

            // 提示：座位参数在骨骼空间下建议值
            var anchor = seat.GetComponent<DragonSeatAnchor>();
            if (anchor != null)
            {
                Debug.Log($"[Seat] 当前参数：Height={anchor.SeatHeight} " +
                          $"Forward={anchor.SeatForward} Tilt={anchor.SeatTilt}");
                Debug.Log($"[Seat] SeatWorldPosition = {anchor.SeatWorldPosition}");
            }
        }

        /// <summary>
        /// 在龙的骨骼树里找指定名字的骨骼（确认属于 SKM_Dragon.001 祖先）。
        /// </summary>
        private static Transform FindBone(UnityEngine.SceneManagement.Scene scene,
                                          string boneName)
        {
            Transform found = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != boneName) continue;
                    var cur = t;
                    bool isDragon = false;
                    while (cur != null)
                    {
                        if (cur.name == "SKM_Dragon.001") { isDragon = true; break; }
                        cur = cur.parent;
                    }
                    if (isDragon) { found = t; break; }
                }
                if (found != null) break;
            }
            return found;
        }

        private static string Chain(Transform t)
        {
            var sb = new System.Text.StringBuilder();
            var cur = t;
            while (cur != null)
            {
                sb.Insert(0, cur.name + " < ");
                cur = cur.parent;
            }
            return sb.ToString();
        }
    }
}
