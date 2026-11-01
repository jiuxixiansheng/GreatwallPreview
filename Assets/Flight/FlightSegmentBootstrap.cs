using UnityEngine;
using UnityEngine.SceneManagement;

namespace Greatwall.Flight
{
    /// <summary>
    /// 兜底看守：骑龙段落的 FlightSegmentStage / DragonBoardingGate / 骑龙都挂在 part3 下，
    /// 而团队 10-27 联机版把 part3 的 m_IsActive 关掉了。
    /// 根节点关闭时 MonoBehaviour 的 Update 不会执行 → 段落永远不会开始。
    /// 这里在场景加载后放一个看守：章节索引到达段落目标章节时把 stage 所在根节点打开，
    /// 其余时间不做任何事（part3 若被团队重新启用，本组件自然空转）。
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class FlightSegmentBootstrap : MonoBehaviour
    {
        private const float PollInterval = 0.3f;
        private const string WatcherName = "[FlightSegmentBootstrap]";

        private float _timer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            // 工程关闭了 Domain Reload，静态委托可能残留，先减后加避免重复注册
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 看守放在场景里而不是 DontDestroyOnLoad：关闭 Domain Reload 时
            // 跨 Play 残留的常驻物体会拿不到 Update。
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root != null && root.name == WatcherName) return;
            }

            var go = new GameObject(WatcherName);
            go.AddComponent<FlightSegmentBootstrap>();
        }

        private void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = PollInterval;

            var stage = FindFirstObjectByType<FlightSegmentStage>(FindObjectsInactive.Include);
            if (stage == null) return;

            Transform root = stage.transform;
            while (root.parent != null) root = root.parent;
            if (root.gameObject.activeSelf) return;   // 已激活，交给 FlightSegmentStage

            if (ReadChapterIndex() != stage.ChapterIndex) return;
            root.gameObject.SetActive(true);
        }

        private static int ReadChapterIndex()
        {
            foreach (var mb in FindObjectsOfType<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Name != "StoryTimelineManager") continue;
                var field = mb.GetType().GetField("currentChapterIndex",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (field != null && field.GetValue(mb) is int index) return index;
            }
            return -1;
        }
    }
}
