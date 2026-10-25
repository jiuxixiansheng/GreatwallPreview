using UnityEditor;
using UnityEngine;

namespace Greatwall.Flight.EditorTools
{
    /// <summary>
    /// 【预览工具】在 Scene 视图里直接看到龙。
    ///
    /// ★ 为什么编辑器里看不到龙：
    ///   团队的 BigDragonBehavior 在 Awake() 里执行
    ///       SetAllMaterialsDistance(1.01f);
    ///   注释写得很清楚：「初始状态：大龙隐藏」。
    ///   shader 是 Shader Graphs/bianshen，_distance = 1.01 表示完全透明。
    ///   这是「转场显现」机制：只有派发「大龙特效转场」事件才显现。
    ///
    ///   而我的 ForceRevealMaterial 只在 Play 运行时生效，
    ///   所以你在 Scene 视图（编辑态）看不到龙。
    ///
    /// ★ 这个工具做什么：
    ///   在编辑态直接把 _distance 改成 0，让龙在 Scene 视图可见。
    ///   切换时会写回材质资产并保存，刷新后依然生效。
    ///
    /// 用法：菜单 Window → Flight → 龙可见性
    /// </summary>
    public static class FlightDragonVisibility
    {
        private const float HiddenValue = 1.01f;
        private const float VisibleValue = 0f;

        [MenuItem("Window/Flight/龙可见性 (切换)")]
        public static void Toggle()
        {
            var mats = FindDragonMaterials();
            if (mats.Count == 0)
            {
                Debug.LogError("[可见性] 没找到龙的材质");
                return;
            }

            // 判断当前状态：全部隐藏 → 显示；否则 → 隐藏
            bool allHidden = true;
            foreach (var m in mats)
            {
                if (m.HasProperty("_distance")
                    && m.GetFloat("_distance") < HiddenValue * 0.5f)
                {
                    allHidden = false;
                    break;
                }
            }

            float target = allHidden ? VisibleValue : HiddenValue;

            foreach (var m in mats)
            {
                if (m.HasProperty("_distance"))
                {
                    m.SetFloat("_distance", target);
                    EditorUtility.SetDirty(m);
                }
            }

            // 关闭可能残留的溶解 keyword
            foreach (var m in mats)
            {
                if (target < 0.01f) m.EnableKeyword("_DISSOLVE_ON");
                else m.DisableKeyword("_DISSOLVE_ON");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[可见性] 已修改 {mats.Count} 个材质：" +
                      $"_distance = {target}（{(target < 0.01f ? "★ 现在应该能看到龙了" : "已隐藏")}）");
            foreach (var m in mats)
            {
                Debug.Log($"[可见性]   {m.name}  ({m.shader.name})");
            }
        }

        [MenuItem("Window/Flight/龙可见性/强制显示")]
        public static void ForceShow()
        {
            SetDistance(VisibleValue);
        }

        [MenuItem("Window/Flight/龙可见性/恢复隐藏 (团队原始状态)")]
        public static void ForceHide()
        {
            SetDistance(HiddenValue);
        }

        private static void SetDistance(float value)
        {
            var mats = FindDragonMaterials();
            if (mats.Count == 0)
            {
                Debug.LogError("[可见性] 没找到龙的材质");
                return;
            }

            foreach (var m in mats)
            {
                if (m.HasProperty("_distance"))
                {
                    m.SetFloat("_distance", value);
                    if (value < 0.01f) m.EnableKeyword("_DISSOLVE_ON");
                    else m.DisableKeyword("_DISSOLVE_ON");
                    EditorUtility.SetDirty(m);
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[可见性] {mats.Count} 个材质 _distance = {value}");
        }

        /// <summary>
        /// 找龙的材质。
        ///
        /// ★ 不猜 FBX 路径 —— 材质是内嵌子资源，路径随导出设置变化。
        ///   直接从场景里龙身上的 SkinnedMeshRenderer 取，
        ///   这样无论材质来自哪个文件都能找到。
        /// </summary>
        private static System.Collections.Generic.List<Material> FindDragonMaterials()
        {
            var result = new System.Collections.Generic.List<Material>();

            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return result;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    // 只看 part3 下的飞行龙，避免误改 Dragon_root 那条
                    if (t.name != "SKM_Dragon") continue;
                    if (!HasAncestor(t, "龙环绕飞行6")) continue;

                    foreach (var r in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m != null && !result.Contains(m)) result.Add(m);
                        }
                    }
                }
            }

            // 场景里找不到（编辑态 vs 运行时差异）时，回退到全库搜索
            if (result.Count == 0) result = SearchAllAssets();

            return result;
        }

        private static bool HasAncestor(Transform t, string name)
        {
            var cur = t.parent;
            while (cur != null)
            {
                if (cur.name == name) return true;
                cur = cur.parent;
            }
            return false;
        }

        /// <summary>全库搜索带 _distance 属性的龙类材质（回退方案）</summary>
        private static System.Collections.Generic.List<Material> SearchAllAssets()
        {
            var result = new System.Collections.Generic.List<Material>();
            string[] guids = AssetDatabase.FindAssets("t:Material");

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.ToLower().Contains("bigdragon")
                    && !path.ToLower().Contains("cly")) continue;

                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(obj is Material m)) continue;
                    if (!m.HasProperty("_distance")) continue;
                    string lower = m.name.ToLower();
                    if (lower.Contains("dragon")
                        && !result.Contains(m))
                    {
                        result.Add(m);
                    }
                }
            }
            return result;
        }
    }
}
