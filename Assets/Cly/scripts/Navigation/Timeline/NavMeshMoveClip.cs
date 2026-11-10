using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Greatwall.Navigation.Timeline
{
    [Serializable]
    public sealed class NavMeshMoveClip : PlayableAsset, ITimelineClipAsset
    {
        public ExposedReference<GameObject> destination;

        public ClipCaps clipCaps => ClipCaps.None;
        public override double duration => 3d;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<NavMeshMoveBehaviour> playable =
                ScriptPlayable<NavMeshMoveBehaviour>.Create(graph);

            GameObject destinationObject = destination.Resolve(graph.GetResolver());
            playable.GetBehaviour().destination =
                destinationObject != null ? destinationObject.transform : null;
            return playable;
        }
    }
}
