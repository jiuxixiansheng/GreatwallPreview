using System;
using UnityEngine.Playables;

namespace Greatwall.VRAnimation.Timeline
{
    [Serializable]
    public sealed class RightHandActionBehaviour : PlayableBehaviour
    {
        public string statePath;
        public float crossFadeSeconds = 0.1f;
        public float layerFadeSeconds = 0.35f;
        public float referenceAnimationLength;
        public bool loop;
    }
}
