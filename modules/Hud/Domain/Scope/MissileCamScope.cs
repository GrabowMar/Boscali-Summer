using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What the seeker of the ridden missile is doing: DATA-LINK (an active radar still flying on the launcher's track), LOCKED, SEARCH (a semi-active seeker whose illuminator lost the target), BLIND (nothing to guide on, or an active radar that lost its return), LOST (the ride is over).</summary>
    internal enum CamMode : byte { DataLink, Locked, Search, Blind, Lost }

    /// <summary>
    /// The MISSILE CAM page's feed (Hud's own, from <c>MissileCamScopeFeed</c>): the own missile the orbit camera rides. There is no second camera in the game: the screen IS the
    /// feed, so the page draws what the seeker knows around it. Angles are degrees off the missile's boresight (x right, y down), the tape is range to go, the profile strip is the
    /// path since launch. Live while the camera rides one of our missiles, then for a few seconds as a lost feed with the result of the shot.
    /// </summary>
    internal struct MissileCamScope
    {
        /// <summary>This missile's place in the follow list (0-based) and the list's size ("MSL 1/2"), its weapon's short and full names, its seeker word (SARH, ARH, IR...), its target's name and the short name of the missile the view steps to next (null: it is the only one).</summary>
        public int Index, Count;
        public string Weapon, Full, Seeker, Target, Next;
        public CamMode Mode;
        public bool HasTarget, HasLast;
        /// <summary>The target (the last known one while blind) off the boresight, and the seeker's cone half angle (0 = the game gives none: an IR or laser seeker).</summary>
        public float ErrXDeg, ErrYDeg, ConeDeg;
        /// <summary>The missile's pitch and roll in degrees (the horizon the page tilts) and its compass heading.</summary>
        public float PitchDeg, RollDeg, HeadingDeg;
        public float RangeM, ClosureMps, TtiS;
        /// <summary>The range tape: its top (the farthest range to go seen on this flight) and the ACT mark of an active radar: where its seeker went active (<see cref="ActivePassed"/>) or, until then, the range the game turns it on at (NaN: no such mark for this seeker).</summary>
        public float TapeTopM, ActiveAtM;
        public bool ActivePassed;
        public ShotPhase Phase;
        /// <summary>The motor: seconds of burn left and the whole burn (0 = a coasting round or unknown).</summary>
        public float BurnLeftS, BurnTotalS;
        /// <summary>Profile: ground distance from the launch and altitude along the path (provider-owned arrays), the missile's own now, and the target's (<see cref="HasTargetPlace"/>).</summary>
        public int PathCount;
        public float[] PathD, PathA;
        public float NowD, NowA;
        public bool HasTargetPlace;
        public float TargetD, TargetA;
        /// <summary>The ride is over: how long ago, and how the shot ended (<see cref="ShotState.InFlight"/> while the game has not said, <see cref="ShotState.Empty"/> for a round the shot ledger does not count: a decoy).</summary>
        public bool Lost;
        public float LostS;
        public ShotState Result;
    }

    /// <summary>The MISSILE CAM page's rules, pure.</summary>
    internal static class MissileCamMath
    {
        /// <summary>The page keeps the lost feed this long after the ride ends.</summary>
        public const float LostSeconds = 3.2f;
        /// <summary>Design units of the schematic per degree off the boresight (the prototype's 3.3 px).</summary>
        public const float PxPerDeg = 3.3f;

        /// <summary>
        /// The seeker's mode from what the game says: nothing to guide on is BLIND; a seeker that has its target is LOCKED; an active radar without a lock is on the data-link
        /// until its first lock (the pitbull) and BLIND after it; a semi-active one without a lock is in SEARCH.
        /// </summary>
        public static CamMode ModeOf(bool arh, bool everPitbull, bool locked, bool hasTarget)
        {
            if (!hasTarget) return CamMode.Blind;
            if (locked) return CamMode.Locked;
            if (arh) return everPitbull ? CamMode.Blind : CamMode.DataLink;
            return CamMode.Search;
        }

        /// <summary>The mode in a word. A weapon that never had a target (a dumb bomb, a round fired blind) is not BLIND, it has NO TGT.</summary>
        public static string ModeWord(CamMode mode, bool hadTarget)
        {
            switch (mode)
            {
                case CamMode.DataLink: return "DLINK";
                case CamMode.Locked: return "LOCKED";
                case CamMode.Search: return "SEARCH";
                case CamMode.Blind: return hadTarget ? "BLIND" : "NO TGT";
                default: return "LOST";
            }
        }

        /// <summary>The seeker is not on the target it was given: the caution states. A weapon that never had one gives no caution.</summary>
        public static bool Caution(CamMode mode, bool hadTarget) => hadTarget && (mode == CamMode.Blind || mode == CamMode.Search);

        /// <summary>How the ride ended, in a word: the shot's result, or LOST while the game has not said (or never will: a decoy).</summary>
        public static string ResultWord(ShotState result) =>
            result == ShotState.Hit ? "HIT" : result == ShotState.Miss ? "MISS" : result == ShotState.Defeated ? "DEFEATED" : "LOST";

        /// <summary>The spacing of the range tape's ticks: the smallest 1 / 2 / 5 step of the player's distance unit (<paramref name="unitM"/> metres: a km, a nautical mile) that puts at most 14 on the tape.</summary>
        public static float TapeStep(float topM, float unitM)
        {
            float step = unitM * 0.1f;
            for (int i = 0; topM / step > 14f && i < 15; i++) step *= i % 3 == 1 ? 2.5f : 2f;      // 0.1, 0.2, 0.5, 1, 2, 5, 10...
            return step;
        }

        /// <summary>The angle off the boresight in degrees of the page's two error angles.</summary>
        public static float OffBoresight(float exDeg, float eyDeg) => (float)Math.Sqrt(exDeg * exDeg + eyDeg * eyDeg);

        /// <summary>Where the target stands against the boresight as the page's angles, right and down, in degrees (a vector in the missile's frame: x right, y up, z ahead; behind the missile reads past 90).</summary>
        public static void Error(float x, float y, float z, out float exDeg, out float eyDeg)
        {
            exDeg = (float)(Math.Atan2(x, z) * 180.0 / Math.PI);
            eyDeg = (float)(-Math.Atan2(y, Math.Sqrt(x * x + z * z)) * 180.0 / Math.PI);
        }

        /// <summary>The seeker gauge: full on the boresight, falling to the floor at the cone's edge, nothing without a lock; a seeker with no cone is simply full while locked.</summary>
        public static float Strength(CamMode mode, float offDeg, float coneDeg)
        {
            if (mode != CamMode.Locked) return 0f;
            if (coneDeg <= 0f) return 1f;
            float v = 1f - offDeg / (coneDeg * 1.15f);
            return v < 0.15f ? 0.15f : v > 1f ? 1f : v;
        }

        /// <summary>A range as its share of the tape from the TARGET at the top (0) down to the top range (1): the missile marker rises toward the target as the range to go falls.</summary>
        public static float TapeFrac(float rangeM, float topM) => topM <= 1f || !(rangeM > 0f) ? 0f : rangeM >= topM ? 1f : rangeM / topM;

        /// <summary>The chain cell that is lit (0 BOOST, 1 MID, 2 ACTIVE, 3 TERM); a bomb has no chain (-1).</summary>
        public static int ChainIndex(ShotPhase phase) => phase == ShotPhase.Fall ? -1 : (int)phase;

        /// <summary>Half the side of the target box in design units: it grows as the range to go falls (36 units at a km), from 16 (far, or a range the game does not give) to 69.</summary>
        public static float BoxHalf(float rangeM) => !(rangeM > 0f) ? 16f : Math.Max(16f, Math.Min(60f, 36000f / rangeM) + 9f);

        /// <summary>Cut a segment to a rectangle (the view has no mask of its own: a tilted horizon must end at its frame); false = none of it is inside.</summary>
        public static bool ClipLine(ref float x0, ref float y0, ref float x1, ref float y1, float xMin, float yMin, float xMax, float yMax)
        {
            float dx = x1 - x0, dy = y1 - y0, t0 = 0f, t1 = 1f;
            if (!ClipEdge(-dx, x0 - xMin, ref t0, ref t1) || !ClipEdge(dx, xMax - x0, ref t0, ref t1) ||
                !ClipEdge(-dy, y0 - yMin, ref t0, ref t1) || !ClipEdge(dy, yMax - y0, ref t0, ref t1)) return false;
            x1 = x0 + dx * t1; y1 = y0 + dy * t1;
            x0 += dx * t0; y0 += dy * t0;
            return true;
        }

        /// <summary>
        /// Cut a hole out of a segment: the parts of (<paramref name="x0"/>, <paramref name="y0"/>) to (<paramref name="x1"/>, <paramref name="y1"/>) outside the rectangle, written to <paramref name="pieces"/> as x0, y0, x1, y1 each (eight
        /// floats) in the segment's order, and how many there are (0 to 2: none when it lies inside, one when the hole holds an end, two when it crosses). -1: the segment does not touch the rectangle, so there is nothing to cut.
        /// </summary>
        public static int ClipOut(float x0, float y0, float x1, float y1, float xMin, float yMin, float xMax, float yMax, float[] pieces)
        {
            float ax = x0, ay = y0, bx = x1, by = y1;
            if (!ClipLine(ref ax, ref ay, ref bx, ref by, xMin, yMin, xMax, yMax)) return -1;
            int n = 0;
            if ((ax - x0) * (ax - x0) + (ay - y0) * (ay - y0) > 0.25f) { pieces[n * 4] = x0; pieces[n * 4 + 1] = y0; pieces[n * 4 + 2] = ax; pieces[n * 4 + 3] = ay; n++; }
            if ((x1 - bx) * (x1 - bx) + (y1 - by) * (y1 - by) > 0.25f) { pieces[n * 4] = bx; pieces[n * 4 + 1] = by; pieces[n * 4 + 2] = x1; pieces[n * 4 + 3] = y1; n++; }
            return n;
        }

        private static bool ClipEdge(float p, float q, ref float t0, ref float t1)
        {
            if (p == 0f) return q >= 0f;
            float r = q / p;
            if (p < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }

        /// <summary>The highest altitude of the path so far and the sample it is at (-1: no path).</summary>
        public static int Apex(float[] altitude, int count, out float apexM)
        {
            int at = -1;
            apexM = 0f;
            for (int i = 0; i < count; i++)
                if (at < 0 || altitude[i] > apexM) { at = i; apexM = altitude[i]; }
            return at;
        }
    }
}
