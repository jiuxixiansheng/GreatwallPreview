using System;
using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Sza.Telescope
{
    /// <summary>Each machine owns its own XR overlay and decoder. Never changes the scene Timeline.</summary>
    public sealed class TelescopeVideoView : MonoBehaviour
    {
        private GameObject overlay, videoPanel;
        private VideoPlayer player;
        private AudioSource audioSource;
        private Image black;
        private Material material, uiMaterial;
        private RenderTexture texture;
        private Coroutine routine;
        private Camera cameraOwner;
        private int originalCullingMask;
        private bool frameReady, ended, failed;
        public bool IsViewing { get; private set; }
        public double PlaybackTime => player != null ? player.time : 0d;

        public void Play(Camera camera, VideoClip clip, Shader shader, double start,
            bool synchronized, float fade, float distance, float radius, float volume)
        {
            StopViewing();
            routine = StartCoroutine(Watch(camera, clip, shader, start, synchronized,
                fade, distance, radius, volume));
        }

        private static double Clock(bool network) => network ? NetworkTime.time : Time.unscaledTimeAsDouble;

        private IEnumerator Watch(Camera camera, VideoClip clip, Shader shader, double start,
            bool network, float fade, float distance, float radius, float volume)
        {
            if (Clock(network) >= start + clip.length) yield break;
            IsViewing = true;
            try
            {
                BuildOverlay(camera, clip, shader, distance, radius, volume);
                player.Prepare();
                yield return Fade(0f, 1f, fade);
                double timeout = Time.realtimeSinceStartupAsDouble + 12d;
                while (!failed && !player.isPrepared && Time.realtimeSinceStartupAsDouble < timeout)
                    yield return null;
                if (!player.isPrepared) failed = true;
                while (!failed && Clock(network) < start) yield return null;
                if (!failed && Clock(network) < start + clip.length)
                {
                    player.time = Math.Max(0d, Clock(network) - start);
                    player.Play();
                    timeout = Time.realtimeSinceStartupAsDouble + 5d;
                    while (!failed && !frameReady && Time.realtimeSinceStartupAsDouble < timeout)
                        yield return null;
                    if (!frameReady) failed = true;
                    if (!failed)
                    {
                        videoPanel.SetActive(true);
                        yield return Fade(1f, 0f, fade);
                        double nextCorrection = Clock(network) + 0.5d;
                        while (!failed && !ended && Clock(network) < start + clip.length)
                        {
                            double now = Clock(network);
                            if (now >= nextCorrection)
                            {
                                nextCorrection = now + 0.5d;
                                if (Math.Abs(player.time - (now - start)) > 0.35d)
                                    player.time = Math.Max(0d, now - start);
                            }
                            yield return null;
                        }
                        yield return Fade(0f, 1f, fade);
                    }
                }
                player.Stop();
                videoPanel.SetActive(false);
                yield return Fade(1f, 0f, fade);
            }
            finally
            {
                DestroyOverlay();
                IsViewing = false;
                routine = null;
            }
        }

        private void BuildOverlay(Camera camera, VideoClip clip, Shader shader,
            float distance, float radius, float volume)
        {
            cameraOwner = camera;
            originalCullingMask = camera.cullingMask;
            camera.cullingMask |= 1 << 5;
            overlay = new GameObject("Telescope View (Local)", typeof(RectTransform), typeof(Canvas));
            overlay.layer = 5;
            overlay.transform.SetParent(camera.transform, false);
            float planeDistance = Mathf.Max(distance, camera.nearClipPlane + 0.1f);
            overlay.transform.localPosition = new Vector3(0f, 0f, planeDistance);
            var canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.sortingOrder = 32760;
            ((RectTransform)overlay.transform).sizeDelta = new Vector2(4f, 4f);
            var group = overlay.AddComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;
            uiMaterial = new Material(shader) { renderQueue = 5000 };
            uiMaterial.SetFloat("_MaskEnabled", 0f);

            videoPanel = new GameObject("Video and Lens", typeof(RectTransform));
            videoPanel.layer = 5;
            videoPanel.transform.SetParent(overlay.transform, false);
            Stretch((RectTransform)videoPanel.transform);
            var background = NewImage("Background", videoPanel.transform);
            background.color = Color.black;
            float aspect = clip.height > 0 ? (float)clip.width / clip.height : 1f;
            // Both circular eyepieces must fit; keep the source video aspect separately.
            float maskAspect = Mathf.Max(1.6f, aspect);
            var imageGO = new GameObject("Lens Video", typeof(RectTransform), typeof(RawImage));
            imageGO.layer = 5;
            imageGO.transform.SetParent(videoPanel.transform, false);
            float heightAtPlane = 2f * planeDistance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float widthAtPlane = heightAtPlane * camera.aspect;
            if (camera.stereoEnabled)
            {
                Matrix4x4 projection = camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
                heightAtPlane = 2f * planeDistance / Mathf.Abs(projection.m11);
                widthAtPlane = 2f * planeDistance / Mathf.Abs(projection.m00);
            }
            float videoHeight = Mathf.Min(heightAtPlane * 0.9f, widthAtPlane * 0.9f / maskAspect);
            ((RectTransform)imageGO.transform).sizeDelta = new Vector2(videoHeight * maskAspect, videoHeight);
            int width = Mathf.Max(16, Mathf.Min(1920, (int)clip.width));
            int height = Mathf.Max(16, Mathf.RoundToInt(width / aspect));
            texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            texture.Create();
            material = new Material(shader) { renderQueue = 5000 };
            material.SetFloat("_Aspect", maskAspect);
            material.SetFloat("_VideoAspect", aspect);
            material.SetFloat("_Radius", radius);
            var image = imageGO.GetComponent<RawImage>();
            image.texture = texture;
            image.material = material;
            image.maskable = false;
            image.raycastTarget = false;
            black = NewImage("Fade", overlay.transform);
            black.color = new Color(0f, 0f, 0f, 0f);
            videoPanel.SetActive(false);

            audioSource = overlay.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = volume;
            player = overlay.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = false;
            player.skipOnDrop = true;
            player.waitForFirstFrame = true;
            player.sendFrameReadyEvents = true;
            player.source = VideoSource.VideoClip;
            player.clip = clip;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = texture;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            if (clip.audioTrackCount > 0)
            {
                player.controlledAudioTrackCount = 1;
                player.EnableAudioTrack(0, true);
                player.SetTargetAudioSource(0, audioSource);
            }
            frameReady = ended = failed = false;
            player.frameReady += OnFrame;
            player.loopPointReached += OnEnded;
            player.errorReceived += OnError;
        }

        private Image NewImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            var image = go.GetComponent<Image>();
            image.material = uiMaterial;
            image.maskable = false;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                if (black == null) yield break;
                float a = Mathf.SmoothStep(from, to, t / Mathf.Max(0.01f, seconds));
                black.color = new Color(0f, 0f, 0f, a);
                if (audioSource != null && videoPanel.activeSelf)
                    audioSource.volume = Mathf.Lerp(1f, 0f, a) * GetComponent<TelescopeVideoManager>().videoVolume;
                yield return null;
            }
            if (black != null) black.color = new Color(0f, 0f, 0f, to);
        }

        private void OnFrame(VideoPlayer source, long frame) { frameReady = true; }
        private void OnEnded(VideoPlayer source) { ended = true; }
        private void OnError(VideoPlayer source, string message)
        {
            failed = true;
            Debug.LogWarning("[望远镜] 视频播放失败，将恢复场景画面：" + message, this);
        }

        public void StopViewing()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            DestroyOverlay();
            IsViewing = false;
        }

        private void DestroyOverlay()
        {
            if (player != null)
            {
                player.frameReady -= OnFrame;
                player.loopPointReached -= OnEnded;
                player.errorReceived -= OnError;
                player.Stop();
                player.targetTexture = null;
            }
            if (overlay != null) Destroy(overlay);
            if (texture != null) { texture.Release(); Destroy(texture); }
            if (material != null) Destroy(material);
            if (uiMaterial != null) Destroy(uiMaterial);
            if (cameraOwner != null) cameraOwner.cullingMask = originalCullingMask;
            overlay = videoPanel = null;
            texture = null;
            material = uiMaterial = null;
            cameraOwner = null;
            player = null;
            black = null;
            audioSource = null;
        }

        private void OnDisable() { StopViewing(); }
    }
}
