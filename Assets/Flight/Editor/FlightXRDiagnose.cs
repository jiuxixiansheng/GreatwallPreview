using System.IO;
using UnityEditor;
using UnityEngine;

namespace Greatwall.Flight.EditorTools
{
    /// <summary>
    /// 【一次性工具】诊断并修复 XR Plug-in Management 里看不到 OpenXR Loader 的问题。
    ///
    /// 用法：
    ///   Window → Flight → Diagnose XR Setup     （先诊断）
    ///   Window → Flight → Open XR Plug-in Panel  （打开面板）
    /// </summary>
    public static class FlightXRDiagnose
    {
        private const string SettingsPath = "ProjectSettings/XRPackageSettings.asset";

        [MenuItem("Window/Flight/Diagnose XR Setup")]
        public static void Diagnose()
        {
            Debug.Log("========== XR 环境诊断 ==========");

            // 1. 编译状态（编译错误会阻止 XR 插件加载）
            bool compiling = EditorApplication.isCompiling;
            Debug.Log($"[1] 正在编译: {(compiling ? "是 ⚠️" : "否")}");

            // 2. 设置文件
            bool hasSettings = File.Exists(SettingsPath);
            Debug.Log($"[2] XRPackageSettings.asset: " +
                      (hasSettings ? "✅ 存在" : "❌ 缺失（面板读不到 Loader 的原因）"));

            // 3. 包状态
            Debug.Log("[3] XR 相关包:");
            foreach (var name in new[]
            {
                "com.unity.xr.openxr",
                "com.unity.xr.management",
                "com.unity.xr.mock-hmd",
                "com.unity.xr.interaction.toolkit"
            })
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(
                    $"Packages/{name}");
                Debug.Log($"    {name}: " +
                          (info != null ? $"已装 {info.version}" : "❌ 未装"));
            }

            // 4. OpenXR 类型是否可加载
            var openxrType = System.Type.GetType(
                "UnityEngine.XR.OpenXR.OpenXRLoader, Unity.XR.OpenXR");
            Debug.Log($"[4] OpenXRLoader 类型: " +
                      (openxrType != null
                          ? $"✅ 可加载（{openxrType.FullName}）"
                          : "❌ 找不到 → 包没编译成功或有编译错误"));

            // 5. 结论
            Debug.Log("========== 诊断结论 ==========");
            if (openxrType == null)
            {
                Debug.LogWarning(
                    "OpenXR 类型加载失败。\n" +
                    "最可能原因：项目有编译错误，导致 OpenXR 包无法编译。\n" +
                    "请先清空 Console 里的所有红色错误再试。");
            }
            else if (!hasSettings)
            {
                Debug.LogWarning(
                    "OpenXR 可用，但 XRPackageSettings.asset 缺失。\n" +
                    "点下面的菜单打开面板，然后勾选 OpenXR Loader 即可生成。");
            }
            else
            {
                Debug.Log("设置看起来正常。若面板仍无 OpenXR，" +
                          "试试勾选 Mock HMD 再取消，触发面板刷新。");
            }
        }

        [MenuItem("Window/Flight/Open XR Plug-in Panel")]
        public static void OpenPanel()
        {
            EditorApplication.ExecuteMenuItem("Window/XR Plug-in Management");
            Debug.Log("[XR] 已打开 XR Plug-in Management 面板");
        }

        [MenuItem("Window/Flight/Toggle Mock HMD Dependency")]
        public static void ToggleMockHMD()
        {
            Debug.LogWarning(
                "[XR] 关于 Mock HMD：\n\n" +
                "Packages/manifest.json 里 com.unity.xr.mock-hmd 是直接依赖，\n" +
                "它会注册自己的 XR Loader。面板里显示的 'Mock HMD' 就是它。\n\n" +
                "这不影响 OpenXR —— 两者可以共存，面板里应该同时列出。\n" +
                "如果面板只显示 Mock HMD，说明 OpenXR 那边没加载成功。\n\n" +
                "要移除 Mock HMD：\n" +
                "  1. 打开 Packages/manifest.json\n" +
                "  2. 删掉 \"com.unity.xr.mock-hmd\": \"1.5.0-exp.3\"\n" +
                "  3. 等 Package Manager 重新解析\n\n" +
                "（但移除后编辑器的模拟 HMD 会失效，" +
                "XR Device Simulator 还能用）");
        }
    }
}
