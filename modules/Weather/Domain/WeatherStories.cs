using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    internal enum StoryKind : byte { FrontalSystem = 0, ColdFront = 1, ConvectiveDay = 2, Occluded = 3, HighPressure = 4 }

    /// <summary>One segment of a story (spec 5.1): where it sits (<see cref="Role"/>), how long it is, which skies it draws from (1-based sky numbers of spec 4.4)
    /// and what its start boundary means. Arrival order is increasing s, so the start boundary is the edge that reaches a point first.</summary>
    internal sealed class SegmentDef
    {
        public SegmentDef(string label, Role role, float minKm, float maxKm, int[] pool, float stepKm = 0f, bool progressive = false,
            MarkerKind? marker = null, bool squallAllowed = false, bool lead = false, bool main = false)
        {
            Label = label; Role = role; MinKm = minKm; MaxKm = maxKm; Pool = pool; StepKm = stepKm; Progressive = progressive;
            Marker = marker; SquallAllowed = squallAllowed; Lead = lead; Main = main;
        }

        public readonly string Label;
        public readonly Role Role;
        public readonly float MinKm, MaxKm, StepKm;
        public readonly int[] Pool;
        /// <summary>Keyframes inside the segment walk the pool in order (the altostratus lowering), instead of picking at random.</summary>
        public readonly bool Progressive;
        /// <summary>A marker on the start boundary (the warm line is the boundary between the warm body and the warm sector).</summary>
        public readonly MarkerKind? Marker;
        /// <summary>The cold marker becomes a squall marker when the squall line (sky 37) is the pick.</summary>
        public readonly bool SquallAllowed;
        /// <summary>The start boundary is the front's s_lead (first change) and / or s_main (the warm body, or the marker of a cold front).</summary>
        public readonly bool Lead, Main;
        /// <summary>Under Story auto this segment (Ridge, Fair or Clearing, the first of a story) is replaced by the opening run (spec 5.1).</summary>
        public bool OpeningCandidate => Role == Role.Ridge || Role == Role.Clearing || Role == Role.HighPress;
    }

    internal sealed class StoryDef
    {
        public StoryDef(string name, bool bearsFront, params SegmentDef[] segments) { Name = name; BearsFront = bearsFront; Segments = segments; }
        public readonly string Name;
        public readonly bool BearsFront;
        public readonly SegmentDef[] Segments;
    }

    /// <summary>The five stories of the first bench review (spec 5.1). Stationary and Marine are added after the slice 4 bench review, which is when
    /// <see cref="Count"/>, <see cref="WeatherKey.StoryCount"/> and the wire bump together.</summary>
    internal static class WeatherStories
    {
        public const int Count = 5;

        private static readonly int[] Ridge = { 8, 9, 11, 12, 2, 7 }, Cirrus = { 2, 3 }, Veil = { 4 }, Alto = { 23, 27 }, AltoOcc = { 23, 33, 27 },
            WarmBody = { 34, 35, 40 }, WarmSector = { 26, 30, 22, 29, 39, 36 }, ColdLine = { 37, 45 }, Clearing = { 18, 19 },
            PostFrontal = { 21, 10, 14, 8 }, Fair = { 8, 9, 11, 12 }, Mediocris = { 15 }, Congestus = { 16 }, Showers = { 41, 17 },
            Cluster = { 42, 44, 47, 28 }, Decay = { 46, 20, 25 }, OccBody = { 43, 40, 35 }, OccShowers = { 41, 17 }, OccFair = { 14, 8 },
            Blue = { 1 }, Mackerel = { 5 }, Cumulus = { 8 };

        private static readonly StoryDef[] Stories =
        {
            new StoryDef("Frontal system", true,
                new SegmentDef("RIDGE", Role.Ridge, 40, 80, Ridge),
                new SegmentDef("CI", Role.PreWarm, 20, 25, Cirrus, lead: true),
                new SegmentDef("CS", Role.PreWarm, 20, 25, Veil),
                new SegmentDef("AS", Role.PreWarm, 25, 30, Alto, stepKm: 14, progressive: true),
                new SegmentDef("WARM BODY", Role.WarmBody, 30, 40, WarmBody, stepKm: 14, progressive: true, main: true),
                new SegmentDef("WARM SECTOR", Role.WarmSector, 40, 80, WarmSector, stepKm: 40, marker: MarkerKind.Warm),
                new SegmentDef("COLD LINE", Role.ColdLine, 20, 30, ColdLine, marker: MarkerKind.Cold),
                new SegmentDef("CLEARING", Role.Clearing, 20, 40, Clearing),
                new SegmentDef("POST-FRONT", Role.PostFrontal, 60, 120, PostFrontal, stepKm: 40)),
            new StoryDef("Cold front", true,
                new SegmentDef("RIDGE", Role.Ridge, 40, 80, Ridge),
                new SegmentDef("COLD LINE", Role.ColdLine, 20, 30, ColdLine, marker: MarkerKind.Cold, squallAllowed: true, lead: true, main: true),
                new SegmentDef("CLEARING", Role.Clearing, 20, 40, Clearing),
                new SegmentDef("POST-FRONT", Role.PostFrontal, 60, 120, PostFrontal, stepKm: 40)),
            new StoryDef("Convective day", false,
                new SegmentDef("FAIR", Role.Ridge, 30, 60, Fair),
                new SegmentDef("BUILD", Role.Build, 30, 50, Mediocris),
                new SegmentDef("BUILD", Role.Build, 25, 40, Congestus),
                new SegmentDef("CELL", Role.Cell, 25, 50, Showers),
                new SegmentDef("CELL", Role.Cell, 25, 40, Cluster),
                new SegmentDef("DECAY", Role.Decay, 40, 80, Decay)),
            new StoryDef("Occluded", true,
                new SegmentDef("CS", Role.PreWarm, 20, 25, Veil, lead: true),
                new SegmentDef("AS", Role.PreWarm, 45, 55, AltoOcc, stepKm: 17, progressive: true),
                new SegmentDef("WARM BODY", Role.WarmBody, 30, 50, OccBody, stepKm: 20, marker: MarkerKind.Occluded, main: true),
                new SegmentDef("SHOWERS", Role.Cell, 25, 40, OccShowers),
                new SegmentDef("FAIR", Role.PostFrontal, 40, 80, OccFair)),
            new StoryDef("High pressure", false,
                new SegmentDef("BLUE", Role.HighPress, 60, 120, Blue),
                new SegmentDef("CIRRUS", Role.HighPress, 60, 120, Cirrus),
                new SegmentDef("MACKEREL", Role.HighPress, 60, 120, Mackerel),
                new SegmentDef("FAIR", Role.HighPress, 60, 120, Cumulus)),
        };

        public static StoryDef Get(int index) => Stories[index];
        public static string Name(int index) => Stories[index].Name;

        /// <summary>The share of a story's length (segment midpoints) whose skies are of <paramref name="cls"/> or a neighbouring class: the weight of
        /// the story for a mission of that class is 1 + 4 * share (spec 5.1).</summary>
        public static float Share(int story, WeatherRegimeType cls)
        {
            float near = 0f, total = 0f;
            foreach (SegmentDef seg in Stories[story].Segments)
            {
                float len = 0.5f * (seg.MinKm + seg.MaxKm);
                total += len;
                foreach (int sky in seg.Pool)
                    if (Math.Abs((int)SkyLibrary.ClassOf(sky - 1) - (int)cls) <= 1) { near += len; break; }
            }
            return total > 0f ? near / total : 0f;
        }

        public static float Weight(int story, WeatherRegimeType cls) => 1f + 4f * Share(story, cls);

        /// <summary>A hashed pick among <paramref name="candidates"/> with the class weights; <paramref name="roll"/> is a uniform [0, 1).</summary>
        public static int Pick(int[] candidates, WeatherRegimeType cls, float roll)
        {
            float total = 0f;
            foreach (int s in candidates) total += Weight(s, cls);
            float at = roll * total;
            foreach (int s in candidates)
            {
                at -= Weight(s, cls);
                if (at < 0f) return s;
            }
            return candidates[candidates.Length - 1];
        }

        public static readonly int[] FrontStories = { (int)StoryKind.FrontalSystem, (int)StoryKind.ColdFront, (int)StoryKind.Occluded };
        public static readonly int[] AllStories = { 0, 1, 2, 3, 4 };
    }
}
