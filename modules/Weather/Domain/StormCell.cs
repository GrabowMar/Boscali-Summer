using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// How tall a cell is allowed to grow. A supercell is the one that tops out above the
    /// tropopause and gets an anvil; the smaller two exist so the sky has texture between events.
    /// </summary>
    internal enum StormKind
    {
        Cumulus = 0,
        ToweringCumulus = 1,
        Supercell = 2
    }

    /// <summary>The escalation ladder a cell imposes on anything inside its ring.</summary>
    internal enum StormWarning
    {
        None = 0,
        Advisory = 1,
        Watch = 2,
        Warning = 3
    }

    /// <summary>
    /// One storm, as a place in the world rather than a number on a dial. Everything spatial —
    /// the tower the renderer builds, where rain falls, what the radar paints, which ring the
    /// player is inside — is a pure function of this struct, so every peer agrees without a
    /// single byte of storm data on the wire.
    /// </summary>
    internal readonly struct StormCell
    {
        public readonly int Slot;
        public readonly StormKind Kind;
        public readonly float X;
        public readonly float Z;
        public readonly float Radius;
        public readonly float Intensity;
        public readonly float Age;
        public readonly float Lifetime;
        public readonly float CloudBase;
        public readonly float TopHeight;
        public readonly float VelocityX;
        public readonly float VelocityZ;

        public StormCell(
            int slot,
            StormKind kind,
            float x,
            float z,
            float radius,
            float intensity,
            float age,
            float lifetime,
            float cloudBase,
            float topHeight,
            float velocityX,
            float velocityZ)
        {
            Slot = slot;
            Kind = kind;
            X = x;
            Z = z;
            Radius = radius;
            Intensity = intensity;
            Age = age;
            Lifetime = lifetime;
            CloudBase = cloudBase;
            TopHeight = topHeight;
            VelocityX = velocityX;
            VelocityZ = velocityZ;
        }

        public bool IsSupercell => Kind == StormKind.Supercell;

        public float DistanceTo(float x, float z)
        {
            float dx = X - x;
            float dz = Z - z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// How much this cell owns the sky at a point: a smooth radial falloff to zero at the
        /// cell's own radius, scaled by intensity. This is the single number rain, haze and the
        /// local warning all read, so they can never disagree with each other.
        /// </summary>
        public float InfluenceAt(float x, float z)
        {
            if (Radius <= 0f || Intensity <= 0f) return 0f;
            float falloff = WeatherRegimes.Clamp01(1f - DistanceTo(x, z) / Radius);
            return falloff * falloff * (3f - 2f * falloff) * Intensity;
        }

        /// <summary>
        /// The ring a point sits in. Deliberately wider than the rain: a pilot wants the warning
        /// before the weather, which is the whole point of a forecast.
        /// </summary>
        public StormWarning WarningAt(float x, float z)
        {
            if (Radius <= 0f || Intensity < StormField.MinWarningIntensity) return StormWarning.None;
            float distance = DistanceTo(x, z);
            if (distance <= Radius * 0.6f) return StormWarning.Warning;
            if (distance <= Radius * 1.6f) return StormWarning.Watch;
            if (distance <= Radius * 4f) return StormWarning.Advisory;
            return StormWarning.None;
        }
    }
}
