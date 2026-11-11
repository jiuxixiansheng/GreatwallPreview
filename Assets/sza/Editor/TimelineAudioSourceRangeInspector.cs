using System;
using System.Globalization;
using System.Reflection;
using Greatwall.VRAnimation.Timeline;
using Sza.Characters;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace Sza.Editor
{
    [InitializeOnLoad]
    public static class TimelineAudioSourceRangeInspector
    {
        // Timeline's temporary Inspector target is internal, but its clip property is public.
        // Read the Inspector's own targets so a locked Inspector keeps showing its own clip.
        private static readonly PropertyInfo ClipProperty = typeof(TimelineEditor).Assembly
            .GetType("UnityEditor.Timeline.EditorClip")?.GetProperty("clip", BindingFlags.Public | BindingFlags.Instance);

        static TimelineAudioSourceRangeInspector()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += DrawSourceRange;
        }

        public readonly struct SourceRange
        {
            public readonly double start, end, usedDuration, sourceDuration;
            public readonly bool wraps, truncated;

            internal SourceRange(double start, double end, double used, double total, bool wraps, bool truncated)
            {
                this.start = start; this.end = end; usedDuration = used; sourceDuration = total;
                this.wraps = wraps; this.truncated = truncated;
            }
        }

        public static bool TryGetClip(UnityEngine.Object target, out TimelineClip clip)
        {
            clip = null;
            if (target == null || ClipProperty == null || !ClipProperty.DeclaringType.IsInstanceOfType(target)) return false;
            clip = ClipProperty.GetValue(target) as TimelineClip;
            return clip?.asset is AudioPlayableAsset || clip?.asset is TimelineFaceClip || clip?.asset is FaceActionClip;
        }

        public static SourceRange GetSourceRange(TimelineClip clip)
        {
            if (!TryGetSourceInfo(clip, out double length, out bool loop)) return default;
            double rawStart = Math.Max(0d, clip.clipIn);
            double requested = Math.Max(0d, clip.duration * clip.timeScale);
            double rawEnd = rawStart + requested;
            if (!loop || length <= 0d)
            {
                double start = Math.Min(rawStart, length);
                double end = Math.Min(rawEnd, length);
                return new SourceRange(start, end, Math.Max(0d, end - start), length, false, rawEnd > length + 1e-8d);
            }

            double loopStart = rawStart % length;
            double loopEnd = rawEnd % length;
            double epsilon = Math.Max(1d, length) * 1e-10d;
            // An interval ending exactly at the last sample ends at the file boundary.
            if (requested > 0d && loopEnd < epsilon) loopEnd = length;
            return new SourceRange(loopStart, loopEnd, requested, length, loopStart + requested > length + epsilon, false);
        }

        private static bool TryGetSourceInfo(TimelineClip clip, out double length, out bool loop)
        {
            length = 0d; loop = false;
            if (clip?.asset is AudioPlayableAsset audio)
            {
                if (audio.clip == null) return false;
                length = audio.duration; loop = audio.loop;
                return true;
            }
            AnimationClip animation = null;
            if (clip?.asset is TimelineFaceClip face) animation = face.referenceAnimationClip;
            else if (clip?.asset is FaceActionClip legacyFace) animation = legacyFace.referenceAnimationClip;
            if (animation == null) return false;
            length = animation.length; loop = animation.isLooping;
            return true;
        }

        private static void DrawSourceRange(UnityEditor.Editor editor)
        {
            foreach (var target in editor.targets)
            {
                if (!TryGetClip(target, out var clip)) continue;
                bool isAnimation = !(clip.asset is AudioPlayableAsset);
                bool hasSource = TryGetSourceInfo(clip, out _, out _);
                if (!hasSource && !isAnimation) continue;
                string kind = isAnimation ? "动画" : "音频";
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("原" + kind + "截取范围（秒）", EditorStyles.boldLabel);
                    if (editor.targets.Length > 1) EditorGUILayout.LabelField(clip.displayName, EditorStyles.miniLabel);
                    if (!hasSource)
                    {
                        EditorGUILayout.HelpBox("请先在 Reference Animation Clip 中指定嘴型源动画，以显示截取范围。", MessageType.Info);
                        continue;
                    }
                    var range = GetSourceRange(clip);
                    DrawTime("原" + kind + "起点", range.start);
                    DrawTime("原" + kind + "终点", range.end);
                    DrawTime("使用的原" + kind + "时长", range.usedDuration);
                    DrawTime("原" + kind + "总长", range.sourceDuration);
                    if (range.wraps)
                        EditorGUILayout.HelpBox("这段已跨过" + kind + "末尾并循环，终点显示最后一圈的位置。", MessageType.Info);
                    else if (range.truncated)
                        EditorGUILayout.HelpBox("片段超出原" + kind + "末尾，以上范围显示实际可用的" + kind + "内容。", MessageType.Info);
                    string hint = "下方 Start / End 为时间线位置。";
                    if ((clip.clipCaps & ClipCaps.ClipIn) != 0) hint += "Clip In 为原" + kind + "起点。";
                    EditorGUILayout.LabelField(hint, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private static void DrawTime(string label, double seconds)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                EditorGUILayout.SelectableLabel(seconds.ToString("0.######", CultureInfo.InvariantCulture),
                    EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }
    }
}
