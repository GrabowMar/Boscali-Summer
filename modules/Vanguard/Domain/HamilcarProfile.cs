using System.Globalization;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Release (rail + wing unfold) -> Glide -> Final (700 m gate) -> Flare (160 m) -> Slide -> Unload -> Husk; Lost = no landing zone or shot down.</summary>
    internal enum HamilcarPhase : byte { Release, Glide, Final, Flare, Slide, Unload, Husk, Lost }

    /// <summary>
    /// Joint fractions 0..1. Fold: 1 = outer wing panels stowed. Visor/Ramp: 0 = flight pose, 1 = open.
    /// Roll: 0..1 of <see cref="HamilcarProfile.RollOff"/>. Spin: 0..1 of the propeller's full speed (<see cref="HamilcarProfile.PropRpm"/>).
    /// </summary>
    internal readonly struct HamilcarPose
    {
        public readonly float Fold, Visor, Ramp, Roll, Spin;

        public HamilcarPose(float fold, float visor, float ramp, float roll, float spin)
        {
            Fold = fold;
            Visor = visor;
            Ramp = ramp;
            Roll = roll;
            Spin = spin;
        }
    }

    /// <summary>
    /// CG-9 HAMILCAR: cargo glider with a small pusher propeller carrying one Hexhound GMG (design/vanguard-remake/hamilcar.md
    /// section 4; model contract hamilcar-model-v6.md section 3). Released from a hold, it flies fifteen metres per metre of
    /// height to a spot 250 m short of the designated target, flares, slides to a stop on its belly, opens the visor and
    /// rolls the vehicle out, then stays as a husk.
    /// Distances are flat metres to the touchdown point; heights are metres above the touchdown ground.
    /// </summary>
    internal static class HamilcarProfile
    {
        public const float GlideRatio = 15f;      // reach = GlideRatio x height above the landing spot (the propeller stretches the glide)
        public const float MaxReach = 45000f;
        public const float StandOff = 250f;       // landing centre is this far short of the target along the approach line
        public const float RingStep = 40f, RingMax = 240f;
        public const int RingPoints = 8;
        public const float BackStep = 150f, BackMax = 1500f; // fallback search back toward the launch
        public const float GlideSpeed = 120f;
        public const float FinalSpeed = 60f;      // at the flare start
        public const float TouchSpeed = 34f;      // at touchdown
        public const float GateRange = 700f, GateHeight = 70f;
        public const float FlareRange = 160f, FlareHeight = 9f;
        public const float TouchSink = 1.5f;      // sink rate at touchdown
        public const float SlideDecel = 12f;
        public const float MaxAccel = 25f;        // velocity-servo clamp
        public const float MaxTurnRate = 12f;     // path heading change, deg/s
        public const float DeployStart = 0.4f, DeployEnd = 1.6f; // wing unfold, seconds after release
        public const float UnloadSeconds = 3.2f;  // stop -> live unit
        public const float RollOff = 6f;          // cradle centre -> live-unit spawn point, straight ahead
        public const float HuskSeconds = 60f;     // empty shell lifetime after unload
        public const float RestHeight = 0.03f;    // model origin above the ground at rest (the origin is the bottom of the keel strip)

        public const float SlideDistance = TouchSpeed * TouchSpeed / (2f * SlideDecel); // 48.2 m
        public const float SlideSeconds = TouchSpeed / SlideDecel;                       // 2.83 s, touchdown -> stop
        public const float Gravity = 9.81f, DropGain = 0.8f, MinSpeedCap = 35f;          // SpeedCap
        public const float LostHeight = 60f;      // no landing zone: lost at or under this height
        public const float TouchSlack = 0.15f;    // Flare -> Slide at RestHeight + this above the ground
        public const float StopSpeed = 0.2f;      // Slide -> Unload at or under this
        public const float ArrestDecel = 12f;     // SinkLimit: the part of MaxAccel that stops a descent
        public const float FloorSink = 7f;        // SinkLimit: sink left on the floor, what the flare takes over with at FlareHeight
        public const float PropRpm = 1500f;       // propeller at full speed (a picture only: the airframe has no thrust)
        public const float PropSpinUp = 1f;       // seconds from the end of the unfold to full speed

        // Unload timeline, seconds after the stop.
        public const float VisorEnd = 1f, RampStart = 0.6f, RampEnd = 1.4f, RollStart = 1.4f;

        // Outer wing panel stow (hamilcar-model-v6.md section 3). The model turns WingOuterR 120 deg about (-1, +1, -1) / sqrt 3
        // and WingOuterL 120 deg about (-1, -1, +1) / sqrt 3, right-handed, in its own frame (+X right, +Y nose, +Z up): a
        // third of a turn about a cube diagonal, which carries span -> aft, leading edge -> down, upper surface -> outboard.
        // In Unity the glider's frame is x right, y up, z forward, so a model axis (ax, ay, az) is the local vector
        // (ax, az, ay): R (-1, +1, -1) -> (-1, -1, +1), L (-1, -1, +1) -> (-1, +1, -1). Each lands on the OTHER panel's
        // model numbers, so never copy the spec's triples in here unswapped. That frame is left-handed, so the same turn
        // is MINUS the model angle about that axis: -120 x Fold for both panels. The other joints follow the same rule
        // (Visor +85 about model +X is -85 about right). HamilcarTests.Stow checks where the three panel axes end up.
        // VanguardBuilder.hamilcar.cs poses the mount with a copy of these three values: change them together.
        public const float OuterStow = -120f;
        public static readonly Vector3 OuterAxisR = new Vector3(-1f, -1f, 1f).normalized, OuterAxisL = new Vector3(-1f, 1f, -1f).normalized;

        private const int Rings = (int)(RingMax / RingStep + 0.5f);
        private const int BackSteps = (int)(BackMax / BackStep + 0.5f);
        public const int CandidateCount = 1 + Rings * RingPoints + BackSteps; // centre, the rings, the back line

        // Flare curve h(u) = FlareHeight x (a u + (1 - a) u^2), u = distance / FlareRange: a is picked so the sink rate at the
        // ground is TouchSink at TouchSpeed; the curve ends where the gate line does (FlareHeight at FlareRange).
        private const float FlareLinear = TouchSink * FlareRange / (TouchSpeed * FlareHeight);

        public static float Reach(float heightAboveSpot) => Mathf.Clamp(GlideRatio * heightAboveSpot, 0f, MaxReach);

        /// <summary>
        /// Landing centre: <see cref="StandOff"/> short of the target on the flat launch -> target line, at the target's height.
        /// A centre farther than <paramref name="reach"/> is clamped to reach on that line and <paramref name="isShort"/> is set.
        /// </summary>
        public static Vector3 Centre(Vector3 launch, Vector3 target, float reach, out bool isShort)
        {
            Vector3 flat = new Vector3(target.x - launch.x, 0f, target.z - launch.z);
            float distance = flat.magnitude;
            Vector3 direction = distance > 1e-3f ? flat / distance : Vector3.forward;
            float along = Mathf.Max(0f, distance - StandOff);
            isShort = along > reach;
            if (isShort) along = reach;
            return new Vector3(launch.x + direction.x * along, target.y, launch.z + direction.z * along);
        }

        /// <summary>
        /// Landing-spot search offsets from the centre as (right, forward) metres: 0 is the centre, then six rings of
        /// <see cref="RingPoints"/> (40..240 m, from dead ahead and clockwise, turned by <paramref name="seed01"/> of a full turn), then the
        /// back line (0, -150 .. -1500). Out of range reads as the centre.
        /// </summary>
        public static Vector2 Candidate(int i, float seed01)
        {
            if (i <= 0 || i >= CandidateCount) return Vector2.zero;
            i--;
            if (i >= Rings * RingPoints) return new Vector2(0f, -BackStep * (i - Rings * RingPoints + 1));
            float radius = RingStep * (1 + i / RingPoints);
            float angle = 2f * Mathf.PI * ((i % RingPoints) / (float)RingPoints + seed01);
            return new Vector2(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius);
        }

        /// <summary>Top speed the glide may ask for: a glider only gains speed by descending (80 % of free fall), floored at 35 m/s.</summary>
        public static float SpeedCap(float releaseSpeed, float dropped) =>
            Mathf.Max(MinSpeedCap, Mathf.Sqrt(releaseSpeed * releaseSpeed + 2f * Gravity * DropGain * Mathf.Max(0f, dropped)));

        /// <summary>
        /// Commanded speed (before <see cref="SpeedCap"/>). <paramref name="distToTouchdown"/> is the flat distance to the touchdown
        /// point; in Slide it is the distance slid since touchdown (the same unsigned distance from the same point).
        /// </summary>
        public static float PhaseSpeed(HamilcarPhase phase, float distToTouchdown)
        {
            switch (phase)
            {
                case HamilcarPhase.Final: return Mathf.Lerp(GlideSpeed, FinalSpeed, Mathf.InverseLerp(GateRange, FlareRange, distToTouchdown));
                case HamilcarPhase.Flare: return Mathf.Lerp(FinalSpeed, TouchSpeed, Mathf.InverseLerp(FlareRange, 0f, distToTouchdown));
                case HamilcarPhase.Slide: return Mathf.Sqrt(Mathf.Max(0f, TouchSpeed * TouchSpeed - 2f * SlideDecel * distToTouchdown));
                case HamilcarPhase.Unload:
                case HamilcarPhase.Husk: return 0f;
                default: return GlideSpeed; // Release, Glide, Lost
            }
        }

        /// <summary>
        /// Commanded height above the touchdown point. Release/Glide/Lost follow the caller's straight line from where the glider is
        /// down to the gate (<paramref name="glideSlopeHeight"/>), never lower than <see cref="GateHeight"/>; Final is the gate line;
        /// Flare is the flare curve to 0; on the ground it is 0.
        /// </summary>
        public static float PathHeight(HamilcarPhase phase, float distToTouchdown, float glideSlopeHeight)
        {
            switch (phase)
            {
                case HamilcarPhase.Final: return Mathf.Lerp(GateHeight, FlareHeight, Mathf.InverseLerp(GateRange, FlareRange, distToTouchdown));
                case HamilcarPhase.Flare: return FlareCurve(Mathf.Clamp01(distToTouchdown / FlareRange));
                case HamilcarPhase.Slide:
                case HamilcarPhase.Unload:
                case HamilcarPhase.Husk: return 0f;
                default: return Mathf.Max(GateHeight, glideSlopeHeight);
            }
        }

        private static float FlareCurve(float u) => FlareHeight * u * (FlareLinear + (1f - FlareLinear) * u);

        /// <summary>
        /// Steepest sink (m/s) the servo may ask for <paramref name="aboveFloor"/> metres over the lowest height of its phase (the gate
        /// height in Glide, the flare height in Final): what a steady <see cref="ArrestDecel"/> brings back to <see cref="FloorSink"/> by the
        /// floor. A glide line steeper than the servo can round out would otherwise carry its sink through the gate into the ground.
        /// </summary>
        public static float SinkLimit(float aboveFloor) => FloorSink + Mathf.Sqrt(2f * ArrestDecel * Mathf.Max(0f, aboveFloor));

        /// <summary>
        /// One phase step per call. <paramref name="age"/> = seconds since spawn; <paramref name="phaseAge"/> = seconds in the current phase
        /// (the unload clock); <paramref name="heightAboveGround"/> = origin height over the ground below. Lost beats everything while airborne.
        /// </summary>
        public static HamilcarPhase Next(HamilcarPhase phase, float age, float distToTouchdown, float heightAboveGround, float speed, float phaseAge, bool hasLandingZone)
        {
            if (phase <= HamilcarPhase.Flare && !hasLandingZone && heightAboveGround <= LostHeight) return HamilcarPhase.Lost;
            switch (phase)
            {
                case HamilcarPhase.Release: return age >= DeployEnd ? HamilcarPhase.Glide : phase;
                case HamilcarPhase.Glide: return distToTouchdown <= GateRange ? HamilcarPhase.Final : phase;
                case HamilcarPhase.Final: return distToTouchdown <= FlareRange ? HamilcarPhase.Flare : phase;
                case HamilcarPhase.Flare: return heightAboveGround <= RestHeight + TouchSlack ? HamilcarPhase.Slide : phase;
                case HamilcarPhase.Slide: return speed <= StopSpeed ? HamilcarPhase.Unload : phase;
                case HamilcarPhase.Unload: return phaseAge >= UnloadSeconds ? HamilcarPhase.Husk : phase;
                default: return phase; // Husk, Lost
            }
        }

        /// <summary>
        /// Joint fractions: the wing follows <paramref name="age"/> (seconds since spawn), the cargo end follows <paramref name="phaseAge"/> inside Unload.
        /// The propeller runs up once the wing is out, turns until touchdown, runs down over the slide and stands still after it.
        /// </summary>
        public static HamilcarPose Pose(HamilcarPhase phase, float age, float phaseAge)
        {
            float fold = 1f - Ease(DeployStart, DeployEnd, age);
            float spin = phase <= HamilcarPhase.Flare ? Ease(DeployEnd, DeployEnd + PropSpinUp, age)
                : phase == HamilcarPhase.Slide ? 1f - Ease(0f, SlideSeconds, phaseAge) : 0f;
            if (phase != HamilcarPhase.Unload && phase != HamilcarPhase.Husk) return new HamilcarPose(fold, 0f, 0f, 0f, spin);
            float t = phase == HamilcarPhase.Husk ? UnloadSeconds : phaseAge;
            return new HamilcarPose(fold, Ease(0f, VisorEnd, t), Ease(RampStart, RampEnd, t), Ease(RollStart, UnloadSeconds, t), 0f);
        }

        // SmoothStep can round a hair above 1 near the end (float), joint fractions must stay in 0..1.
        private static float Ease(float from, float to, float t) => Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t)));

        // ---- HUD words (vg.hamilcar), uppercase

        public const string DownText = "HAMILCAR DOWN", DownDetail = "HEXHOUND OUT"; // Info
        public const string ShortText = "HAMILCAR SHORT";                            // Caution
        public const string LostText = "HAMILCAR LOST";                              // Warning

        public static string Km(float metres) => (metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " KM";

        // One string per 0.1 km step (up to 99.9 km), built on first use: the launcher HUD asks for this at 5 Hz.
        private static readonly string[] Held = new string[1000];

        /// <summary>The held line: distance (to the glider) in metres.</summary>
        public static string HudText(float metres)
        {
            int tenths = Mathf.RoundToInt(metres / 100f);
            return tenths >= 0 && tenths < Held.Length ? Held[tenths] ??= "HAMILCAR " + Km(tenths * 100f) : "HAMILCAR " + Km(metres);
        }

        /// <summary>The held line's detail; empty once it is a husk or lost (the line is gone).</summary>
        public static string HudDetail(HamilcarPhase phase)
        {
            switch (phase)
            {
                case HamilcarPhase.Release:
                case HamilcarPhase.Glide: return "GLIDE";
                case HamilcarPhase.Final: return "FINAL";
                case HamilcarPhase.Flare: return "FLARE";
                case HamilcarPhase.Slide:
                case HamilcarPhase.Unload: return "ROLLING OUT";
                default: return "";
            }
        }

        /// <summary>Caution detail: <paramref name="metres"/> = flat distance from the landing centre to the target.</summary>
        public static string ShortDetail(float metres) => "LZ " + Km(metres) + " FROM TARGET";

        public static string LostDetail(bool shotDown) => shotDown ? "SHOT DOWN" : "NO LANDING ZONE";
    }
}
