using System;
using UnityEngine;

namespace Xcy.Audio
{
    public enum InteractionAudioCue : byte
    {
        WallGlow, LightCurtain, BrickPrompt, BrickPlaced, BrickCompleted,
        CrackPrompt, CrackRepair, CrackCompleted, BoardingPrompt, Transfer, TakeOff,
        CannonPickup, CannonLoad, CannonPull, CannonFire, CannonFirework
    }

    [CreateAssetMenu(menuName = "Xcy/Interaction Audio Library")]
    public sealed class InteractionAudioLibrary : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public InteractionAudioCue cue;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume;
            public bool loop;
            [Tooltip("大于0时，每次播完后等待此秒数再播放，优先于连续循环；设为0则使用loop设置。")]
            [Min(0f)] public float repeatInterval;
            [Range(0f, 1f)] public float spatialBlend;
        }

        public Entry[] entries = Array.Empty<Entry>();

        public bool TryGet(InteractionAudioCue cue, out Entry entry)
        {
            foreach (var candidate in entries)
                if (candidate.cue == cue && candidate.clip != null)
                { entry = candidate; return true; }
            entry = default;
            return false;
        }

        public uint ConfigurationHash()
        {
            uint hash = 2166136261;
            foreach (var entry in entries)
            {
                foreach (char c in entry.cue + ":" + (entry.clip != null ? entry.clip.name : "missing"))
                    hash = (hash ^ c) * 16777619;
            }
            return hash;
        }
    }
}
