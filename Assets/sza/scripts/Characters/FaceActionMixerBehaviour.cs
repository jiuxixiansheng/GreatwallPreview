using System;
using UnityEngine;
using UnityEngine.Playables;

namespace Greatwall.VRAnimation.Timeline
{
    /// <summary>Owns the face layer for the duration of each FaceActionClip.</summary>
    public sealed class FaceActionMixerBehaviour : PlayableBehaviour
    {
        public PlayableDirector director;
        public double[] clipStartTimes;
        private CharacterTimelineAnimator controller;
        private FaceActionBehaviour active;
        private int activeLayer = -1;
        private double previousLocalTime;
        private bool warned;

        public override void OnGraphStart(Playable playable) { active = null; }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var binding = playerData as CharacterTimelineAnimator;
            if (binding != controller) { Release(); controller = binding; }
            if (controller == null)
            {
                if (!warned) Debug.LogWarning("Face Action Track 没有绑定 CharacterTimelineAnimator。");
                warned = true; return;
            }
            var animator = controller.BoundAnimator;
            if (animator == null || !animator.gameObject.activeInHierarchy) { active = null; return; }

            FaceActionBehaviour next = null;
            double localTime = 0d, latestStart = double.NegativeInfinity;
            for (int i = 0; i < playable.GetInputCount(); i++)
            {
                if (playable.GetInputWeight(i) <= 0f) continue;
                double start = clipStartTimes != null && i < clipStartTimes.Length ? clipStartTimes[i] : i;
                if (start < latestStart) continue;
                var input = (ScriptPlayable<FaceActionBehaviour>)playable.GetInput(i);
                next = input.GetBehaviour(); localTime = input.GetTime(); latestStart = start;
            }
            if (next == null) { Release(); return; }

            int layer = controller.FaceLayerIndex;
            int hash = Animator.StringToHash(next.statePath ?? "");
            if (layer < 0 || layer >= animator.layerCount || string.IsNullOrWhiteSpace(next.statePath) || !animator.HasState(layer, hash))
            {
                if (!warned) Debug.LogWarning("Face Action Track 找不到面部状态：" + next.statePath, controller);
                warned = true; Release(); return;
            }
            float seconds = Mathf.Max(0f, (float)localTime);
            if (next.referenceAnimationLength > 0f)
                seconds = next.loop ? Mathf.Repeat(seconds, next.referenceAnimationLength)
                    : Mathf.Min(seconds, Mathf.Max(0f, next.referenceAnimationLength - 0.00001f));
            bool changed = !ReferenceEquals(active, next) || info.seekOccurred || localTime < previousLocalTime - 0.05d ||
                Math.Abs(localTime - previousLocalTime - info.deltaTime) > 0.5d;
            animator.SetLayerWeight(layer, 1f);
            if (!Application.isPlaying)
            {
                animator.PlayInFixedTime(hash, layer, seconds);
                animator.Update(0f);
            }
            else if (changed)
            {
                // Clip In and resumed/seeked time are included; ordinary playback remains at1x.
                animator.CrossFadeInFixedTime(hash, Mathf.Max(0f, next.crossFadeSeconds), layer, seconds);
            }
            active = next; activeLayer = layer; previousLocalTime = localTime;
        }

        public override void OnGraphStop(Playable playable)
        {
            // Pausing within the sequence preserves the expression; destroying the graph releases it.
            if (director != null && director.time > 0d && director.time < director.duration - 0.0001d) return;
            Release();
        }

        public override void OnPlayableDestroy(Playable playable) => Release();

        private void Release()
        {
            if (active == null) return;
            var animator = controller != null ? controller.BoundAnimator : null;
            if (animator != null && animator.gameObject.activeInHierarchy && activeLayer >= 0 && activeLayer < animator.layerCount)
            {
                int neutral = Animator.StringToHash(animator.GetLayerName(activeLayer) + ".Idle");
                if (animator.HasState(activeLayer, neutral)) animator.Play(neutral, activeLayer, 0f);
                else animator.SetLayerWeight(activeLayer, 0f);
                // Evaluate immediately, including editor-preview gaps where Animator does not tick.
                animator.Update(0f);
            }
            active = null;
        }
    }
}
