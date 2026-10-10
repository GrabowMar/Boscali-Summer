using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>
    /// Our own pose for a heading-up plan: global position and heading, so any global point projects to (right, forward) metres. A trail
    /// stores global points and is projected through the CURRENT frame every paint, so it never drifts when the origin shifts or we turn.
    /// </summary>
    internal readonly struct PlanFrame
    {
        public readonly float OwnX, OwnZ, Sin, Cos, HeadingDeg;

        public PlanFrame(float ownX, float ownZ, float headingDeg)
        {
            OwnX = ownX;
            OwnZ = ownZ;
            HeadingDeg = headingDeg;
            double h = headingDeg * Math.PI / 180.0;
            Sin = (float)Math.Sin(h);
            Cos = (float)Math.Cos(h);
        }

        /// <summary>The point as metres to our right and ahead.</summary>
        public void Project(float gx, float gz, out float right, out float forward)
        {
            float dx = gx - OwnX, dz = gz - OwnZ;
            right = dx * Cos - dz * Sin;
            forward = dx * Sin + dz * Cos;
        }

        /// <summary>Compass bearing off the nose (0..360 clockwise) and ground range of a global point.</summary>
        public void Polar(float gx, float gz, out float bearingDeg, out float rangeM)
        {
            Project(gx, gz, out float r, out float f);
            bearingDeg = ThreatMath.Norm360((float)(Math.Atan2(r, f) * 180.0 / Math.PI));
            rangeM = (float)Math.Sqrt(r * r + f * f);
        }
    }

    /// <summary>
    /// The SHOT plan's range scale: 5, 10, 20, 40, 80, 160 or 320 km, the smallest that holds everything with a margin. It grows at once and only
    /// shrinks after the smaller scale has held for a moment, and a change slides over a second (the prototype's 40 to 20 km step). Pure.
    /// </summary>
    internal sealed class RangeScale
    {
        public static readonly float[] Steps = { 5f, 10f, 20f, 40f, 80f, 160f, 320f };
        public const float Margin = 1.1f, ShrinkAfterS = 1.5f, SlideS = 1f;

        private int step = -1, want = -1;
        private float from, at, shrinkSince;

        /// <summary>The scale now, in km.</summary>
        public float Km { get; private set; } = 40f;

        public static int StepFor(float needKm)
        {
            for (int i = 0; i < Steps.Length; i++) if (needKm * Margin <= Steps[i]) return i;
            return Steps.Length - 1;
        }

        public void Update(float needKm, float now)
        {
            int target = StepFor(float.IsNaN(needKm) ? 0f : needKm);
            if (step < 0) { step = want = target; Km = Steps[step]; return; }
            if (target > step) Switch(target, now);
            else if (target < step)
            {
                if (want != target) { want = target; shrinkSince = now; }
                if (now - shrinkSince >= ShrinkAfterS) Switch(target, now);
            }
            else want = step;
            float u = Math.Min(1f, Math.Max(0f, (now - at) / SlideS)), e = u * u * (3f - 2f * u);
            Km = from + (Steps[step] - from) * e;
        }

        private void Switch(int target, float now)
        {
            from = Km;
            at = now;
            step = want = target;
        }

        public void Reset() { step = want = -1; Km = 40f; }
    }

    /// <summary>The SHOT page's rules (the prototype's <c>pages/shot.js</c> maths), pure.</summary>
    internal static class ShotMath
    {
        /// <summary>The last stretch of a homing weapon's flight is TERM (the prototype's 2.4 km mark).</summary>
        public const float TerminalRangeM = 2500f;
        /// <summary>A motor is "boost" for at most this long (a sustained motor burns the whole flight).</summary>
        public const float BoostMaxS = 4f;
        /// <summary>A defeated weapon stays on the plan this long, hollow.</summary>
        public const float GhostS = 1.6f;
        /// <summary>A result stays on the plan this long (it fades over the last 0.8 s).</summary>
        public const float ResultS = 4.2f;
        public const float Gravity = 9.81f;
        /// <summary>A closing speed under this is not closing (no time to go).</summary>
        public const float MinClosureMps = 15f;

        /// <summary>
        /// The phase of a delivery: a bomb FALLs; a missile BOOSTs while its booster is on (the game holds the missile's own motor until it drops) or its motor runs in the first
        /// <see cref="BoostMaxS"/>, flies MID until its own seeker has the target (<see cref="SeekerLocked"/>), is ACTIVE from then and TERM inside <see cref="TerminalRangeM"/>.
        /// </summary>
        public static ShotPhase PhaseOf(bool bomb, bool motorOn, float ageS, bool boosterAttached, bool seekerLocked, float rangeM)
        {
            if (bomb) return ShotPhase.Fall;
            if (boosterAttached || (motorOn && ageS < BoostMaxS)) return ShotPhase.Boost;
            if (!seekerLocked) return ShotPhase.Mid;
            return rangeM <= TerminalRangeM ? ShotPhase.Term : ShotPhase.Active;
        }

        /// <summary>
        /// The weapon's own seeker has its target. The game says so in two ways: a radar seeker (ARH, SARH) reports its mode (locked, or searching: an ARH before its pitbull
        /// and either kind once it lost the return), every other seeker (IR, laser, optical, anti-radiation, inertial) stays passive and simply has a target until it loses it.
        /// </summary>
        public static bool SeekerLocked(bool radarSeeker, bool modeLocked, bool hasTarget) => hasTarget && (!radarSeeker || modeLocked);

        public static string PhaseWord(ShotPhase phase)
        {
            switch (phase)
            {
                case ShotPhase.Boost: return "BOOST";
                case ShotPhase.Mid: return "MID";
                case ShotPhase.Active: return "ACTIVE";
                case ShotPhase.Term: return "TERM";
                default: return "SPLASH";
            }
        }

        /// <summary>Seconds to arrive at the current closing speed; NaN when it is not closing (no cap: a cruise missile has minutes to go).</summary>
        public static float TimeToGo(float rangeM, float closureMps) =>
            closureMps >= MinClosureMps && rangeM >= 0f ? rangeM / closureMps : float.NaN;

        /// <summary>
        /// Seconds until a bomb that is <paramref name="heightM"/> above the ground, climbing at <paramref name="climbMps"/>, lands, with gravity alone (no drag).
        /// ponytail: drag is ignored, so a bomb released high and fast lands a little short of where this says; fold in WeaponInfo.dragCoef if that shows.
        /// </summary>
        public static float FallTime(float heightM, float climbMps)
        {
            if (heightM <= 0f) return 0f;
            return (climbMps + (float)Math.Sqrt(climbMps * climbMps + 2f * Gravity * heightM)) / Gravity;
        }

        private static readonly float[] RingCandidates = { 1f, 2.5f, 5f, 10f, 20f, 40f, 80f, 160f, 320f };

        /// <summary>The ring values (km) to draw at <paramref name="unitsPerKm"/> design units per km: those from an eighth of the scale up that still fit inside <paramref name="maxRadius"/> (the prototype's 5, 10, 20, 40); the 1 and 2.5 km rings only on the smallest scales.</summary>
        public static int RingValues(float scaleKm, float unitsPerKm, float maxRadius, float[] into)
        {
            int n = 0;
            foreach (float v in RingCandidates)
            {
                if (v < scaleKm / 8f - 0.001f || v * unitsPerKm >= maxRadius || n >= into.Length) continue;
                if ((v == 1f && scaleKm > 5f) || (v == 2.5f && scaleKm > 10f)) continue;
                into[n++] = v;
            }
            return n;
        }

        /// <summary>The lead shot: the soonest to arrive among those flying (a known time before an unknown one, then the nearest); -1 when none flies.</summary>
        public static int Lead(ShotMissile[] shots, int count)
        {
            int best = -1;
            for (int i = 0; i < count; i++)
            {
                if (shots[i].Ghost) continue;
                if (best < 0 || Sooner(shots[i], shots[best])) best = i;
            }
            return best;
        }

        private static bool Sooner(in ShotMissile a, in ShotMissile b)
        {
            bool ka = !float.IsNaN(a.TtiS), kb = !float.IsNaN(b.TtiS);
            if (ka != kb) return ka;
            if (ka && a.TtiS != b.TtiS) return a.TtiS < b.TtiS;
            return a.RangeM < b.RangeM;
        }

        /// <summary>A weapon has lost its target: the unit it was given is still alive and is no longer what it guides on (a flare, chaff, a notch, terrain, a decoy). A bomb is never "lost": it falls where it falls.</summary>
        public static bool LostTarget(bool bomb, bool firstTargetAlive, bool stillOnIt) => !bomb && firstTargetAlive && !stillOnIt;

        /// <summary>The share of the journey flown: 0 at the launch, 1 at the aim point.</summary>
        public static float Progress(float flownM, float remainingM)
        {
            float total = flownM + remainingM;
            return total < 1f ? 0f : Math.Min(1f, Math.Max(0f, flownM / total));
        }

        /// <summary>The prototype's TTI ring around a target: wide while the shot is far, shrinking onto it, capped at 34 units.</summary>
        public static float TtiRingRadius(float ttiS) => Math.Min(34f, 7f + 3f * (float.IsNaN(ttiS) ? 10f : Math.Max(0f, ttiS)));
    }
}
