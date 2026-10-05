using System;

namespace BoscaliSummer.Modules.Immersion.Domain
{
    // Presentation tuning, not a medical model. One state drives every pilot cue.
    internal sealed class PilotExposure
    {
        public float Positive { get; private set; }
        public float Negative { get; private set; }
        public float Strain => Math.Max(Positive, Negative * 0.55f);

        public void Step(float g, float dt)
        {
            // A paused/stalled frame is not evidence of sustained load.
            if (dt <= 0f || dt > 0.25f || float.IsNaN(g) || float.IsInfinity(g)) return;
            dt = Math.Min(dt, 0.05f);
            float positive = Clamp01((g - 4f) / 5f);
            float negative = Clamp01((-g - 1f) / 2f);
            Positive = Approach(Positive, positive, dt / (positive > Positive ? 1.5f : 3f));
            Negative = Approach(Negative, negative, dt / (negative > Negative ? 0.8f : 2f));
        }

        public void Reset() { Positive = Negative = 0f; }
        private static float Approach(float value, float target, float step) =>
            target > value ? Math.Min(target, value + step) : Math.Max(target, value - step);
        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
