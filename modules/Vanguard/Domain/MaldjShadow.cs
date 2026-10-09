using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Ordered: a radar picks the best a jammer gives it (Shadowed beats BurnThrough beats None).</summary>
    internal enum ShadowState { None, BurnThrough, Shadowed }

    /// <summary>
    /// MALD-J "SHADOW" numbers and geometry. The jammer projects a forward lobe; an enemy radar inside the lobe cannot
    /// see a friendly aircraft that sits within a narrow cone around the jammer's line of sight from that radar,
    /// until the aircraft closes inside burn-through range of the radar.
    /// </summary>
    internal static class MaldjShadow
    {
        public const float LobeRange = 35000f;
        public const float LobeHalfAngle = 55f;
        public const float ShadowHalfAngle = 12f;
        public const float BurnThroughRange = 9000f;
        // Secondary effect: vanilla Unit.Jam noise only on lobe radars this close to the jammer itself (an overflight).
        // Noise blinds a radar outright (Radar.CanSeeRadarReturn returns !IsJammed), so it must stay well inside
        // BurnThroughRange: the jammer sits between the radar and the hidden aircraft, and an equal range would blind the
        // radar to a burning-through aircraft too.
        public const float NoiseRange = 3000f;
        public const float NoiseAmount = 1.5f;
        public const float CruiseSpeed = 200f;
        public const float MinAltitude = 300f;
        public const float MaxAltitude = 9000f;
        public const float Lifetime = 240f;
        public const float WingStart = 0.3f;
        public const float WingEnd = 0.8f;
        public const float WingSweepDegrees = 90f;
        public const float MastStart = 1.0f;
        public const float MastEnd = 1.6f;
        public const float MastDegrees = 60f;
        public const float PulseHz = 1.5f;

        public static float CruiseAltitude(float launchAltitude) => Mathf.Clamp(launchAltitude, MinAltitude, MaxAltitude);

        /// <summary>Proportional throttle around the cruise speed: full at 180 m/s, idle at 220 m/s.</summary>
        public static float Throttle(float speed) => Mathf.Clamp01(0.5f + (CruiseSpeed - speed) / 40f);

        /// <summary>Wing unfold 0..1 by missile age (the FBX holds the stowed pose, see MaldjEmitter).</summary>
        public static float WingDeploy(float age) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(WingStart, WingEnd, age));

        /// <summary>Dorsal mast raise (degrees) by missile age.</summary>
        public static float MastAngle(float age) =>
            MastDegrees * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MastStart, MastEnd, age));

        /// <summary>The lobe is live once the mast is up.</summary>
        public static bool Jamming(float age) => age >= MastEnd;

        /// <summary>Emission multiplier for the Glow strips: dim while deploying, pulsing while jamming.</summary>
        public static float Glow(float age, float time) =>
            Jamming(age) ? 1.3f + 0.9f * Mathf.Sin(time * PulseHz * 2f * Mathf.PI) : 0.3f;

        /// <summary>Radar inside the forward lobe: within range and within the half-angle of the jammer's flat heading.</summary>
        public static bool InLobe(Vector3 jammerForward, Vector3 jammerToRadar)
        {
            if (jammerToRadar.sqrMagnitude > LobeRange * LobeRange) return false;
            var forward = new Vector3(jammerForward.x, 0f, jammerForward.z);
            var toRadar = new Vector3(jammerToRadar.x, 0f, jammerToRadar.z);
            return forward.sqrMagnitude > 1e-6f && toRadar.sqrMagnitude > 1f && Vector3.Angle(forward, toRadar) <= LobeHalfAngle;
        }

        /// <summary>Close-in noise: a lobe radar within <see cref="NoiseRange"/> of the jammer is blinded outright.</summary>
        public static bool Noises(Vector3 jammerForward, Vector3 jammerToRadar) =>
            jammerToRadar.sqrMagnitude <= NoiseRange * NoiseRange && InLobe(jammerForward, jammerToRadar);

        /// <summary>What one jammer does for one friendly aircraft against one radar.</summary>
        public static ShadowState Classify(Vector3 jammerForward, Vector3 jammerToRadar, Vector3 radarToAircraft)
        {
            if (!InLobe(jammerForward, jammerToRadar)) return ShadowState.None;
            if (Vector3.Angle(-jammerToRadar, radarToAircraft) > ShadowHalfAngle) return ShadowState.None;
            return radarToAircraft.sqrMagnitude > BurnThroughRange * BurnThroughRange ? ShadowState.Shadowed : ShadowState.BurnThrough;
        }
    }
}
