using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;

namespace Greatwall.Flight.EditorTools
{
    /// <summary>
    /// 【一次性工具】用 API 直接给 XRGeneralSettings 添加 OpenXRLoader。
    ///
    /// 背景：ProjectSettings/XRPackageSettings.asset 缺失，
    /// 导致 Window → XR Plug-in Management 面板里看不到 OpenXR Loader。
    /// 手动在面板里勾选通常能生成，但有时点了没反应。
    /// 这个工具用 XRManagerSettings.CreateLoader 直接加，最可靠。
    ///
    /// 用法：菜单 Window → Flight → Enable OpenXR Loader
    /// </summary>
    public static class FlightXREnableOpenXR
    {
        private const string SettingsPath = "ProjectSettings/XRPackageSettings.asset";

        [MenuItem("Window/Flight/Enable OpenXR Loader")]
        public static void Enable()
        {
            // 1. 找到 XRGeneralSettingsPerBuildTarget
            var perBuildTarget =
                AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");

            XRGeneralSettingsPerBuildTarget settingsPerBuild = null;
            if (perBuildTarget.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(perBuildTarget[0]);
                settingsPerBuild =
                    AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            }

            if (settingsPerBuild == null)
            {
                Debug.LogError("[XR] 找不到 XRGeneralSettingsPerBuildTarget");
                return;
            }

            // 2. 取 Standalone 的 XRGeneralSettings
            var standalone = settingsPerBuild.SettingsForBuildTarget(
                BuildTargetGroup.Standalone);

            if (standalone == null)
            {
                Debug.LogError("[XR] 取不到 Standalone 的 XRGeneralSettings");
                return;
            }

            var assigned = standalone.AssignedSettings;
            if (assigned == null)
            {
                Debug.LogError("[XR] AssignedSettings 为空");
                return;
            }

            // 3. 检查现有 Loader
            var loaders = assigned.activeLoaders;
            Debug.Log($"[XR] 当前 Standalone Loader 数: " +
                      (loaders == null ? 0 : loaders.Count));

            bool hasOpenXR = false;
            if (loaders != null)
            {
                foreach (var l in loaders)
                {
                    if (l == null) continue;
                    Debug.Log($"[XR]   - {l.GetType().FullName}");
                    if (l.GetType().Name.Contains("OpenXR")) hasOpenXR = true;
                }
            }

            if (hasOpenXR)
            {
                Debug.Log("[XR] ✅ OpenXR Loader 已存在，无需重复添加");
                EnsureInitOnStart(standalone);
                return;
            }

            // 4. 创建 OpenXRLoader 并加入列表
            var loaderType = System.Type.GetType(
                "UnityEngine.XR.OpenXR.OpenXRLoader, Unity.XR.OpenXR");

            if (loaderType == null)
            {
                Debug.LogError("[XR] 找不到 OpenXRLoader 类型");
                return;
            }

            // 手动实例化 Loader
            var loader = (XRLoader)System.Activator.CreateInstance(loaderType);
            if (loader == null)
            {
                Debug.LogError("[XR] 创建 OpenXRLoader 实例失败");
                return;
            }

            // TryAddLoader(XRLoader loader, int index)，index = -1 表示追加到最后
            bool added = assigned.TryAddLoader(loader, -1);
            if (!added)
            {
                Debug.LogError("[XR] TryAddLoader 返回 false");
                return;
            }

            Debug.Log($"[XR] ✅ 已添加 Loader: {loader.GetType().FullName}");

            // 5. 启用 Initialize XR on Startup
            EnsureInitOnStart(standalone);

            // 6. 保存
            EditorUtility.SetDirty(standalone);
            EditorUtility.SetDirty(settingsPerBuild);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool fileExists = File.Exists(SettingsPath);
            Debug.Log($"[XR] XRPackageSettings.asset: " +
                      (fileExists ? "✅ 已生成" : "⚠️ 仍未生成（面板里勾一下即可）"));

            Debug.Log("[XR] 请重新打开 Window → XR Plug-in Management 确认");
        }

        private static void EnsureInitOnStart(XRGeneralSettings settings)
        {
            if (settings.InitManagerOnStart)
            {
                Debug.Log("[XR] Initialize XR on Startup 已是开启状态");
                return;
            }

            settings.InitManagerOnStart = true;
            Debug.Log("[XR] ✅ 已开启 Initialize XR on Startup");
        }
    }
}
