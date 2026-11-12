using Greatwall.Flight;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Video;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace Sza.Ending
{
    [DefaultExecutionOrder(30000)]
    public sealed class PanoramicEndingPlayer : MonoBehaviour
    {
        public static PanoramicEndingPlayer Instance { get; private set; }
        public EndingSequenceSettings settings;
        public VideoPlayer videoPlayer;
        public Renderer panorama;
        public Material panoramaMaterial, blackoutMaterial;
        public Renderer introductionPhoto;
        public Material photoDissolveMaterial;
        public Transform viewCenter;
        public Camera fallbackCamera;
        [Range(8, 31)] public int presentationLayer = 30;
        public bool IsReady => firstFrame && viewCamera != null;
        public bool IsFinished { get; private set; }
        public bool IsBlack => opacity >= 0.999f;
        public double PlaybackTime => videoPlayer != null ? videoPlayer.time : 0d;
        public bool IsShowingPhoto => introductionPhoto != null && introductionPhoto.enabled;

        private Camera viewCamera;
        private Transform rig;
        private RenderTexture texture;
        private Material sphereMaterial, fadeMaterial;
        private Material photoMaterial;
        private GameObject fadeQuad;
        private bool firstFrame, scheduled, started, returning, cameraCentered;
        private double startAt;
        private float opacity = 1f;
        private int oldMask;
        private CameraClearFlags oldClear;
        private Color oldBackground;
        private bool oldPost;
        private ScriptableRenderer oldRenderer;

        private void Awake()
        {
            Instance = this;
            if (settings == null) settings = Resources.Load<EndingSequenceSettings>("EndingSequenceSettings");
            if (settings == null || settings.video == null || videoPlayer == null || panorama == null ||
                panoramaMaterial == null || blackoutMaterial == null)
            {
                Debug.LogError("[Ending] Ending video or presentation references are missing.", this);
                enabled = false;
                return;
            }
            panorama.enabled = false;
            sphereMaterial = new Material(panoramaMaterial);
            fadeMaterial = new Material(blackoutMaterial);
            panorama.sharedMaterial = sphereMaterial;
            if (introductionPhoto != null && photoDissolveMaterial != null && settings.introductionPhoto != null)
            {
                photoMaterial = new Material(photoDissolveMaterial);
                photoMaterial.SetTexture("_MainTex", settings.introductionPhoto);
                photoMaterial.SetFloat("_Progress", 0f);
                introductionPhoto.sharedMaterial = photoMaterial;
                introductionPhoto.enabled = false;
            }
            sphereMaterial.SetFloat("_YawDegrees", settings.panoramaYaw);
            texture = new RenderTexture((int)settings.video.width, (int)settings.video.height, 0, RenderTextureFormat.ARGB32)
            { name = "Ending Video (runtime)", useMipMap = false, autoGenerateMips = false, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            texture.Create();
            sphereMaterial.SetTexture("_VideoTex", texture);
            videoPlayer.playOnAwake = false;
            videoPlayer.isLooping = false;
            videoPlayer.skipOnDrop = false;
            videoPlayer.waitForFirstFrame = true;
            videoPlayer.source = VideoSource.VideoClip;
            videoPlayer.clip = settings.video;
            videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            videoPlayer.targetTexture = texture;
            videoPlayer.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            videoPlayer.sendFrameReadyEvents = true;
            videoPlayer.prepareCompleted += OnPrepared;
            videoPlayer.frameReady += OnFirstFrame;
            videoPlayer.loopPointReached += OnFinished;
            videoPlayer.errorReceived += OnError;
            if (settings.video.audioTrackCount > 0)
            {
                var audio = GetComponent<AudioSource>();
                videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
                videoPlayer.controlledAudioTrackCount = 1;
                videoPlayer.SetTargetAudioSource(0, audio);
            }
            else videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            Application.onBeforeRender += FollowHead;
        }

        private void Start() { ResolveViewer(); videoPlayer.Prepare(); }
        private void OnPrepared(VideoPlayer source) { source.Play(); }

        private void OnFirstFrame(VideoPlayer source, long frame)
        {
            if (firstFrame) return;
            source.Pause();
            source.sendFrameReadyEvents = false;
            firstFrame = true;
            Debug.Log("[Ending] First video frame prepared; waiting for viewers.", this);
        }

        public void ScheduleStart(double at)
        {
            if (scheduled) return;
            scheduled = true; startAt = at;
        }

        public void BeginReturn() { returning = true; }

        private void Update()
        {
            if (returning)
                opacity = settings.returnFadeSeconds > 0f ? Mathf.MoveTowards(opacity, 1f, Time.unscaledDeltaTime / settings.returnFadeSeconds) : 1f;
            else if (scheduled && IsReady && EndingSequenceCoordinator.Now >= startAt)
            {
                double elapsed = EndingSequenceCoordinator.Now - startAt;
                opacity = settings.revealSeconds > 0f ? Mathf.MoveTowards(opacity, 0f, Time.unscaledDeltaTime / settings.revealSeconds) : 0f;
                bool photoActive = UpdatePhoto(elapsed - settings.revealSeconds);
                // Frame zero remains paused until the entire photo introduction ends.
                if (!photoActive && opacity <= 0f && !started)
                {
                    panorama.enabled = true; started = true; videoPlayer.Play();
                }
            }
            if (fadeMaterial != null) fadeMaterial.SetFloat("_Opacity", opacity);
        }

        private void LateUpdate() { ResolveViewer(); FollowHead(); }

        private void ResolveViewer()
        {
            if (viewCamera != null) return;
            bool networked = NetworkClient.active || NetworkServer.active;
            if (networked)
            {
                if (NetworkClient.localPlayer == null) return;
                rig = NetworkClient.localPlayer.transform;
                viewCamera = rig.GetComponentInChildren<Camera>(true);
            }
            else
            {
                viewCamera = fallbackCamera;
                rig = fallbackCamera != null ? fallbackCamera.transform.root : null;
                if (fallbackCamera != null)
                {
                    fallbackCamera.enabled = true;
                    var listener = fallbackCamera.GetComponent<AudioListener>();
                    if (listener != null) listener.enabled = true;
                }
            }
            if (viewCamera == null || rig == null) return;
            oldMask = viewCamera.cullingMask; oldClear = viewCamera.clearFlags; oldBackground = viewCamera.backgroundColor;
            viewCamera.cullingMask = 1 << presentationLayer;
            viewCamera.clearFlags = CameraClearFlags.SolidColor; viewCamera.backgroundColor = Color.black;
            var additional = viewCamera.GetComponent<UniversalAdditionalCameraData>();
            if (additional != null)
            {
                oldPost = additional.renderPostProcessing;
                oldRenderer = additional.scriptableRenderer;
                additional.renderPostProcessing = false;
                int index = settings.PresentationRendererIndex();
                if (index >= 0) additional.SetRenderer(index);
            }
            if (fallbackCamera != viewCamera && fallbackCamera != null)
            {
                fallbackCamera.enabled = false;
                var listener = fallbackCamera.GetComponent<AudioListener>(); if (listener != null) listener.enabled = false;
            }
            foreach (var rider in rig.GetComponentsInChildren<DragonRideController>(true)) { rider.Unmount(); rider.enabled = false; }
            foreach (var locomotion in rig.GetComponentsInChildren<LocomotionProvider>(true)) locomotion.enabled = false;
            var character = rig.GetComponent<CharacterController>();
            bool controllerEnabled = character != null && character.enabled;
            if (controllerEnabled) character.enabled = false;
            // Move the tracked eye, not just its origin, to the sphere centre.
            rig.position += viewCenter.position - viewCamera.transform.position;
            if (controllerEnabled) character.enabled = true;
            if (settings.alignToHeadOnEntry) panorama.transform.rotation = Quaternion.Euler(0f, viewCamera.transform.eulerAngles.y, 0f);
            if (introductionPhoto != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
                introductionPhoto.transform.SetPositionAndRotation(viewCamera.transform.position + forward * settings.photoDistance, Quaternion.LookRotation(forward));
                if (settings.introductionPhoto != null)
                    introductionPhoto.transform.localScale = new Vector3(settings.photoHeight * settings.introductionPhoto.width / settings.introductionPhoto.height, settings.photoHeight, 1f);
            }
            cameraCentered = true;
            fadeQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fadeQuad.name = "Ending Blackout"; fadeQuad.layer = presentationLayer;
            Destroy(fadeQuad.GetComponent<Collider>());
            fadeQuad.transform.SetParent(viewCamera.transform, false);
            float distance = Mathf.Max(0.2f, viewCamera.nearClipPlane + 0.05f);
            fadeQuad.transform.localPosition = Vector3.forward * distance;
            fadeQuad.transform.localScale = Vector3.one * distance * 12f;
            fadeQuad.GetComponent<Renderer>().sharedMaterial = fadeMaterial;
        }

        [BeforeRenderOrder(1000)]
        private void FollowHead()
        {
            if (cameraCentered && viewCamera != null) panorama.transform.position = viewCamera.transform.position;
        }

        private void OnFinished(VideoPlayer source)
        {
            source.Pause(); IsFinished = true;
            Debug.Log("[Ending] Full video completed; waiting for all viewers before Menu.", this);
        }

        private bool UpdatePhoto(double time)
        {
            if (photoMaterial == null || introductionPhoto == null) return false;
            float fadeIn = Mathf.Max(0f, settings.photoDissolveInSeconds);
            float hold = Mathf.Max(0f, settings.photoHoldSeconds);
            float fadeOut = Mathf.Max(0f, settings.photoDissolveOutSeconds);
            double total = fadeIn + hold + fadeOut;
            if (time >= total) { introductionPhoto.enabled = false; return false; }
            introductionPhoto.enabled = time >= 0d;
            float progress;
            if (time < 0d) progress = 0f;
            else if (time < fadeIn) progress = fadeIn > 0f ? Mathf.SmoothStep(0f, 1f, (float)time / fadeIn) : 1f;
            else if (time < fadeIn + hold) progress = 1f;
            else progress = fadeOut > 0f ? Mathf.SmoothStep(1f, 0f, (float)(time - fadeIn - hold) / fadeOut) : 0f;
            photoMaterial.SetFloat("_Progress", progress);
            return true;
        }

        private void OnError(VideoPlayer source, string message)
        {
            // A decode failure must never count as having watched the full video.
            Debug.LogError("[Ending] Video playback failed: " + message, this);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Application.onBeforeRender -= FollowHead;
            if (videoPlayer != null)
            {
                videoPlayer.prepareCompleted -= OnPrepared; videoPlayer.frameReady -= OnFirstFrame;
                videoPlayer.loopPointReached -= OnFinished; videoPlayer.errorReceived -= OnError;
                videoPlayer.Stop(); videoPlayer.targetTexture = null;
            }
            if (viewCamera != null)
            {
                viewCamera.cullingMask = oldMask; viewCamera.clearFlags = oldClear; viewCamera.backgroundColor = oldBackground;
                var additional = viewCamera.GetComponent<UniversalAdditionalCameraData>();
                if (additional != null)
                {
                    additional.renderPostProcessing = oldPost;
                    var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    int index = -1;
                    if (pipeline != null && oldRenderer != null)
                        for (int i = 0; i < pipeline.rendererDataList.Length; i++)
                            if (pipeline.GetRenderer(i) == oldRenderer) { index = i; break; }
                    additional.SetRenderer(index);
                }
            }
            if (fadeQuad != null) Destroy(fadeQuad);
            if (texture != null) { texture.Release(); Destroy(texture); }
            if (sphereMaterial != null) Destroy(sphereMaterial);
            if (fadeMaterial != null) Destroy(fadeMaterial);
            if (photoMaterial != null) Destroy(photoMaterial);
        }
    }
}
