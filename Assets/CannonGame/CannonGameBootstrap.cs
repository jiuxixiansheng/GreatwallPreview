using UnityEngine;
using UnityEngine.SceneManagement;

namespace Greatwall.CannonGame
{
    internal static class CannonGameBootstrap
    {
        // 主场景禁止用 Unity 保存，炮台小游戏只能在加载后动态挂到 JiPao 上。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (transform.gameObject.scene != scene || transform.name != "JiPao") continue;
                if (transform.GetComponent<CannonGame>() == null) transform.gameObject.AddComponent<CannonGame>();
                return;
            }
        }
    }
}
