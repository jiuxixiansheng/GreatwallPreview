using System.Collections.Generic;
using Greatwall.VRAnimation.Timeline;
using UnityEngine;

namespace Xcy.CrackRepair
{
    [DisallowMultipleComponent]
    public sealed class Part2CrackRepairCoordinator : MonoBehaviour
    {
        [SerializeField] private StoryTimelineManager storyManager;
        [SerializeField] private TimelineCompletionRelay part2TimelineRelay;
        [SerializeField] private CrackRepairDecal[] cracks = new CrackRepairDecal[0];

        private readonly HashSet<CrackRepairDecal> completed = new HashSet<CrackRepairDecal>();
        private bool timelineCompleted;
        private bool advancing;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            completed.Clear();
            timelineCompleted = false;
            advancing = false;
            foreach (CrackRepairDecal crack in cracks)
            {
                if (crack == null) continue;
                crack.SetCoordinator(this);
                crack.ResetRepair();
            }
            if (part2TimelineRelay != null)
                part2TimelineRelay.onTimelineCompleted.AddListener(OnPart2TimelineCompleted);
        }

        private void OnDisable()
        {
            if (part2TimelineRelay != null)
                part2TimelineRelay.onTimelineCompleted.RemoveListener(OnPart2TimelineCompleted);
        }

        private void OnPart2TimelineCompleted()
        {
            if (timelineCompleted) return;
            timelineCompleted = true;
            foreach (CrackRepairDecal crack in cracks)
                if (crack != null) crack.Unlock();
        }

        public void NotifyRepairCompleted(CrackRepairDecal crack)
        {
            if (!timelineCompleted || advancing || crack == null || System.Array.IndexOf(cracks, crack) < 0)
                return;

            completed.Add(crack);
            if (completed.Count != cracks.Length || cracks.Length != 4) return;

            advancing = true;
            if (storyManager != null) storyManager.SwitchToNextChapter();
            else Debug.LogError("Part2 repair is complete, but StoryTimelineManager is missing.", this);
        }
    }
}
