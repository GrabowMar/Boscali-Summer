using System.Globalization;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum GlidePhase { Boost, Glide, Terminal }

    internal readonly struct GlidePlan
    {
        public readonly GlidePhase Phase;
        public readonly float Altitude;
        public readonly float Lateral;

        public GlidePlan(GlidePhase phase, float altitude, float lateral)
        {
            Phase = phase;
            Altitude = altitude;
            Lateral = lateral;
        }
    }

    /// <summary>
    /// HAWC-X "SKIP": boost to the glide ceiling, booster separation at burnout, two skips with a GLOW PHASE around each
    /// apex (and whenever it is fast and high), then a steep FINAL the pilot can shift onto a nearby designation.
    /// During the glow the sheath is lit, enemy detection skips the glider and destination changes are refused.
    /// </summary>
    internal static class GlideProfile
    {
        public const float Ceiling = 24000f;
        public const float SkipAmplitude = 3000f;
        public const float SkipPeriod = 30f;
        public const int Skips = 2;
        public const float WeaveAmplitude = 2500f;
        public const float WeavePeriod = 23f;
        public const float BoostTime = 22f;       // motor burnout = booster separation
        public const float ApexWindow = 6f;       // glow half-width around each skip apex, seconds
        // ...and any time faster than this above PlasmaAltitude. Tune with the sim's VG_HawcX_topSpeed: if the burn tops
        // out well above it the glider glows for the whole glide and the skip windows stop being the readable beat.
        public const float PlasmaSpeed = 2000f;
        public const float PlasmaOffSpeed = 1900f; // hysteresis: a glow already lit holds down to here (and 500 m lower)
        public const float PlasmaAltitude = 20000f;
        public const float NudgeReach = 20000f;   // a designation this close to the current aim point retargets the final

        // Dive starts once the target is within ~1.2x the current altitude (a ~40 deg terminal dive).
        public static float TerminalRange(float altitude) => Mathf.Max(15000f, altitude * 1.2f);

        /// <summary><paramref name="terminal"/>: the dive already started. Terminal latches: a shift (up to NudgeReach) or a
        /// line-of-sight dive sitting on the 1.2x boundary must never send it back up to the ceiling.</summary>
        public static GlidePlan Plan(float time, float groundRange, float altitude, float seed = 0f, bool terminal = false)
        {
            if (terminal || groundRange < TerminalRange(altitude)) return new GlidePlan(GlidePhase.Terminal, 0f, 0f);
            if (time < BoostTime) return new GlidePlan(GlidePhase.Boost, Ceiling, 0f);
            float t = time - BoostTime;
            // Skips run on a fixed clock so every peer agrees when they happen; after the last one it glides flat.
            float skip = t < Skips * SkipPeriod ? SkipAmplitude * Mathf.Sin(2f * Mathf.PI * t / SkipPeriod) : 0f;
            float w = t + seed * 17.3f; // per-missile weave phase
            // Weave fades out over the last 30 km so the dive starts on bearing.
            float fade = Mathf.Clamp01((groundRange - TerminalRange(altitude)) / 30000f);
            return new GlidePlan(GlidePhase.Glide, Ceiling + skip,
                WeaveAmplitude * fade * Mathf.Sin(2f * Mathf.PI * w / WeavePeriod));
        }

        /// <summary>Missile age at the apex of skip 1..Skips (the sine peak a quarter period into each cycle).</summary>
        public static float ApexAge(int skip) => BoostTime + (skip - 0.75f) * SkipPeriod;

        /// <summary>The skip (1..Skips) whose apex window holds this age, else 0.</summary>
        public static int SkipAt(float age)
        {
            for (int k = 1; k <= Skips; k++)
                if (Mathf.Abs(age - ApexAge(k)) <= ApexWindow) return k;
            return 0;
        }

        /// <summary>GLOW PHASE: a skip apex window while gliding, or fast and high in any phase. <paramref name="glowing"/>
        /// is the previous answer: the plasma clause switches off lower than it switches on, so it cannot flicker.</summary>
        public static bool Glow(GlidePhase phase, float age, float speed, float altitude, bool glowing = false) =>
            (phase == GlidePhase.Glide && SkipAt(age) > 0) ||
            (glowing ? speed > PlasmaOffSpeed && altitude > PlasmaAltitude - 500f : speed > PlasmaSpeed && altitude > PlasmaAltitude);

        /// <summary>Sheath emission 0..1 from airspeed.</summary>
        public static float SheathGlow(float speed) => Mathf.Clamp01((speed - 1500f) / 1200f);

        public static bool CanNudge(float metersFromAim) => metersFromAim <= NudgeReach;

        /// <summary>
        /// Body-flap trim in degrees (+ = trailing edge down): tracks the skip's curvature, so 0 at both ends of the skip
        /// window, +10 at the trough pull-up (age 44.5), -10 over the apex; then bites 18 for the dive. Presentation slews it.
        /// </summary>
        public static float FlapAngle(GlidePhase phase, float age)
        {
            if (phase == GlidePhase.Terminal) return 18f;
            float t = age - BoostTime;
            if (phase == GlidePhase.Boost || t >= Skips * SkipPeriod) return 0f;
            return -10f * Mathf.Sin(2f * Mathf.PI * t / SkipPeriod);
        }

        public static string ShiftText(float meters) =>
            "SHIFTED +" + (meters / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km";
    }
}
