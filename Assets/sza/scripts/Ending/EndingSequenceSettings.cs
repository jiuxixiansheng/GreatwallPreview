using UnityEngine;
using UnityEngine.Video;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sza.Ending
{
    [CreateAssetMenu(menuName = "Sza/Ending Sequence Settings")]
    public sealed class EndingSequenceSettings : ScriptableObject
    {
        public string gameplayScene = "Assets/Scenes/szaScene_Final.unity";
        public string endingScene = "Assets/Scenes/szaScene_end.unity";
        public string menuScene = "Assets/Scenes/Menu.unity";
        public VideoClip video;
        [Tooltip("片尾专用渲染器，不包含关卡的全屏雾或调色效果。")]
        public ScriptableRendererData presentationRenderer;
        [Min(0f)] public float revealSeconds = 0.6f;
        [Min(0f)] public float returnFadeSeconds = 0.7f;
        [Min(0.1f)] public float networkStartLead = 0.6f;
        [Tooltip("全景图水平旋转，单位为度。")]
        public float panoramaYaw;
        [Tooltip("在每台头显进入片尾时，让视频正前方对齐当前观看方向。")]
        public bool alignToHeadOnEntry = true;

        [Header("片尾前的照片展示")]
        public Texture2D introductionPhoto;
        [Min(0.1f)] public float photoHeight = 5f;
        [Min(0.5f)] public float photoDistance = 5f;
        [Min(0f)] public float photoDissolveInSeconds = 1.5f;
        [Min(0f)] public float photoHoldSeconds = 10f;
        [Min(0f)] public float photoDissolveOutSeconds = 1.5f;

        public int PresentationRendererIndex()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null || presentationRenderer == null) return -1;
            for (int i = 0; i < pipeline.rendererDataList.Length; i++)
                if (pipeline.rendererDataList[i] == presentationRenderer) return i;
            return -1;
        }
    }
}
