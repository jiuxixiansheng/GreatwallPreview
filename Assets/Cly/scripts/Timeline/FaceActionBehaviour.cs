using System;
using UnityEngine;
using UnityEngine.Playables;

namespace Greatwall.VRAnimation.Timeline
{
    [Serializable]
    public sealed class FaceActionBehaviour : PlayableBehaviour
    {
        public string statePath;
        public float crossFadeSeconds = 0.1f;

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
                Debug.LogWarning("Face Action Track 没有绑定 CharacterTimelineAnimator。请把角色拖到轨道绑定框中。");
                triggered = true;
                return;
            }

            controller.PlayFaceState(statePath, crossFadeSeconds);
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
