using System;

namespace BoscaliSummer.Features.Support.Domain.Layout
{
    /// <summary>
    /// A cancellation-safe tween. Retargeting continues from the current value; reduced motion snaps.
    /// Nothing in the product waits for a tween to finish.
    /// </summary>
    internal struct TweenState
    {
        public float From;
        public float To;
        public float Value;
        public float Elapsed;
        public float Duration;
        public bool Reduce;

        public void Retarget(float to, float duration, bool reduce)
        {
            From = Value;
            To = to;
            Elapsed = 0f;
            Duration = duration;
            Reduce = reduce;
            if (reduce || !(duration > 0f)) Value = to;
        }

        public void Tick(float dt)
        {
            if (Reduce || !(Duration > 0f))
            {
                Value = To;
                return;
            }
            if (dt > 0f) Elapsed += dt;
            float eased = Motion.EaseOutCubic(Motion.Progress(Elapsed, Duration, false));
            Value = From + (To - From) * eased;
        }
    }

    /// <summary>Shared easing. Rooms pick their own durations; these four constants belong to the root.</summary>
    internal static class Motion
    {
        public const float BackdropIn = 0.12f;
        public const float WindowIn = 0.16f;
        public const float WindowOut = 0.09f;
        public const float RoomSwitch = 0.12f;

        public static float EaseOutCubic(float t)
        {
            if (float.IsNaN(t) || t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            float remaining = 1f - t;
            return 1f - remaining * remaining * remaining;
        }

        /// <summary>Linear 0..1. Reduced motion and a non-positive duration are already finished.</summary>
        public static float Progress(float elapsed, float duration, bool reduce)
        {
            if (reduce || !(duration > 0f) || float.IsNaN(duration)) return 1f;
            if (float.IsNaN(elapsed) || elapsed <= 0f) return 0f;
            if (elapsed >= duration) return 1f;
            return elapsed / duration;
        }
    }
}
