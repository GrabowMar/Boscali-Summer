using System;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>A host-granted fixed footprint. Opening a client view grants no observation.</summary>
    internal readonly struct SpaceRevealWindow
    {
        public const float ObservationSeconds = 20f;
        /// <summary>RADAR bird busy time: the window plus a margin so Execute lag cannot overlap two windows.</summary>
        public const float BirdBusySeconds = ObservationSeconds + 1f;
        public readonly BirdKind Source;
        public readonly float X, Z, Radius, CreatedAt, ExpiresAt, MinimumSpeed, MaximumSpeed;

        private SpaceRevealWindow(BirdKind source, float x, float z, float radius, float now,
            float expiresAt, float minimumSpeed, float maximumSpeed)
        {
            Source = source; X = x; Z = z; Radius = radius; CreatedAt = now; ExpiresAt = expiresAt;
            MinimumSpeed = minimumSpeed; MaximumSpeed = maximumSpeed;
        }

        public static bool TryCreate(BirdKind source, float x, float z, float radius, float now,
            float seconds, float minimumSpeed, float maximumSpeed, out SpaceRevealWindow window)
        {
            window = default;
            float expiresAt = now + seconds;
            if ((source != BirdKind.Optical && source != BirdKind.Radar) || !Coordinate(x) || !Coordinate(z) ||
                !SpaceRules.Finite(radius) || radius <= 0 || radius > 100000 || !SpaceRules.MissionTime(now) ||
                !SpaceRules.Finite(seconds) || seconds <= 0 || seconds > 300 || !SpaceRules.Finite(expiresAt) ||
                expiresAt <= now || !SpaceRules.Finite(minimumSpeed) || minimumSpeed < 0 ||
                !SpaceRules.Finite(maximumSpeed) || maximumSpeed < minimumSpeed) return false;
            window = new SpaceRevealWindow(source, x, z, radius, now, expiresAt, minimumSpeed, maximumSpeed);
            return true;
        }

        public bool Active(float now) => SpaceRules.MissionTime(now) && Radius > 0 && now >= CreatedAt && now < ExpiresAt;

        public bool Contains(float x, float z, float speed, float now)
        {
            if (!Active(now) || !Coordinate(x) || !Coordinate(z) || !SpaceRules.Finite(speed) ||
                speed < MinimumSpeed || speed > MaximumSpeed) return false;
            double dx = (double)x - X, dz = (double)z - Z;
            return dx * dx + dz * dz <= (double)Radius * Radius;
        }

        /// <summary>Native eligibility only. A real sighting event stamps mission time separately.</summary>
        public static bool NativeFresh(float nativeNow, float nativeSpotted)
        {
            // Multiplayer mission time is Mirage's network stopwatch; it need not run at Unity's scaled rate.
            // Never subtract native age from mission time, or refresh a pure reveal by polling this predicate.
            if (!SpaceRules.MissionTime(nativeNow) ||
                !SpaceRules.MissionTime(nativeSpotted)) return false;
            float age = nativeNow - nativeSpotted;
            return age >= 0 && age < ObservationSeconds;
        }

        private static bool Coordinate(float value) => SpaceRules.Finite(value) && Math.Abs(value) <= 10000000f;
    }
}
