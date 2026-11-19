using UnityEngine;
using UnityEngine.Video;

namespace Sza.Menu
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VideoPlayer), typeof(AudioSource), typeof(MeshRenderer))]
    public sealed class MenuIntroScreen : MonoBehaviour
    {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        private VideoPlayer player;
        private MeshRenderer screen;
        private RenderTexture videoTexture;

        private void Awake()
        {
            player = GetComponent<VideoPlayer>();
            screen = GetComponent<MeshRenderer>();
        }

        private void OnEnable()
        {
            screen.enabled = false;
            VideoClip clip = player.clip;
            if (clip == null)
            {
                Debug.LogError("[Menu] 先导片尚未绑定。", this);
                return;
            }

            videoTexture = new RenderTexture((int)clip.width, (int)clip.height, 0, RenderTextureFormat.ARGB32)
            {
                name = "Menu Intro Video",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            videoTexture.Create();
            var properties = new MaterialPropertyBlock();
            properties.SetTexture(BaseMap, videoTexture);
            screen.SetPropertyBlock(properties);

            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = videoTexture;
            player.isLooping = true;
            player.skipOnDrop = true;
            player.waitForFirstFrame = true;
            player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            if (clip.audioTrackCount > 0)
            {
                player.audioOutputMode = VideoAudioOutputMode.AudioSource;
                player.controlledAudioTrackCount = 1;
                player.EnableAudioTrack(0, true);
                player.SetTargetAudioSource(0, GetComponent<AudioSource>());
            }
            else player.audioOutputMode = VideoAudioOutputMode.None;

            player.prepareCompleted += PlayPrepared;
            player.frameReady += ShowScreen;
            player.errorReceived += ReportError;
            player.sendFrameReadyEvents = true;
            player.Prepare();
        }

        private void PlayPrepared(VideoPlayer source) => source.Play();

        private void ShowScreen(VideoPlayer source, long frame)
        {
            screen.enabled = true;
            source.sendFrameReadyEvents = false;
        }

        private void ReportError(VideoPlayer source, string message)
        {
            screen.enabled = false;
            Debug.LogError($"[Menu] 先导片播放失败：{message}", this);
        }

        private void OnDisable()
        {
            player.prepareCompleted -= PlayPrepared;
            player.frameReady -= ShowScreen;
            player.errorReceived -= ReportError;
            player.sendFrameReadyEvents = false;
            player.Stop();
            player.targetTexture = null;
            screen.enabled = false;
            screen.SetPropertyBlock(null);
            if (videoTexture == null) return;
            videoTexture.Release();
            Destroy(videoTexture);
            videoTexture = null;
        }
    }
}
