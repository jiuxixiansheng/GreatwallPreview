using System;
using UnityEngine;
using UnityEngine.Playables;

namespace Greatwall.VRAnimation.Timeline
{
    public sealed class RightHandActionMixerBehaviour : PlayableBehaviour
    {
        public PlayableDirector director;
        public double[] clipStartTimes;
        private CharacterTimelineAnimator controller;
        private RightHandActionBehaviour active;
        private int activeLayer = -1;
        private double previousLocalTime;
        private float lastLayerFadeSeconds = 0.35f;
        private bool warned;

        public override void OnGraphStart(Playable playable)
        {
            active = null;
            warned = false;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var binding = playerData as CharacterTimelineAnimator;
            if (binding != controller) { Release(); controller = binding; }
            if (controller == null)
            {
                if (!warned) Debug.LogWarning("Right Hand Action Track 没有绑定 CharacterTimelineAnimator。");
                warned = true;
                return;
            }
            var animator = controller.BoundAnimator;
            if (animator == null || !animator.gameObject.activeInHierarchy) { active = null; return; }

            RightHandActionBehaviour next = null;
            double localTime = 0d, latestStart = double.NegativeInfinity;
            for (int i = 0; i < playable.GetInputCount(); i++)
            {
                if (playable.GetInputWeight(i) <= 0f) continue;
                double start = clipStartTimes != null && i < clipStartTimes.Length ? clipStartTimes[i] : i;
                if (start < latestStart) continue;
                var input = (ScriptPlayable<RightHandActionBehaviour>)playable.GetInput(i);
                next = input.GetBehaviour();
                localTime = input.GetTime();
                latestStart = start;
            }
            if (next == null)
            {
                // 轨道进入空白时逐帧降低权重，让原来的手部姿势自然显露。
                if (activeLayer >= 0 && activeLayer < animator.layerCount)
                    FadeLayerWeight(animator, activeLayer, 0f, lastLayerFadeSeconds);
                active = null;
                return;
            }

            int layer = controller.RightHandLayerIndex;
            int hash = Animator.StringToHash(next.statePath ?? "");
            if (layer < 0 || layer >= animator.layerCount || string.IsNullOrWhiteSpace(next.statePath) ||
                !animator.HasState(layer, hash))
            {
                if (!warned) Debug.LogWarning("Right Hand Action Track 找不到右手状态：" + next.statePath, controller);
                warned = true;
                Release();
                return;
            }

            float seconds = Mathf.Max(0f, (float)localTime);
            if (next.referenceAnimationLength > 0f)
                seconds = next.loop ? Mathf.Repeat(seconds, next.referenceAnimationLength)
                    : Mathf.Min(seconds, Mathf.Max(0f, next.referenceAnimationLength - 0.00001f));
            bool changed = !ReferenceEquals(active, next) || info.seekOccurred ||
                localTime < previousLocalTime - 0.05d ||
                Math.Abs(localTime - previousLocalTime - info.deltaTime) > 0.5d;
            lastLayerFadeSeconds = next.layerFadeSeconds;
            if (Application.isPlaying)
                FadeLayerWeight(animator, layer, 1f, lastLayerFadeSeconds);
            else
                animator.SetLayerWeight(layer, 1f);
            if (!Application.isPlaying)
            {
                animator.PlayInFixedTime(hash, layer, seconds);
                animator.Update(0f);
            }
            else if (changed)
            {
                animator.CrossFadeInFixedTime(hash, Mathf.Max(0f, next.crossFadeSeconds), layer, seconds);
            }
            active = next;
            activeLayer = layer;
            previousLocalTime = localTime;
        }

        public override void OnGraphStop(Playable playable)
        {
            // Timeline 在章节中途暂停时保留右手姿势。
            if (director != null && director.time > 0d && director.time < director.duration - 0.0001d) return;
            Release();
        }

        public override void OnPlayableDestroy(Playable playable) => Release();

        private static void FadeLayerWeight(Animator animator, int layer, float target, float seconds)
        {
            float current = animator.GetLayerWeight(layer);
            float step = seconds <= 0f ? 1f : Time.deltaTime / seconds;
            animator.SetLayerWeight(layer, Mathf.MoveTowards(current, target, step));
        }

        private void Release()
        {
            var animator = controller != null ? controller.BoundAnimator : null;
            if (animator != null && animator.gameObject.activeInHierarchy &&
                activeLayer >= 0 && activeLayer < animator.layerCount)
            {
                animator.SetLayerWeight(activeLayer, 0f);
                if (!Application.isPlaying) animator.Update(0f);
            }
            active = null;
        }
    }
}
