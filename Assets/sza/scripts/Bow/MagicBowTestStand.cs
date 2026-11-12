using System.Collections;
using Mirror;
using UnityEngine;

namespace Sza.Bow
{
    /// <summary>Chapter-local test bow, visible in the editor and spawned once by the host at runtime.</summary>
    public sealed class MagicBowTestStand : MonoBehaviour
    {
        public NetworkMagicBow bowPrefab;
        public GameObject placementPreview;
        private NetworkMagicBow instance;
        public NetworkMagicBow Instance => instance;

        private void Awake()
        {
            if (placementPreview != null) placementPreview.SetActive(false);
        }
        private void OnEnable()
        {
            if (Application.isPlaying) StartCoroutine(SpawnAfterNetworkStartup());
        }
        private IEnumerator SpawnAfterNetworkStartup()
        {
            // Menu's NetworkSceneStarter runs Start before this continuation, including when testing part1 directly.
            yield return null;
            if (NetworkClient.active && !NetworkServer.active || instance != null || bowPrefab == null) yield break;
            instance = Instantiate(bowPrefab, transform.position, transform.rotation);
            instance.name = "Bow_Test · 第一章开场测试弓";
            instance.PlaceForPickup(transform.position, transform.rotation);
            if (NetworkServer.active) NetworkServer.Spawn(instance.gameObject);
        }
        private void OnDisable()
        {
            StopAllCoroutines();
            if (instance == null) return;
            if (NetworkServer.active && instance.netId != 0) NetworkServer.Destroy(instance.gameObject);
            else Destroy(instance.gameObject);
            instance = null;
        }
    }
}
