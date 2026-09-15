using System;
using UnityEngine;
using UnityEngine.Playables;

namespace Greatwall.VRAnimation.Timeline
{
    [Serializable]
    public sealed class BodyActionBehaviour : PlayableBehaviour
    {
        public BodyActionCommand command;
        public string statePath;
        public float stateCrossFadeSeconds = 0.2f;
        public float layerFadeSeconds = 0.25f;

        private bool triggered;

        public override void OnGraphStart(Playable playable)
        {
            triggered = false;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (triggered || info.effectiveWeight <= 0f) return;

            CharacterTimelineAnimator controller = playerData as CharacterTimelineAnimator;
            if (controller == null)
            {
                Debug.LogWarning("Body Action Track 没有绑定 CharacterTimelineAnimator。请把角色拖到轨道绑定框中。");
                triggered = true;
                return;
            }

            if (command == BodyActionCommand.ReleaseToBaseLayer)
                controller.ReleaseBodyAction(layerFadeSeconds);
            else
                controller.PlayBodyState(
                    statePath,
                    stateCrossFadeSeconds,
                    layerFadeSeconds,
                    Mathf.Max(0f, (float)playable.GetTime()));

            triggered = true;
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            double time = playable.GetTime();
            if (time <= 0d || time >= playable.GetDuration())
                triggered = false;
        }
    }
}
