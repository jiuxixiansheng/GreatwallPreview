using System;
using UnityEngine.Playables;

namespace Greatwall.VRAnimation.Timeline
{
    [Serializable]
    public sealed class FaceActionBehaviour : PlayableBehaviour
    {
        public string statePath;
        public float crossFadeSeconds = 0.1f;
        public float referenceAnimationLength;
        public bool loop;
        // The track mixer owns playback and release so neighboring clips cannot reset each other.
    }
}
