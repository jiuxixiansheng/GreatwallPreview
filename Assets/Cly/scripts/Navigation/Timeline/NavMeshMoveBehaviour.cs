using System;
using UnityEngine;
using UnityEngine.Playables;

namespace Greatwall.Navigation.Timeline
{
    [Serializable]
    public sealed class NavMeshMoveBehaviour : PlayableBehaviour
    {
        public Transform destination;

        private DragonNavMeshMover activeMover;
        private bool triggered;

        public override void OnGraphStart(Playable playable)
        {
            triggered = false;
            activeMover = null;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (!Application.isPlaying || triggered || info.effectiveWeight <= 0f) return;

            DragonNavMeshMover mover = playerData as DragonNavMeshMover;
            if (mover == null)
            {
                Debug.LogWarning("NavMesh Move Track 没有绑定 DragonNavMeshMover。");
                triggered = true;
                return;
            }

            if (destination == null)
            {
                Debug.LogWarning("NavMesh Move Clip 没有设置 Destination。", mover);
                triggered = true;
                return;
            }

            float remainingSeconds = Mathf.Max(
                0.01f,
                (float)(playable.GetDuration() - playable.GetTime()));

            activeMover = mover;
            activeMover.MoveToPositionInDuration(destination.position, remainingSeconds);
            triggered = true;
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (Application.isPlaying && triggered && activeMover != null)
                activeMover.StopMoving();

            triggered = false;
            activeMover = null;
        }
    }
}
