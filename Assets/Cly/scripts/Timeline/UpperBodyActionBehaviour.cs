using System;
using UnityEngine.Playables;

namespace Greatwall.VRAnimation.Timeline
{
    [Serializable]
    public sealed class UpperBodyActionBehaviour : PlayableBehaviour
    {
        public string statePath;
        public float stateCrossFadeSeconds;
        public float layerFadeSeconds;

        // 数据交给轨道混合器统一处理，避免相邻片段各自释放同一个 Animator 层。
    }
}
