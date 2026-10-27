using System;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Braking-distance limits, pursuit throttle, and speed-brake hysteresis.</summary>
    internal static class FormationClosure
    {
        internal readonly struct Controls
        {
            public readonly float Throttle;
            public readonly bool Airbrake;
            public Controls(float throttle, bool airbrake) { Throttle = throttle; Airbrake = airbrake; }
        }

        public static float SafeClosure(float distance, float braking, float responseSeconds)
        {
            double decel = Math.Max(0.1f, braking);
            double lag = decel * Math.Max(0f, responseSeconds);
            return (float)(Math.Sqrt(lag * lag + 2d * decel * Math.Max(0f, distance)) - lag);
        }
    }
}
