using UnityEditor;
using UnityEngine;

namespace Greatwall.Flight.EditorTools
{
    /// <summary>
    /// 【诊断】检查飞行龙的材质显现状态与云的实际生成情况。
    /// 用法：菜单 Window → Flight → Diagnose Dragon Visibility
    /// </summary>
    public static class FlightDragonDiagnose
    {
        [MenuItem("Window/Flight/Diagnose Dragon Visibility")]
        public static void Diagnose()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!scene.IsValid()) { Debug.LogError("场景无效"); return; }

            Transform dragon = null;
            Transform clouds = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "骑龙飞上天空" || t.name == "龙环绕飞行6") dragon = t;
                    if (t.name == "FlightClouds") clouds = t;
                }
            }

            if (dragon == null) { Debug.LogError("找不到 骑龙飞上天空（旧名 龙环绕飞行6）"); return; }

            Debug.Log("========== 龙可见性诊断 ==========");
            Debug.Log($"龙 active: {dragon.gameObject.activeInHierarchy}");
            Debug.Log($"龙 世界坐标: {dragon.position}");
            Debug.Log($"龙 世界缩放: {dragon.lossyScale}");

            // 查材质
            var renderers = dragon.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Debug.Log($"渲染器数量: {renderers.Length}");

            foreach (var r in renderers)
            {
                if (r == null) continue;
                Debug.Log($"  {r.gameObject.name}: enabled={r.enabled} " +
                          $"activeInHierarchy={r.gameObject.activeInHierarchy}");

                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) { Debug.Log($"    [{i}] material = null"); continue; }

                    // 查 _distance
                    bool hasProp = m.HasProperty("_distance");
                    float dist = hasProp ? m.GetFloat("_distance") : -999f;

                    // 查 shader keywords
                    bool dissolve = m.IsKeywordEnabled("_DISSOLVE_ON");

                    Debug.Log($"    [{i}] {m.name} shader={m.shader?.name} " +
                              $"_distance={dist:F3} hasProp={hasProp} " +
                              $"keyword_DISSOLVE={dissolve}");
                }
            }

            // Animator 状态
            var anim = dragon.GetComponent<Animator>();
            if (anim != null)
            {
                Debug.Log($"Animator: enabled={anim.enabled} " +
                          $"controller={(anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "NULL")}");
                var ac = anim.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                if (ac != null)
                {
                    var ps = ac.parameters;
                    Debug.Log($"  参数: {(ps.Length > 0 ? string.Join(", ", System.Array.ConvertAll(ps, x => x.name)) : "无")}");
                    var layers = ac.layers;
                    foreach (var L in layers)
                    {
                        Debug.Log($"  层 '{L.name}': 状态数={L.stateMachine.states.Length}");
                        foreach (var cs in L.stateMachine.states)
                            Debug.Log($"     - {cs.state.name} motion={(cs.state.motion != null ? cs.state.motion.name : "NULL")}");
                    }
                }
            }

            // 云
            Debug.Log("--- 云 ---");
            if (clouds == null)
            {
                Debug.Log("找不到 FlightClouds");
            }
            else
            {
                var ps = clouds.GetComponent<ParticleSystem>();
                if (ps == null)
                {
                    Debug.Log("FlightClouds 上没有 ParticleSystem");
                }
                else
                {
                    Debug.Log($"粒子系统: " +
                              $"activeInHierarchy={clouds.gameObject.activeInHierarchy}");
                    Debug.Log($"  main.simulationSpace={ps.main.simulationSpace} " +
                              $"(World=3) maxParticles={ps.main.maxParticles} startSize={ps.main.startSize}");
                    Debug.Log($"  当前粒子数: {ps.particleCount}");
                    Debug.Log($"  材质: {(ps.GetComponent<ParticleSystemRenderer>()?.sharedMaterial != null ? ps.GetComponent<ParticleSystemRenderer>().sharedMaterial.name : "NULL")}");
                }
            }

            // ★ 座位锚点：打印完整父级链与实际世界坐标
            Debug.Log("--- 座位 ---");
            Transform seat = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (Transform tr in root.GetComponentsInChildren<Transform>(true))
                {
                    if (tr.name == "DragonSeat") seat = tr;
                }
            }
            if (seat == null)
            {
                Debug.Log("  找不到 DragonSeat");
            }
            else
            {
                var chain = new System.Text.StringBuilder();
                var cur = seat;
                while (cur != null)
                {
                    chain.Insert(0, cur.name + "(" + cur.gameObject.activeSelf + ") < ");
                    cur = cur.parent;
                }
                Debug.Log($"  父级链: {chain}");
                Debug.Log($"  局部位置: {seat.localPosition}");
                Debug.Log($"  世界坐标: {seat.position}");
                Debug.Log($"  世界缩放: {seat.lossyScale}");
                var anchor = seat.GetComponent<DragonSeatAnchor>();
                if (anchor != null)
                {
                    Debug.Log($"  SeatWorldPosition = {anchor.SeatWorldPosition}");
                    Debug.Log($"  参数: H={anchor.SeatHeight} F={anchor.SeatForward} " +
                              $"L={anchor.SeatLeft} Tilt={anchor.SeatTilt} Yaw={anchor.SeatYaw}");
                }
                else
                {
                    Debug.Log("  ★ 没有 DragonSeatAnchor 组件");
                }
            }

            // 相机
            Debug.Log("--- 相机 ---");
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Main Camera" && t.GetComponent<Camera>() != null)
                    {
                        var c = t.GetComponent<Camera>();
                        Debug.Log($"Main Camera: 世界坐标={t.position} " +
                                  $"nearClip={c.nearClipPlane} farClip={c.farClipPlane} " +
                                  $"fieldOfView={c.fieldOfView} enabled={c.enabled}");
                    }
                }
            }
        }
    }
}
