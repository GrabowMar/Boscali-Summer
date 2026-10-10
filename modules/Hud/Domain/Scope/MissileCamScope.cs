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

        public static string ModeWord(CamMode mode)
        {
            switch (mode)
            {
                case CamMode.DataLink: return "DLINK";
                case CamMode.Locked: return "LOCKED";
                case CamMode.Search: return "SEARCH";
                case CamMode.Blind: return "BLIND";
                default: return "LOST";
            }
        }

        /// <summary>The seeker is not on its target although it should be: the caution states.</summary>
        public static bool Caution(CamMode mode) => mode == CamMode.Blind || mode == CamMode.Search;

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
