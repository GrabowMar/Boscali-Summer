using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What the plate under the contract's name says (the line the pilot reads first).</summary>
    internal enum ObjectiveLine : byte { ContactLost, TooLow, NoSightline, NoTrack, Progress, Next }

    /// <summary>One known air-defence ring against the pilot: the distance to its centre, the reach at the pilot's height and whether the pilot is inside it.</summary>
    internal struct SamExposure
    {
        public float DistanceM, ReachM, MaskM, BearingDeg;
        public bool Inside;
    }

    /// <summary>
    /// The OBJECTIVE page's rules (pages/objective.js), pure: the plot's auto range with its hysteresis, bearings, the air-defence reach at the
    /// pilot's height (vanilla <c>CombatAI.AnalyzeTarget</c> solved for distance, as <see cref="AirDefenceRing.EffectiveRadius(float)"/>), the plate's
    /// line, the hold-gate words and the headline's choice. No UnityEngine.
    /// </summary>
    internal static class ObjectiveMath
    {
        /// <summary>The plot's ranges (kilometres from the zone centre to the edge of the plot).</summary>
        private static readonly float[] Ladder = { 1.2f, 2f, 3.2f, 5f, 8f, 12f, 20f, 32f, 50f, 80f, 130f, 200f };

        /// <summary>The zone ring fills at least this share of the plot's range.</summary>
        public const float ZoneShare = 0.4f;
        /// <summary>The pilot sits inside the plot at this share of its range at most.</summary>
        public const float OwnShare = 0.9f;
        /// <summary>Radar SAM cones are drawn up to this height on the side view.</summary>
        public const float ConeCeilingM = 3000f;

        private static readonly float[] MetricBars = { 100f, 200f, 500f, 1000f, 2000f, 5000f, 10000f, 20000f, 50000f, 100000f };
        private static readonly float[] ImperialBars = { 926f, 1852f, 3704f, 9260f, 18520f, 37040f, 92600f };

        /// <summary>The scale bar's length in metres: the longest round length (a kilometre step, or half a nautical mile and up in imperial) that still fits <paramref name="maxPx"/> at this scale.</summary>
        public static float ScaleBar(float pxPerKm, bool imperial, float maxPx)
        {
            float[] bars = imperial ? ImperialBars : MetricBars;
            float best = bars[0];
            for (int i = 0; i < bars.Length; i++) if (bars[i] / 1000f * pxPerKm <= maxPx) best = bars[i];
            return best;
        }

        /// <summary>The range wanted to show the zone and the pilot: the larger of the zone's radius over its share and the pilot's distance over its share.</summary>
        public static float Need(float zoneRadiusKm, float ownDistanceKm) => Math.Max(zoneRadiusKm / ZoneShare, ownDistanceKm / OwnShare);

        /// <summary>
        /// The plot range for a need, with hysteresis: it grows to the smallest step that holds the need at once, and shrinks only when the need
        /// has fallen well under the step below (so a pilot circling the edge does not make the scale breathe).
        /// </summary>
        public static float Range(float current, float need)
        {
            int cur = IndexOf(current);
            if (need > Ladder[cur]) return Ladder[Fit(need)];
            if (cur > 0 && need < Ladder[cur - 1] * 0.7f) return Ladder[Fit(need / 0.7f)];
            return Ladder[cur];
        }

        private static int Fit(float need)
        {
            for (int i = 0; i < Ladder.Length; i++) if (Ladder[i] >= need) return i;
            return Ladder.Length - 1;
        }

        private static int IndexOf(float range)
        {
            for (int i = 0; i < Ladder.Length; i++) if (Math.Abs(Ladder[i] - range) < 0.001f) return i;
            return Fit(range);
        }

        /// <summary>Compass bearing (degrees clockwise from north) of the offset east <paramref name="dx"/>, north <paramref name="dz"/>.</summary>
        public static float Bearing(float dx, float dz) => (float)((Math.Atan2(dx, dz) * 180.0 / Math.PI % 360.0 + 360.0) % 360.0);

        /// <summary>The bearing from the pilot to the zone's centre, relative to the nose, 0..360.</summary>
        public static float Relative(float bearing, float heading) => ((bearing - heading) % 360f + 360f) % 360f;

        public static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The pilot against one ring.</summary>
        public static SamExposure Expose(in AirDefenceRing ring, float ownX, float ownZ, float agl)
        {
            float d = Distance(ownX, ownZ, ring.X, ring.Z), reach = ring.EffectiveRadius(agl);
            return new SamExposure
            {
                DistanceM = d, ReachM = reach, Inside = d < reach,
                MaskM = ring.MinAltitude > 0f && ring.MaxRange > 0f ? ring.MinAltitude * d / ring.MaxRange : 0f,
                BearingDeg = Bearing(ring.X - ownX, ring.Z - ownZ),
            };
        }

        /// <summary>Does this ring draw as a radar cone (a radar SAM or a launch-only ring avoided like one)?</summary>
        public static bool Radar(in AirDefenceRing ring) => (ThreatPictureLimits.RadarConeMask & ThreatPictureLimits.Bit(ring.Kind)) != 0;

        /// <summary>The nearest radar ring to the pilot (the SAM gauge and the cone's subject), -1 when none.</summary>
        public static int NearestRadar(AirDefenceRing[] rings, int count, float ownX, float ownZ)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < count && i < rings.Length; i++)
            {
                if (!Radar(rings[i])) continue;
                float d = Distance(ownX, ownZ, rings[i].X, rings[i].Z);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>The ring's short word: RADAR SAM, IR SAM, GUN, UNK (a ring known only from a launch).</summary>
        public static string Kind(AirDefenceKind kind) =>
            kind == AirDefenceKind.RadarSam ? "SAM" : kind == AirDefenceKind.IrSam ? "IR SAM" : kind == AirDefenceKind.Gun ? "GUN" : "SAM?";

        // ------------------------------------------------------------------------------------------------ the plate and the gates

        public static ObjectiveLine Line(in ObjectiveScope d)
        {
            if (!d.HasMarker || d.Contact == ObjectiveContact.Lost || d.Contact == ObjectiveContact.Unavailable) return ObjectiveLine.ContactLost;
            if (d.HasGates && d.GateRange)
            {
                if (!d.GateAlt) return ObjectiveLine.TooLow;
                if (!d.GateTrack) return ObjectiveLine.NoTrack;
                if (!d.GateLos) return ObjectiveLine.NoSightline;
            }
            return Progress(d) > 0f ? ObjectiveLine.Progress : ObjectiveLine.Next;
        }

        /// <summary>The current phase's progress 0..1 (0 when there is none or it has not started).</summary>
        public static float Progress(in ObjectiveScope d) =>
            d.Phases != null && d.Current >= 0 && d.Current < d.PhaseCount && d.Current < d.Phases.Length ? Clamp01(d.Phases[d.Current].Progress) : 0f;

        /// <summary>The line is a caution: something keeps the hold from counting.</summary>
        public static bool Warns(ObjectiveLine line) => line == ObjectiveLine.TooLow || line == ObjectiveLine.NoSightline || line == ObjectiveLine.NoTrack || line == ObjectiveLine.ContactLost;

        public static string LineWord(ObjectiveLine line) =>
            line == ObjectiveLine.ContactLost ? "CONTACT LOST" : line == ObjectiveLine.TooLow ? "TOO LOW - CLIMB" : line == ObjectiveLine.NoSightline ? "NO SIGHTLINE - CLIMB" :
            line == ObjectiveLine.NoTrack ? "NO FRESH TRACK" : null;

        /// <summary>The contact's quality as a word: FIXED, KNOWN, LAST KNOWN, LOST.</summary>
        public static string Contact(ObjectiveContact contact) =>
            contact == ObjectiveContact.Fixed ? "FIXED" : contact == ObjectiveContact.Known ? "KNOWN" : contact == ObjectiveContact.LastKnown ? "LAST KNOWN" : "LOST";

        /// <summary>
        /// Word-wraps <paramref name="text"/> into at most <paramref name="lines"/>.Length lines of at most <paramref name="width"/> characters, cutting at spaces (a longer
        /// word is cut where it falls) and ending the last line with an ellipsis when the text runs on. Returns the lines used. Allocates: call it when the text changes.
        /// </summary>
        public static int Wrap(string text, int width, string[] lines)
        {
            int n = 0, at = 0;
            while (text != null && n < lines.Length && width > 1)
            {
                while (at < text.Length && text[at] == ' ') at++;
                if (at >= text.Length) break;
                bool last = n == lines.Length - 1;
                int cut = Math.Min(text.Length, at + width);
                if (cut < text.Length)
                {
                    int limit = last ? at + width - 1 : cut;       // the last line leaves room for its ellipsis
                    int space = text.LastIndexOf(' ', limit, limit - at + 1);
                    cut = space > at ? space : limit;
                }
                string line = text.Substring(at, cut - at);
                lines[n++] = last && cut < text.Length ? line + "…" : line;
                at = cut;
            }
            return n;
        }

        /// <summary>The deadline reads as a caution under a minute.</summary>
        public static bool DeadlineLow(float left) => left > 0f && left < 60f;

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
