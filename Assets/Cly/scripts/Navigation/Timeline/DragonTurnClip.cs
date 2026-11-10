using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Greatwall.Navigation.Timeline
{
    [Serializable]
    public sealed class DragonTurnClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("正数向右转，负数向左转。例如右转 90 填 90，左转 90 填 -90。")]
        public float turnAngleDegrees = 90f;

        public ClipCaps clipCaps => ClipCaps.None;
        public override double duration => 1d;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<DragonTurnBehaviour> playable =
                ScriptPlayable<DragonTurnBehaviour>.Create(graph);
            playable.GetBehaviour().turnAngleDegrees = turnAngleDegrees;
            return playable;
        }
    }
}
