using UnityEngine;
using UnityEngine.Playables;

namespace Greatwall.VRAnimation.Timeline
{
    public sealed class UpperBodyActionMixerBehaviour : PlayableBehaviour
    {
        public double[] clipStartTimes;
        private CharacterTimelineAnimator controller;
        private UpperBodyActionBehaviour activeClip;
        private PlayableDirector director;
        private float lastFadeSeconds = 0.25f;
        private bool warnedMissingBinding;

        public override void OnGraphStart(Playable playable)
        {
            warnedMissingBinding = false;
            director = playable.GetGraph().GetResolver() as PlayableDirector;
#if UNITY_EDITOR
            Debug.Log($"[UpperBody诊断] 上半身轨道启动；Director={director?.name}；片段数量={playable.GetInputCount()}");
#endif
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (!Application.isPlaying) return;
            controller = playerData as CharacterTimelineAnimator;
            if (controller == null)
            {
                if (!warnedMissingBinding)
                {
                    Debug.LogWarning("Upper Body Action Track 没有绑定 CharacterTimelineAnimator。请把角色拖到轨道绑定框中。");
                    warnedMissingBinding = true;
                }
                return;
            }

            UpperBodyActionBehaviour nextClip = null;
            float nextClipStartTime = 0f;
            double latestStart = double.NegativeInfinity;
            for (int i = 0; i < playable.GetInputCount(); i++)
            {
                float weight = playable.GetInputWeight(i);
                if (weight <= 0f) continue;
                // 重叠时由后开始的片段接管，不等到权重过半，也不反复切回旧动作。
                // 比较 Timeline 起始位置，不能比较包含 Clip In 的动画内部时间。
                double start = clipStartTimes != null && i < clipStartTimes.Length ? clipStartTimes[i] : i;
                if (start < latestStart) continue;
                var input = (ScriptPlayable<UpperBodyActionBehaviour>)playable.GetInput(i);
                nextClip = input.GetBehaviour();
                // 输入时间已包含 Timeline 的 Clip In，不要再加一次偏移。
                nextClipStartTime = Mathf.Max(0f, (float)input.GetTime());
                latestStart = start;
            }

            if (ReferenceEquals(nextClip, activeClip)) return;
            activeClip = nextClip;
            if (nextClip == null)
            {
#if UNITY_EDITOR
                Debug.Log($"[UpperBody诊断] 进入轨道空白，释放上半身；Director={director?.name}", controller);
#endif
                controller.ReleaseUpperBody(lastFadeSeconds);
                return;
            }

            lastFadeSeconds = nextClip.layerFadeSeconds;
            controller.PlayUpperBodyState(nextClip.statePath,
                nextClip.stateCrossFadeSeconds, nextClip.layerFadeSeconds, nextClipStartTime);
        }

        public override void OnGraphStop(Playable playable)
        {
            // 中途 Pause（以后用于等待任务）保留覆盖，不把暂停误判成章节结束。
            // 自然结束的 Hold 模式也要释放，否则最后一个上半身动作会残留。
            if (director != null && director.time > 0d && director.time < director.duration - 0.0001d)
                return;
            Release();
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            Release();
        }

        private void Release()
        {
            if (Application.isPlaying && controller != null && activeClip != null)
                controller.ReleaseUpperBody(lastFadeSeconds);
            activeClip = null;
        }
    }
}
