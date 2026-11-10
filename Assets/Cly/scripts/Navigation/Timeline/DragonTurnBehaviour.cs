using System;
using UnityEngine;
using UnityEngine.Playables;

namespace Greatwall.Navigation.Timeline
{
    [Serializable]
    public sealed class DragonTurnBehaviour : PlayableBehaviour
    {
        public float turnAngleDegrees;

        private DragonTurnController activeController;
        private bool started;
        private float entryProgress;

        public override void OnGraphStart(Playable playable)
        {
            started = false;
            activeController = null;
            entryProgress = 0f;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (!Application.isPlaying || info.effectiveWeight <= 0f) return;

            DragonTurnController controller = playerData as DragonTurnController;
            if (controller == null)
            {
                if (!started)
                    Debug.LogWarning("Dragon Turn Track 没有绑定 DragonTurnController。");
                started = true;
                return;
            }

            float duration = Mathf.Max(0.01f, (float)playable.GetDuration());
            float progress = Mathf.Clamp01((float)playable.GetTime() / duration);

            if (!started)
            {
                entryProgress = progress < 0.02f ? 0f : progress;
                float entryEased = SmoothStep(entryProgress);
                controller.BeginTurn(turnAngleDegrees * (1f - entryEased));
                activeController = controller;
                started = true;
            }

            float remainingProgress = Mathf.InverseLerp(entryProgress, 1f, progress);
            activeController.EvaluateTurn(remainingProgress);
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (!started || activeController == null)
            {
                started = false;
                activeController = null;
                return;
            }

            bool reachedEnd = playable.GetTime() >= playable.GetDuration() - 0.001d;
            if (reachedEnd)
                activeController.CompleteTurn();
            else
                activeController.CancelTurn();

            started = false;
            activeController = null;
        }

        private static float SmoothStep(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }
    }
}
