using System;

namespace BoscaliSummer.Modules.Immersion.Domain
{
    internal static class PilotPoseMath
    {
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static float Input(float value) => Finite(value) ? Math.Max(-1f, Math.Min(1f, value)) : 0f;

        internal static float Smoothing(float dt, float rate) =>
            Finite(dt) && Finite(rate) && dt > 0f && dt <= .25f && rate > 0f ? (float)(1.0 - Math.Exp(-Math.Min(dt, .05f) * rate)) : 0f;

        internal static float BreathMetres(float phase, bool comfort) => !comfort && Finite(phase) ? (float)Math.Sin(phase) * .003f : 0f;
        internal static float TorsoDegrees(float lateralG, bool comfort) =>
            Finite(lateralG) ? Math.Max(-2f, Math.Min(2f, -lateralG * .7f)) * (comfort ? .25f : 1f) : 0f;

        internal static bool CaptureDue(double now, double previous) =>
            !double.IsNaN(now) && !double.IsInfinity(now) && now >= previous && now - previous >= .1;
    }
}
