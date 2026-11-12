using System.Collections;
using System.Collections.Generic;
using Greatwall.Flight;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sza.Ending
{
    public struct EndingViewerStatus : NetworkMessage { public byte flag; }
    public struct EndingStartVideo : NetworkMessage { public double at; }
    public struct EndingReturnToMenu : NetworkMessage { }
    public struct EndingCloseSession : NetworkMessage { }
    public struct EndingEnterScene : NetworkMessage { }

    /// <summary>Scene-independent barriers. Mirror alone changes network scenes.</summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class EndingSequenceCoordinator : MonoBehaviour
    {
        public const byte FlightBlack = 1, VideoReady = 2, VideoDone = 4, ReturnBlack = 8, MenuArrived = 16;
        private enum Phase { Flight, LoadingEnding, Preparing, Playing, Returning, LoadingMenu, Closing }
        public static EndingSequenceCoordinator Instance { get; private set; }
        public EndingSequenceSettings settings;
        private Phase phase;
        private bool serverRegistered, clientRegistered;
        private readonly Dictionary<int, byte> statuses = new Dictionary<int, byte>();
        private byte localFlags;
        private float nextStatus;
        private double startAt;
        private bool startReceived, returnReceived, closing;
        private FlightDirector flight;
        private FlightFadeController flightFade;
        private bool requestedFlightBlack;
        private readonly List<Behaviour> retiredServices = new List<Behaviour>();
        private UnityEngine.UI.Button[] menuButtons;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var config = Resources.Load<EndingSequenceSettings>("EndingSequenceSettings");
            if (config == null) return;
            if (Instance == null && (scene.path == config.gameplayScene || scene.path == config.endingScene))
            {
                GameObject host = NetworkManager.singleton != null ? NetworkManager.singleton.gameObject : new GameObject("Ending Sequence Session");
                var coordinator = host.GetComponent<EndingSequenceCoordinator>();
                if (coordinator == null) coordinator = host.AddComponent<EndingSequenceCoordinator>();
                coordinator.settings = config;
            }
            if (Instance != null) Instance.SceneEntered(scene.path);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            if (settings == null) settings = Resources.Load<EndingSequenceSettings>("EndingSequenceSettings");
            DontDestroyOnLoad(transform.root.gameObject);
        }

        private void SceneEntered(string path)
        {
            if (path == settings.endingScene)
            {
                phase = Phase.Preparing;
                foreach (var service in retiredServices) if (service != null) Destroy(service);
                retiredServices.Clear();
            }
            else if (path == settings.menuScene && phase != Phase.Flight)
            {
                phase = Phase.LoadingMenu;
                menuButtons = FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None);
                foreach (var button in menuButtons) button.interactable = false;
            }
        }

        private void Update()
        {
            if (settings == null || closing) return;
            EnsureHandlers();
            string path = SceneManager.GetActiveScene().path;
            if (path == settings.gameplayScene && phase == Phase.Flight) WatchFlight();
            var player = PanoramicEndingPlayer.Instance;
            if (path == settings.endingScene && player != null)
            {
                if (player.IsReady) localFlags |= VideoReady;
                if (player.IsFinished) localFlags |= VideoDone;
                if (startReceived) player.ScheduleStart(startAt);
                if (returnReceived)
                {
                    player.BeginReturn();
                    if (player.IsBlack) localFlags |= ReturnBlack;
                }
            }
            if (path == settings.menuScene && phase == Phase.LoadingMenu) localFlags |= MenuArrived;
            if (Time.unscaledTime >= nextStatus && localFlags != 0)
            {
                nextStatus = Time.unscaledTime + 0.25f;
                if (NetworkClient.isConnected) NetworkClient.Send(new EndingViewerStatus { flag = localFlags });
            }
            if (NetworkServer.active || !NetworkClient.active) AdvanceAuthority(path);
        }

        private void EnsureHandlers()
        {
            if (NetworkServer.active && !serverRegistered)
            {
                NetworkServer.RegisterHandler<EndingViewerStatus>(OnStatus);
                NetworkServer.OnConnectedEvent += OnServerConnected;
                NetworkServer.OnDisconnectedEvent += OnServerDisconnected;
                serverRegistered = true;
            }
            if (!NetworkServer.active && serverRegistered)
            {
                NetworkServer.OnConnectedEvent -= OnServerConnected;
                NetworkServer.OnDisconnectedEvent -= OnServerDisconnected;
                serverRegistered = false;
            }
            if (NetworkClient.active && !clientRegistered)
            {
                NetworkClient.RegisterHandler<EndingStartVideo>(OnStartVideo);
                NetworkClient.RegisterHandler<EndingReturnToMenu>(OnReturn);
                NetworkClient.RegisterHandler<EndingCloseSession>(OnCloseSession);
                NetworkClient.RegisterHandler<EndingEnterScene>(OnEnterScene);
                clientRegistered = true;
            }
            if (!NetworkClient.active) clientRegistered = false;
        }

        private void WatchFlight()
        {
            if (flight == null) flight = FindFirstObjectByType<FlightDirector>();
            if (flight == null || flight.CurrentPhase != FlightPhase.Ending) return;
            if (!requestedFlightBlack)
            {
                requestedFlightBlack = true;
                foreach (var loop in FindObjectsByType<FlightExperienceLoop>(FindObjectsInactive.Include, FindObjectsSortMode.None)) loop.enabled = false;
                flightFade = FindFirstObjectByType<FlightFadeController>();
                if (flightFade == null) flightFade = gameObject.AddComponent<FlightFadeController>();
                flightFade.FadeOut(flight.FadeOutDuration);
            }
            if (flightFade != null && flightFade.IsBlack) localFlags |= FlightBlack;
        }

        private void OnStatus(NetworkConnectionToClient connection, EndingViewerStatus message)
        {
            statuses[connection.connectionId] = message.flag;
            // A viewer who joined during playback still watches from frame zero.
            if (phase == Phase.Playing && (message.flag & VideoReady) != 0 && (message.flag & VideoDone) == 0)
                connection.Send(new EndingStartVideo { at = startAt });
        }

        private void OnServerConnected(NetworkConnectionToClient connection)
        {
            statuses.Remove(connection.connectionId);
            // Admission closes once all current viewers have completed the video.
            if (phase >= Phase.Returning) connection.Disconnect();
        }

        private void OnServerDisconnected(NetworkConnectionToClient connection) { statuses.Remove(connection.connectionId); }

        private bool AllHave(byte flag, bool requirePlayer = false)
        {
            if (!NetworkServer.active) return (localFlags & flag) != 0;
            int count = 0;
            foreach (var connection in NetworkServer.connections.Values)
            {
                if (connection == null || !connection.isAuthenticated || (requirePlayer && connection.identity == null)) continue;
                count++;
                byte value;
                if (!statuses.TryGetValue(connection.connectionId, out value) || (value & flag) == 0) return false;
            }
            // Dedicated servers have no local HMD and no local video to report.
            return count > 0 || (Application.isBatchMode && phase != Phase.Flight);
        }

        private void AdvanceAuthority(string path)
        {
            if (phase == Phase.Flight && requestedFlightBlack && AllHave(FlightBlack, true))
            {
                phase = Phase.LoadingEnding;
                if (NetworkServer.active) NetworkServer.SendToAll(new EndingEnterScene());
                RetireGameplay();
                ChangeScene(settings.endingScene);
            }
            else if (path == settings.endingScene && phase == Phase.Preparing && AllHave(VideoReady))
            {
                phase = Phase.Playing;
                startAt = Now + settings.networkStartLead;
                if (NetworkServer.active) NetworkServer.SendToAll(new EndingStartVideo { at = startAt });
                else OnStartVideo(new EndingStartVideo { at = startAt });
            }
            else if (phase == Phase.Playing && AllHave(VideoDone))
            {
                phase = Phase.Returning;
                if (NetworkManager.singleton != null) NetworkManager.singleton.autoCreatePlayer = false;
                if (NetworkServer.active) NetworkServer.SendToAll(new EndingReturnToMenu());
                else OnReturn(new EndingReturnToMenu());
            }
            else if (phase == Phase.Returning && AllHave(ReturnBlack))
            {
                phase = Phase.LoadingMenu;
                ChangeScene(settings.menuScene);
            }
            else if (phase == Phase.LoadingMenu && path == settings.menuScene && AllHave(MenuArrived))
            {
                phase = Phase.Closing;
                if (NetworkServer.active) NetworkServer.SendToAll(new EndingCloseSession());
                StartCoroutine(CloseHostAfterDelivery());
            }
        }

        public static double Now => NetworkClient.active || NetworkServer.active ? NetworkTime.time : Time.unscaledTimeAsDouble;

        private void OnStartVideo(EndingStartVideo message)
        {
            if (startReceived) return;
            startReceived = true;
            startAt = System.Math.Max(message.at, Now);
        }

        private void OnReturn(EndingReturnToMenu message)
        {
            returnReceived = true;
            if (NetworkManager.singleton != null) NetworkManager.singleton.autoCreatePlayer = false;
        }

        private void OnCloseSession(EndingCloseSession message)
        {
            if (!NetworkServer.active) CloseSession();
        }

        private void OnEnterScene(EndingEnterScene message) { RetireGameplay(); }

        private void RetireGameplay()
        {
            // Reliable ordering drains gameplay snapshots before switching scenes.
            foreach (var component in GetComponents<MonoBehaviour>())
            {
                if (!(component is NetworkGameplayCoordinator) && !(component is NetworkTimelineActorCoordinator) &&
                    !(component is Sza.Telescope.TelescopeVideoManager)) continue;
                component.enabled = false;
                if (!retiredServices.Contains(component)) retiredServices.Add(component);
            }
            foreach (var rider in FindObjectsByType<DragonRideController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            { rider.Unmount(); rider.enabled = false; }
            if (flight != null) { flight.BindPlayerRoot(null); flight.enabled = false; }
        }

        private void ChangeScene(string path)
        {
            if (NetworkServer.active) NetworkManager.singleton.ServerChangeScene(path);
            else SceneManager.LoadSceneAsync(path);
        }

        private IEnumerator CloseHostAfterDelivery()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            CloseSession();
        }

        private void CloseSession()
        {
            if (closing) return;
            closing = true;
            if (menuButtons != null) foreach (var button in menuButtons) if (button != null) button.interactable = true;
            var manager = NetworkManager.singleton;
            if (manager != null)
            {
                // We are already in Menu on every endpoint before disconnecting.
                manager.offlineScene = "";
                if (NetworkServer.active && NetworkClient.active) manager.StopHost();
                else if (NetworkServer.active) manager.StopServer();
                else if (NetworkClient.active) manager.StopClient();
                Destroy(manager.gameObject);
            }
            else Destroy(gameObject);
        }

        private void OnDestroy()
        {
            NetworkServer.OnConnectedEvent -= OnServerConnected;
            NetworkServer.OnDisconnectedEvent -= OnServerDisconnected;
            if (Instance == this) Instance = null;
        }
    }
}
