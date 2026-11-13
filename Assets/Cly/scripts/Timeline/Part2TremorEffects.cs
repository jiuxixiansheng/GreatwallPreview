using System.Collections.Generic;
using Mirror;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Greatwall.VRAnimation.Timeline
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayableDirector))]
    public sealed class Part2TremorEffects : MonoBehaviour
    {
        private const float RepairHintSeconds = 10f;
        [SerializeField, Min(0f)] private float shakeDuration = 8f;
        [SerializeField, Min(0f)] private float vrShakeMeters = 0.009f;
        [SerializeField, Min(0f)] private float desktopShakeMeters = 0.035f;
        [SerializeField] private DecalProjector[] cracks;
        [SerializeField] private ParticleSystem smokePrefab;
        [SerializeField] private SignalAsset revealSignal;

        private readonly List<ParticleSystem> smoke = new List<ParticleSystem>();
        private PlayableDirector director;
        private float[] originalFade;
        private bool revealed;
        private double revealTime = double.NaN;
        private Transform cameraTransform;
        private Transform cameraParent;
        private Transform shakePivot;
        private int cameraSibling;
        private double shakeStartedAt;
        private float shakeMeters;
        private Canvas repairHint;
        private float repairHintUntil;
        private bool repairPromptGlowing;

        private void Awake()
        {
            director = GetComponent<PlayableDirector>();
            originalFade = new float[cracks == null ? 0 : cracks.Length];
            for (int i = 0; i < originalFade.Length; i++)
                if (cracks[i] != null) originalFade[i] = cracks[i].fadeFactor;

            if (revealSignal == null || !(director.playableAsset is TimelineAsset timeline)) return;
            foreach (TrackAsset track in timeline.GetOutputTracks())
                foreach (IMarker marker in track.GetMarkers())
                    if (marker is SignalEmitter emitter && emitter.asset == revealSignal)
                    {
                        revealTime = emitter.time;
                        return;
                    }
        }

        private void OnEnable()
        {
            director.stopped += OnTimelineStopped;
            SetCracksVisible(false);
        }

        private void OnDisable()
        {
            director.stopped -= OnTimelineStopped;
            StopShake();
            ClearSmoke();
            ClearRepairHint();
            repairPromptGlowing = false;
            SetCracksVisible(true);
        }

        private void OnTimelineStopped(PlayableDirector _)
        {
            StopShake();
            ClearSmoke();
            ClearRepairHint();
            repairPromptGlowing = false;
            SetCracksVisible(false);
        }

        public void BeginShake()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || shakeDuration <= 0f) return;
            StopShake();

            bool headsetActive = XRSettings.isDeviceActive;
            Camera view = FindPlayerView(headsetActive);
            if (view == null || !view.isActiveAndEnabled) return;

            shakeMeters = headsetActive || view.stereoEnabled ? vrShakeMeters : desktopShakeMeters;
            if (shakeMeters <= 0f) return;

            // 给本机镜头单独增加位移，保留 XR 追踪驱动的相机本地姿态。
            cameraTransform = view.transform;
            cameraParent = cameraTransform.parent;
            cameraSibling = cameraTransform.GetSiblingIndex();
            var pivotObject = new GameObject("Timeline Camera Tremor");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(pivotObject, view.gameObject.scene);
            shakePivot = pivotObject.transform;
            shakePivot.SetParent(cameraParent, false);
            shakePivot.SetSiblingIndex(cameraSibling);
            cameraTransform.SetParent(shakePivot, false);
            shakeStartedAt = director.time;
        }

        private static Camera FindPlayerView(bool headsetActive)
        {
            if (NetworkClient.active)
            {
                // 联机只从这台设备拥有的玩家 Rig 取相机，不能选到其他玩家。
                var player = NetworkClient.localPlayer;
                if (player == null) return null;
                var rig = player.GetComponentInChildren<XROrigin>(true);
                if (rig != null && rig.Camera != null) return rig.Camera;
                var setup = player.GetComponent<PlayerNetworkSetup>();
                return !headsetActive && setup != null ? setup.localCamera : null;
            }

            Camera main = Camera.main;
            var mainRig = main != null ? main.GetComponentInParent<XROrigin>() : null;
            if (mainRig != null && mainRig.Camera == main) return main;

            foreach (var rig in FindObjectsByType<XROrigin>(FindObjectsSortMode.None))
                if (rig.isActiveAndEnabled && rig.Camera != null && rig.Camera.isActiveAndEnabled)
                    return rig.Camera;

            // 只有桌面预览允许用普通 Main Camera 兜底。
            return headsetActive ? null : main;
        }

        private void LateUpdate()
        {
            UpdateRepairHint();
            if (shakePivot == null) return;
            float elapsed = (float)(director.time - shakeStartedAt);
            if (cameraTransform == null || elapsed < 0f || elapsed >= shakeDuration)
            {
                StopShake();
                return;
            }

            float envelope = Mathf.SmoothStep(0f, 1f, elapsed / 0.3f) *
                             Mathf.SmoothStep(0f, 1f, (shakeDuration - elapsed) / 1.2f);
            Vector3 displacement = cameraTransform.right * Mathf.Sin(elapsed * 16f) +
                                   cameraTransform.up * (0.4f * Mathf.Sin(elapsed * 23f));
            displacement *= shakeMeters * envelope;
            shakePivot.localPosition = cameraParent != null
                ? cameraParent.InverseTransformVector(displacement) : displacement;
        }

        private void StopShake()
        {
            if (shakePivot == null) return;
            if (cameraTransform != null && cameraTransform.parent == shakePivot)
            {
                cameraTransform.SetParent(cameraParent, false);
                cameraTransform.SetSiblingIndex(cameraSibling);
            }
            Destroy(shakePivot.gameObject);
            shakePivot = null;
            cameraTransform = null;
            cameraParent = null;
        }

        public void RevealCracks()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || revealed) return;
            SetCracksVisible(true);
            // 迟加入或跳到后面的玩家也要看见裂缝，但不补播旧烟雾。
            if (smokePrefab == null || director.time - revealTime > 1d) return;

            foreach (DecalProjector crack in cracks)
            {
                if (crack == null) continue;
                Transform anchor = crack.transform;
                Vector3 position = anchor.position - anchor.forward * 0.06f;
                ParticleSystem puff = Instantiate(smokePrefab, position, Quaternion.LookRotation(-anchor.forward), transform);
                puff.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = puff.main;
                main.loop = false;
                main.playOnAwake = false;
                main.scalingMode = ParticleSystemScalingMode.Local;
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.65f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.65f);
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.2f);
                main.stopAction = ParticleSystemStopAction.Destroy;
                smoke.Add(puff);
                puff.Play();
            }
        }

        public void SetRepairPromptGlowing(bool glowing)
        {
            if (!Application.isPlaying || !isActiveAndEnabled || repairPromptGlowing == glowing) return;
            repairPromptGlowing = glowing;
            if (glowing)
                repairHintUntil = Time.unscaledTime + RepairHintSeconds;
        }

        private void UpdateRepairHint()
        {
            if (repairHintUntil <= 0f) return;
            if (Time.unscaledTime >= repairHintUntil)
            {
                ClearRepairHint();
                return;
            }

            if (repairHint != null && repairHint.worldCamera != null && repairHint.worldCamera.isActiveAndEnabled)
                return;

            if (repairHint != null) Destroy(repairHint.gameObject);
            repairHint = null;
            Camera view = FindPlayerView(XRSettings.isDeviceActive);
            if (view == null || !view.isActiveAndEnabled) return;
            Font font = Resources.Load<Font>("STXINGKA");
            if (font == null)
            {
                ClearRepairHint();
                return;
            }

            // 和越界提示一样，把世界空间 Canvas 挂到本机头显相机上，双眼都能看到。
            var canvasObject = new GameObject("Crack Repair Hint", typeof(RectTransform), typeof(Canvas));
            var rect = (RectTransform)canvasObject.transform;
            rect.SetParent(view.transform, false);
            rect.localPosition = new Vector3(0f, -0.20f, 1.35f);
            rect.localScale = Vector3.one * 0.0011f;
            rect.sizeDelta = new Vector2(450f, 120f);
            repairHint = canvasObject.GetComponent<Canvas>();
            repairHint.renderMode = RenderMode.WorldSpace;
            repairHint.worldCamera = view;
            repairHint.sortingOrder = 30;

            var label = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Outline));
            var labelRect = (RectTransform)label.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var text = label.GetComponent<Text>();
            text.font = font;
            text.text = "靠近裂缝，\n扣动扳机开始修复";
            text.fontSize = 40;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            var outline = label.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }

        private void ClearRepairHint()
        {
            repairHintUntil = 0f;
            if (repairHint != null) Destroy(repairHint.gameObject);
            repairHint = null;
        }

        private void SetCracksVisible(bool visible)
        {
            revealed = visible;
            // 只控制贴花透明度，修复脚本仍负责启用状态、遮罩和交互解锁。
            for (int i = 0; i < originalFade.Length; i++)
                if (cracks[i] != null) cracks[i].fadeFactor = visible ? originalFade[i] : 0f;
        }

        private void ClearSmoke()
        {
            foreach (ParticleSystem puff in smoke)
                if (puff != null) Destroy(puff.gameObject);
            smoke.Clear();
        }
    }
}
