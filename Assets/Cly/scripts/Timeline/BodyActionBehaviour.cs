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
        public float referenceAnimationLength = 0f;
        public bool followAnimatorSpeed;

        private bool triggered;
        private CharacterTimelineAnimator controller;

        public override void OnGraphStart(Playable playable)
        {
            triggered = false;
            controller = null;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (info.effectiveWeight <= 0f) return;

            if (controller == null)
            {
                controller = playerData as CharacterTimelineAnimator;
            }

            if (controller == null)
            {
                if (!triggered)
                {
                    Debug.LogWarning("Body Action Track 没有绑定 CharacterTimelineAnimator。请把角色拖到轨道绑定框中。");
                    triggered = true;
                }

                return;
            }

            if (command == BodyActionCommand.ReleaseToBaseLayer)
            {
                if (!triggered)
                {
                    controller.ReleaseBodyAction(layerFadeSeconds);
                    triggered = true;
                }

                return;
            }

            float localTime = Mathf.Max(0f, (float)playable.GetTime());

            if (!triggered)
            {
                controller.PlayBodyState(
                    statePath,
                    stateCrossFadeSeconds,
                    layerFadeSeconds,
                    GetLoopedStateTime(localTime));

                triggered = true;
            }

            if (!followAnimatorSpeed && referenceAnimationLength > 0f)
            {
                float normalizedTime =
                    Mathf.Repeat(localTime, referenceAnimationLength)
                    / referenceAnimationLength;

                controller.SampleBodyState(statePath, normalizedTime);
            }
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            double time = playable.GetTime();
            double duration = playable.GetDuration();

            if (time <= 0d || time >= duration)
            {
                if (controller != null && command == BodyActionCommand.PlayState)
                {
                    controller.ReleaseBodyAction(layerFadeSeconds);
                }

                triggered = false;
            }
        }

        private float GetLoopedStateTime(float localTime)
        {
            if (referenceAnimationLength <= 0f)
                return localTime;

            return Mathf.Repeat(localTime, referenceAnimationLength);
        }
    }
}