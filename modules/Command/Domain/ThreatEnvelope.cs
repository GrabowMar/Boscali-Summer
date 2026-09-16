using System;

namespace BoscaliSummer.Features.Command.Domain
{
    /// <summary>
    /// A radar's serialized detection envelope, as the game reads it off the component.
    /// <c>maxRange</c> is a scaling constant, not a hard cutoff: the range a target is
    /// actually seen at depends on its signature.
    /// </summary>
    internal readonly struct RadarEnvelope
    {
        public RadarEnvelope(float maxRange, float maxSignal, float minSignal)
        {
            MaxRange = maxRange;
            MaxSignal = maxSignal;
            MinSignal = minSignal;
        }

        public float MaxRange { get; }
        public float MaxSignal { get; }
        public float MinSignal { get; }
    }

    /// <summary>
    /// Who the radar is looking at: the local aircraft's signature and altitude. Altitude is
    /// above the map datum, which is what the game's horizon gate measures against.
    /// </summary>
    internal readonly struct TargetSignature
    {
        public TargetSignature(float rcs, float altitude)
        {
            Rcs = rcs;
            Altitude = altitude;
        }

        public float Rcs { get; }
        public float Altitude { get; }
    }

    /// <summary>
    /// The game's own detection gates as arithmetic, so a coverage ring can be drawn where a
    /// unit actually starts seeing the player rather than at a nominal number.
    ///
    /// <para><c>Radar.CanSeeRadarReturn</c> asks <c>RadarParams.GetSignalStrength</c> for
    /// <c>min(maxRange / slant * RCS^0.25, maxSignal) - clutter * clutterFactor</c> and detects
    /// while that beats <c>minSignal</c>. <c>DetectorManager.RequestRadarCheck</c> rejects
    /// anything past the combined radio horizon before it ever asks, and no radar scans beyond
    /// twice its nominal range. Those three are what a ring is made of.</para>
    ///
    /// <para>Deliberately not modelled, because both are bounded and one-sided: look-down
    /// clutter only subtracts from the signal and can only shrink the ring, doppler only
    /// stretches it, and the terrain line-of-sight raycast is a per-bearing cut the caller
    /// would have to pay a raycast fan for. A ring drawn here is the radio-horizon and
    /// scan-limit picture, and it is the optimistic one.</para>
    /// </summary>
    internal static class ThreatEnvelope
    {
        /// <summary>Twice the Earth's radius, the constant in the game's horizon gate.</summary>
        public const float EarthDiameterMetres = 12742000f;

        private const float MinimumAltitude = 0f;

        /// <summary>The outer bound every radar search is clamped to: twice its nominal range.</summary>
        public static float ScanRadius(float maxRange) => Math.Max(0f, maxRange) * 2f;

        /// <summary>
        /// The ground distance at which either end of the link drops below the radio horizon:
        /// <c>sqrt(2R*h_sensor) + sqrt(2R*h_target)</c>. Nothing is detected past it at any
        /// signature.
        /// </summary>
        public static float HorizonRadius(float sensorAltitude, float targetAltitude) =>
            (float)(Math.Sqrt(EarthDiameterMetres * Math.Max(MinimumAltitude, sensorAltitude)) +
                    Math.Sqrt(EarthDiameterMetres * Math.Max(MinimumAltitude, targetAltitude)));

        /// <summary>
        /// How far out the radar starts seeing this target, as a radius on the ground.
        ///
        /// <para>The signal test is on slant range and the horizon is on ground range, so the
        /// two are converted through the height difference before they are compared.</para>
        /// </summary>
        public static float RadarRadius(in RadarEnvelope radar, in TargetSignature target, float sensorAltitude)
        {
            if (radar.MaxRange <= 0f || target.Rcs <= 0f) return 0f;

            float outer = Math.Min(ScanRadius(radar.MaxRange), HorizonRadius(sensorAltitude, target.Altitude));
            if (outer <= 0f) return 0f;

            // A threshold at or below zero has no failing distance, so the scan bound decides.
            if (radar.MinSignal <= 0f) return outer;

            // The plateau is the ceiling on the signal, and a ceiling under the threshold
            // means this radar never commits a target at any range.
            if (radar.MaxSignal <= radar.MinSignal) return 0f;

            double slantLimit = (double)radar.MaxRange * Math.Pow(target.Rcs, 0.25f) / radar.MinSignal;
            double height = sensorAltitude - target.Altitude;
            double groundSquared = slantLimit * slantLimit - height * height;

            // The envelope does not reach down to (or up to) this altitude at all.
            if (groundSquared <= 0.0) return 0f;

            return (float)Math.Min(Math.Sqrt(groundSquared), outer);
        }

        /// <summary>
        /// Optical and infrared, which never touch radar cross-section: the game needs the
        /// target inside the detector's own sweep AND inside
        /// <c>visibility * magnification</c>, so the nearer of the two wins. A stealthy
        /// airframe shrinks the radar rings and leaves these exactly where they were.
        /// </summary>
        public static float OpticalRadius(float detectorRange, float targetVisibility, float magnification)
        {
            if (detectorRange <= 0f || targetVisibility <= 0f || magnification <= 0f) return 0f;
            return Math.Min(detectorRange, targetVisibility * magnification);
        }

        /// <summary>
        /// How hot one emitter's envelope is at a given ground distance: full at the emitter,
        /// easing to nothing exactly at the radius. Cubed, because an envelope on this scale is
        /// wider than the theater itself — a ground radar's radio horizon against a high target
        /// reaches past the far map edge — so a gentle curve would tint the whole map and say
        /// nothing. The cube keeps the informative part (proximity to the emitter) loud and the
        /// far field near silent, and it costs no square root.
        /// </summary>
        public static float Heat01(float radius, float distanceMetres)
        {
            if (radius <= 0f || distanceMetres >= radius) return 0f;
            float closeness = 1f - Math.Max(0f, distanceMetres) / radius;
            return closeness * closeness * closeness;
        }
    }
}
