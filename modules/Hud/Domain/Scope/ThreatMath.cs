using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What the THREAT page tells the pilot to do right now (it drives the action box, the beam wedge and the dispenser highlight).</summary>
    internal enum ThreatCue : byte
    {
        /// <summary>Nothing painting us, nothing inbound (the page is not live then, but a defeat banner can run out into it).</summary>
        Clear,
        Spike,
        Lock,
        /// <summary>A missile just lost us: a banner for a moment.</summary>
        Done,
        /// <summary>An inbound missile whose seeker the board has no counter for.</summary>
        Inbound,
        /// <summary>Radar missile, not beamed yet: turn to put it on the wing line.</summary>
        Beam,
        Chaff,
        Flare,
    }

    /// <summary>The THREAT page's rules (the prototype's <c>pages/threat.js</c> maths), pure: the time-to-impact ring scale, the beam cue, the nearest missile and the action cue.</summary>
    internal static class ThreatMath
    {
        /// <summary>The outer ring is 20 s to impact; a missile further out than <see cref="RingCap"/> sits just outside it.</summary>
        public const float RingSeconds = 20f, RingCap = 22f;
        /// <summary>A missile within this many degrees of the wing line is beamed.</summary>
        public const float BeamTolerance = 4f;
        /// <summary>A radar missile this close gets chaff whether or not it is beamed (there is no time left to turn).</summary>
        public const float ChaffNowS = 5f;
        /// <summary>Time to impact at or under this is the warning figure (the headline flashes, the ring counts down).</summary>
        public const float WarnS = 7f;
        /// <summary>The beam side only flips when the other wing line is this many degrees nearer (a missile near the tail must not flap the cue).</summary>
        public const float SideSwitch = 30f;
        /// <summary>A missile that lost us stays on the scope this long; the fade runs over the last of it.</summary>
        public const float GhostS = 2.6f, GhostFadeS = 1.5f;

        public static float Norm360(float deg) => (deg % 360f + 360f) % 360f;

        /// <summary>Degrees folded into (-180, 180].</summary>
        public static float Norm180(float deg)
        {
            float d = Norm360(deg);
            return d > 180f ? d - 360f : d;
        }

        /// <summary>Radius as a fraction of the scope's, square-root scaled so the 5 s ring is not a dot (the prototype's <c>rr</c>); an unknown time sits at the cap.</summary>
        public static float RingFrac(float ttiS)
        {
            float v = float.IsNaN(ttiS) ? RingCap : ttiS < 0f ? 0f : ttiS > RingCap ? RingCap : ttiS;
            return (float)Math.Sqrt(v / RingSeconds);
        }

        public static bool Warn(float ttiS) => !float.IsNaN(ttiS) && ttiS <= WarnS;

        /// <summary>The wing line to put a radar missile on: +1 right (090), -1 left (270). Starts on the nearer side and then holds it.</summary>
        public static int BeamSide(float bearingDeg, int previous)
        {
            float b = Norm360(bearingDeg);
            int nearer = b < 180f ? 1 : -1;
            if (previous == 0 || nearer == previous) return previous == 0 ? nearer : previous;
            return Math.Abs(BeamOff(b, previous)) - Math.Abs(BeamOff(b, nearer)) >= SideSwitch ? nearer : previous;
        }

        /// <summary>Degrees still to turn to put the missile on its wing line: positive = turn LEFT (the bearing has to grow), negative = turn right.</summary>
        public static float BeamOff(float bearingDeg, int side) => Norm180((side > 0 ? 90f : 270f) - bearingDeg);

        public static bool Beamed(float off) => Math.Abs(off) <= BeamTolerance;

        /// <summary>Index of the missile that matters most: live, soonest to arrive, a known time before an unknown one, then the nearest; -1 when none is live.</summary>
        public static int Nearest(ThreatMissile[] missiles, int count)
        {
            int best = -1;
            for (int i = 0; i < count; i++)
            {
                if (!missiles[i].Live) continue;
                if (best < 0 || Sooner(missiles[i], missiles[best])) best = i;
            }
            return best;
        }

        private static bool Sooner(in ThreatMissile a, in ThreatMissile b)
        {
            bool ka = !float.IsNaN(a.TtiS), kb = !float.IsNaN(b.TtiS);
            if (ka != kb) return ka;
            if (ka && a.TtiS != b.TtiS) return a.TtiS < b.TtiS;
            if (a.RangeM != b.RangeM) return a.RangeM < b.RangeM;
            return a.Id < b.Id;
        }

        /// <summary>
        /// The action cue: the nearest live missile first (beam it, then chaff; flares for an IR seeker), then a banner for a missile that
        /// just lost us, then a radar lock, then a spike. <paramref name="near"/> is only read when <paramref name="hasNear"/>.
        /// </summary>
        public static ThreatCue Cue(bool hasNear, in ThreatMissile near, bool recentDefeat, bool locked, bool spiked)
        {
            if (hasNear)
            {
                if (near.Class == ThreatSeeker.Ir) return ThreatCue.Flare;
                if (near.Class == ThreatSeeker.Other) return ThreatCue.Inbound;
                float off = BeamOff(near.BearingDeg, near.BeamSide == 0 ? BeamSide(near.BearingDeg, 0) : near.BeamSide);
                bool late = !float.IsNaN(near.TtiS) && near.TtiS <= ChaffNowS;
                return Beamed(off) || late ? ThreatCue.Chaff : ThreatCue.Beam;
            }
            if (recentDefeat) return ThreatCue.Done;
            if (locked) return ThreatCue.Lock;
            return spiked ? ThreatCue.Spike : ThreatCue.Clear;
        }

        /// <summary>The fade of a missile that lost us: 1 until <see cref="GhostFadeS"/>, 0 at <see cref="GhostS"/>.</summary>
        public static float GhostFade(float defeatedS) => 1f - Math.Min(1f, Math.Max(0f, (defeatedS - GhostFadeS) / (GhostS - GhostFadeS)));

        /// <summary>The seeker word's class, the same split as the countermeasure stations' (<see cref="HudCm.SeekerMask"/>): SARH and ARH are beamed and chaffed, IR is flared, anything else is just shown.</summary>
        public static ThreatSeeker ClassOf(string seeker) =>
            seeker == "IR" ? ThreatSeeker.Ir : seeker == "SARH" || seeker == "ARH" ? ThreatSeeker.Radar : ThreatSeeker.Other;

        /// <summary>The detail of a radar's ticker notice ("RANGE 36KM"): the ticker pages a notice's text, then its detail, each alone on its line, so a bare "36KM" said nothing in game (the second page of "SPIKE - HYPERION CLASS CARRIER").</summary>
        public static string RangeDetail(float metres, NOAvionics.AvUnits units) => ScopeStr.Distance("RANGE", metres, units, 16).ToString();

        /// <summary>The widest a radar's tag on the scope is, in characters: the game's names run to 22 ("HYPERION CLASS CARRIER" in game) and three tags that wide cannot share the plot.</summary>
        public const int TagChars = 12;

        /// <summary>
        /// A radar's name as its tag on the scope prints it: whole when it fits <see cref="TagChars"/>, else the whole words that do ("HYPERION CLASS CARRIER" reads HYPERION; the ticker and the gauge line carry the full name),
        /// a first word that is itself too long cut, never an ellipsis (the precedent of <c>ShotMath.ChipName</c>). Written into <paramref name="into"/> (at least <see cref="TagChars"/> long); returns the length.
        /// </summary>
        public static int TagName(string name, Span<char> into) => TagName(name, into, TagChars);

        /// <summary><see cref="TagName(string,Span{char})"/> for any width: the whole words of <paramref name="name"/> that fit <paramref name="max"/> characters (<paramref name="into"/> holds at least that many).</summary>
        public static int TagName(string name, Span<char> into, int max)
        {
            if (string.IsNullOrEmpty(name) || max < 1) return 0;
            int a = 0, b = name.Length;
            while (a < b && name[a] == ' ') a++;
            while (b > a && name[b - 1] == ' ') b--;
            int len = b - a;
            if (len > max)
            {
                int cut = max;
                if (name[a + cut] != ' ')      // a word straddles the limit: back up to the space before it
                    while (cut > 0 && name[a + cut - 1] != ' ') cut--;
                while (cut > 0 && name[a + cut - 1] == ' ') cut--;
                len = cut > 0 ? cut : max;      // the first word alone is too long: cut it
            }
            for (int i = 0; i < len; i++) into[i] = name[a + i];
            return len;
        }
    }
}
