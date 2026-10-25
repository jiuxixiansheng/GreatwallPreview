using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Greatwall.Flight.EditorTools
{
    /// <summary>
    /// 【一次性工具】为飞行龙正确创建 Animator Controller。
    ///
    /// 之前的 YAML 手写版本结构不对（Statemachine for layer 'Base Layer' is missing），
    /// 用 UnityEditor.Animations API 创建可保证结构正确。
    ///
    /// 用法：菜单 Window → Flight → Setup Flight Animator
    /// </summary>
    public static class FlightAnimatorSetup
    {
        private const string DragonPath =
            "Assets/sza/model/bigdragon/龙环绕飞行6.fbx";
        private const string ControllerPath =
            "Assets/Flight/AC_dragon_flight.controller";

        [MenuItem("Window/Flight/Setup Flight Animator")]
        public static void Setup()
        {
            var dragon = AssetDatabase.LoadAssetAtPath<GameObject>(DragonPath);
            if (dragon == null)
            {
                Debug.LogError($"[FlightSetup] 找不到 {DragonPath}");
                return;
            }

            // 读取 FBX 里的动画 clip
            var clips = AssetDatabase.LoadAllAssetsAtPath(DragonPath);
            AnimationClip clip = null;
            foreach (var c in clips)
            {
                if (c is AnimationClip ac && !ac.name.Contains("__preview__"))
                {
                    clip = ac;
                    Debug.Log($"[FlightSetup] 找到 clip: {ac.name} ({ac.length:F1}s)");
                    break;
                }
            }

            if (clip == null)
            {
                Debug.LogError("[FlightSetup] FBX 里没有可用动画 clip。" +
                               "请检查 Model Import Settings 的 Import Animation 是否勾选。");
                return;
            }

            // 创建 Controller
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            var sm = controller.layers[0].stateMachine;
            var state = sm.AddState("FlyForward");
            state.motion = clip;

            // 确保循环播放
            foreach (var s in sm.states)
            {
                foreach (var tr in s.state.transitions)
                {
                    tr.hasExitTime = false;
                }
            }

            sm.defaultState = state;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[FlightSetup] ✅ 已创建 {ControllerPath}" +
                      $"，状态 FlyForward，clip={clip.name}");

            // 自动赋给场景里的龙
            AssignToDragon(controller);
        }

        private static void AssignToDragon(RuntimeAnimatorController controller)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "龙环绕飞行6") continue;

                    var animator = t.GetComponent<Animator>();
                    if (animator == null)
                    {
                        Debug.LogWarning($"[FlightSetup] {t.name} 上没有 Animator 组件");
                        continue;
                    }

                    animator.runtimeAnimatorController = controller;
                    EditorUtility.SetDirty(animator);
                    Debug.Log($"[FlightSetup] ✅ 已把 Controller 赋给 {t.name}");
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
        }
    }
}
