using System.Collections.Generic;
using Greatwall.VRAnimation.Timeline;
using UnityEngine;
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Xcy.CrackRepair
{
    [DisallowMultipleComponent]
    public sealed class Part2CrackRepairCoordinator : MonoBehaviour
    {
        [SerializeField] private StoryTimelineManager storyManager;
        [SerializeField] private TimelineCompletionRelay part2TimelineRelay;
        [SerializeField] private CrackRepairDecal[] cracks = new CrackRepairDecal[0];

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        [Header("编辑器临时测试：数字键 1～4 修复对应裂缝")]
        [SerializeField] private bool enableKeyboardRepair = true;
#endif

        private readonly HashSet<CrackRepairDecal> completed = new HashSet<CrackRepairDecal>();
        private bool repairUnlocked;
        private bool repairCompleted;
        private bool advancing;

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        private void Update()
        {
            if (!enableKeyboardRepair || !repairUnlocked || repairCompleted) return;
            // 避免在 Inspector 输入数字时误触发，测试时先点击 Game 窗口。
            UnityEditor.EditorWindow focusedWindow = UnityEditor.EditorWindow.focusedWindow;
            if (focusedWindow == null || focusedWindow.GetType().Name != "GameView") return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) RepairFromKeyboard(0);
            if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) RepairFromKeyboard(1);
            if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) RepairFromKeyboard(2);
            if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) RepairFromKeyboard(3);
        }

        private void RepairFromKeyboard(int index)
        {
            if (index < cracks.Length && cracks[index] != null)
                cracks[index].StartRepair();
        }
#endif

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            completed.Clear();
            repairUnlocked = false;
            repairCompleted = false;
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
            // 修复后先播完本章剩余内容，再切下一章。
            if (!repairCompleted || advancing) return;
            advancing = true;
            if (storyManager != null) storyManager.SwitchToNextChapter();
            else Debug.LogError("Part2 timeline is complete, but StoryTimelineManager is missing.", this);
        }

        // 由 timeline_02 的暂停 Signal 调用，替代原来的“播完后解锁”。
        public void BeginRepair()
        {
            if (!Application.isPlaying || repairUnlocked || repairCompleted) return;
            repairUnlocked = true;
            foreach (CrackRepairDecal crack in cracks)
                if (crack != null) crack.Unlock();
        }

        public void NotifyRepairCompleted(CrackRepairDecal crack)
        {
            if (!repairUnlocked || repairCompleted || crack == null || System.Array.IndexOf(cracks, crack) < 0)
                return;

            completed.Add(crack);
            if (completed.Count != cracks.Length || cracks.Length != 4) return;

            repairCompleted = true;
            EventDispatcher.Instance.Dispatch("裂缝修复完成");
        }
    }
}
