using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>The tape identity (spec 3.2) as plain values: everything <see cref="WeatherTape.Generate"/> reads, and nothing else.</summary>
    internal readonly struct TapeInputs
    {
        public TapeInputs(uint seed, byte story, byte skyPin, byte layoutSalt, byte missionClass, float openingBase, uint id)
        { Seed = seed; Story = story; SkyPin = skyPin; LayoutSalt = layoutSalt; MissionClass = missionClass; OpeningBase = openingBase; Id = id; }

        public readonly uint Seed, Id;
        public readonly byte Story, SkyPin, LayoutSalt, MissionClass;
        public readonly float OpeningBase;

        public static TapeInputs From(WeatherKey k) => new TapeInputs(k.Seed, k.Story, k.SkyPin, k.LayoutSalt, k.MissionClass, Quantised(k.OpeningBase), k.TapeId);

        /// <summary>The opening base in the 10 m steps <see cref="WeatherKey.TapeId"/> hashes it in: Generate must read what the identity sees, or two keys with one TapeId (1501 m and 1504 m)
        /// would generate different tapes and the cache would hand each peer whichever came first.</summary>
        private static float Quantised(float openingBase) =>
            (float)(Math.Round(openingBase / WeatherKey.OpeningBaseStep, MidpointRounding.AwayFromZero) * WeatherKey.OpeningBaseStep);

        /// <summary>Same value as <see cref="WeatherKey.LayoutSeed"/>.</summary>
        public uint LayoutSeed => Deterministic.Hash((int)Seed, LayoutSalt, 0x4C41, 0x5EED);
    }

    /// <summary>One keyframe: a sky instance that holds from its start boundary (the ramp midpoint <see cref="S"/>, 0 for the first) to the next.</summary>
    internal readonly struct TapeKeyframe
    {
        public TapeKeyframe(float s, float width, in SkyParams sky, LowGenus genus, bool hasLow, int skyIndex, int segment)
        { S = s; Width = width; Sky = sky; Genus = genus; HasLow = hasLow; SkyIndex = skyIndex; Segment = segment; }

        public readonly float S, Width;
        public readonly SkyParams Sky;
        public readonly LowGenus Genus;
        public readonly bool HasLow;
        public readonly int SkyIndex, Segment;
        public float RampStart => S - 0.5f * Width;
        public float RampEnd => S + 0.5f * Width;
    }

    /// <summary>A role block of the story, for the console strip and the tests: where it starts and ends along the tape (ramp midpoints).</summary>
    internal readonly struct TapeSegment
    {
        public TapeSegment(string label, Role role, WeatherRegimeType cls, float s0, float s1, int story, int firstKeyframe)
        { Label = label; Role = role; Class = cls; S0 = s0; S1 = s1; Story = story; FirstKeyframe = firstKeyframe; }
        public readonly string Label;
        public readonly Role Role;
        public readonly WeatherRegimeType Class;
        public readonly float S0, S1;
        public readonly int Story, FirstKeyframe;
    }

    /// <summary>A front of the tape: s_lead and s_main are ramp starts (spec 5.1), Dpre = s_main - s_lead.</summary>
    internal readonly struct TapeFront
    {
        public TapeFront(int story, float sLead, float sMain, int marker) { Story = story; SLead = sLead; SMain = sMain; Marker = marker; }
        public readonly int Story, Marker;
        public readonly float SLead, SMain;
        public float Dpre => SMain - SLead;
    }

    /// <summary>
    /// The weather tape (spec 5): a one-dimensional picture along s, a pure function of the tape identity. Stories become segments, segments become
    /// keyframes (sky instances) blended by contrast-scaled ramps, front markers sit on chosen boundaries, and one shared meander bends every
    /// boundary into a parallel wavy line. Immutable once generated and cached by <see cref="WeatherKey.TapeId"/>.
    /// </summary>
    internal sealed class WeatherTape
    {
        public const float SpanMetres = BakedTape.SpanMetres;
        public const int MaxKeyframes = 256;
        public const float OpeningRunMinMetres = 250000f, TailMinMetres = 300000f;

        private BakedTape baked;
        private readonly object bakeLock = new object();

        private WeatherTape(TapeInputs inputs) { Inputs = inputs; }

        public TapeInputs Inputs { get; private set; }
        public TapeKeyframe[] Keyframes { get; private set; }
        public TapeSegment[] Segments { get; private set; }
        public TapeFront[] Fronts { get; private set; }
        public MarkerSet Markers { get; private set; }
        public float MeanderAmplitude { get; private set; }
        public float MeanderWavelength { get; private set; }
        public float MeanderPhase { get; private set; }
        /// <summary>The opening run's end boundary (ramp midpoint) in metres; 0 when there is no run (a pinned story or sky).</summary>
        public float OpeningEnd { get; private set; }
        /// <summary>True when the first story is a front (the front rule, or a pinned front-bearing story): Solve may place a front opening.</summary>
        public bool OpensWithFront { get; private set; }
        public int KeyframeCount => Keyframes.Length;
        public bool HasFront => Fronts.Length > 0;
        /// <summary>s_ev: the first front's s_lead (the ramp start of the opening run's end boundary), or NaN without a front.</summary>
        public float SEv => Fronts.Length > 0 ? Fronts[0].SLead : float.NaN;
        public WeatherRegimeType OpeningClass { get; private set; }

        /// <summary>The baked array and texture source (spec 7.2), built on first use and shared by every field.</summary>
        public BakedTape Baked { get { lock (bakeLock) return baked ?? (baked = TapeBaker.Bake(this)); } }

        /// <summary>The library index (0-based) of the sky showing at position s: the plateau's keyframe, or the nearer side of a ramp.</summary>
        public int SkyIndexAt(float s)
        {
            int ramp = TapeBaker.RampAt(Keyframes, s);
            int k = ramp >= 0 ? (s < Keyframes[ramp].S ? ramp - 1 : ramp) : TapeBaker.PlateauAt(Keyframes, s);
            return Keyframes[k].SkyIndex;
        }

        public TapeFrame Frame(float headingDeg, float offsetMetres, NoiseXform noise = null) =>
            new TapeFrame(headingDeg, offsetMetres, MeanderAmplitude, MeanderWavelength, MeanderPhase, noise);

        // ---- cache -----------------------------------------------------------------------------

        private static readonly List<WeatherTape> Cache = new List<WeatherTape>();

        /// <summary>The tape of a key: a 4-entry LRU behind a lock, so the main field, the forecast field, the lightning slots and the workers share one object.</summary>
        public static WeatherTape For(WeatherKey key)
        {
            lock (Cache)
            {
                for (int i = 0; i < Cache.Count; i++)
                    if (Cache[i].Inputs.Id == key.TapeId)
                    {
                        WeatherTape hit = Cache[i];
                        Cache.RemoveAt(i);
                        Cache.Insert(0, hit);
                        return hit;
                    }
            }
            WeatherTape made = Generate(TapeInputs.From(key));
            lock (Cache)
            {
                // Two threads may both have missed: the first to insert wins and the other shares its object (and its bake).
                for (int i = 0; i < Cache.Count; i++)
                    if (Cache[i].Inputs.Id == key.TapeId) return Cache[i];
                Cache.Insert(0, made);
                if (Cache.Count > 4) Cache.RemoveAt(Cache.Count - 1);
            }
            return made;
        }

        // ---- generation ------------------------------------------------------------------------

        private struct Block
        {
            public int Sky;                // 0-based library index
            public float Km;               // nominal length
            public int Segment;            // index into the segment list
            public bool Lead, Main;
            public MarkerKind? Marker;
            public float MarkerStrength;
        }

        private struct SegmentRow { public string Label; public Role Role; public int Story; public int FirstBlock; }

        public static WeatherTape Generate(TapeInputs inputs)
        {
            var tape = new WeatherTape(inputs);
            uint layout = inputs.LayoutSeed;
            // Picks hash the seed, the salt and the mission class only: the opening base never changes which sky is picked (spec 5.1).
            uint root = unchecked((uint)Deterministic.Hash((int)inputs.Seed, inputs.LayoutSalt, inputs.MissionClass, 0x7A9E));
            float U(int a, int b = 0, int c = 0) => WeatherMath.Hash01(root, a, b, c);

            tape.MeanderAmplitude = WeatherMath.HashRange(layout, 0x3A1, 0, 0, 6000f, 14000f);
            tape.MeanderWavelength = Math.Max(WeatherMath.HashRange(layout, 0x3A2, 0, 0, 70000f, 130000f), TapeFrame.MinWavelengthPerAmplitude * tape.MeanderAmplitude);
            tape.MeanderPhase = WeatherMath.Hash01(layout, 0x3A3) * 2f * (float)Math.PI;

            bool pinnedSky = inputs.SkyPin < SkyLibrary.Count;
            bool pinnedStory = !pinnedSky && inputs.Story < WeatherStories.Count;
            WeatherRegimeType cls = inputs.MissionClass < WeatherKey.ClassCount ? (WeatherRegimeType)inputs.MissionClass : NoClass(inputs.Seed);
            tape.OpeningClass = cls;

            var blocks = new List<Block>();
            var segments = new List<SegmentRow>();
            int openingSky = -1;

            if (pinnedSky) blocks.Add(new Block { Sky = inputs.SkyPin, Km = 3000f });
            else
            {
                if (!pinnedStory)
                {
                    openingSky = SkyLibrary.PickOpening(cls, root);
                    blocks.Add(new Block { Sky = openingSky, Km = 250f + 30f * U(1), Segment = 0 });
                    segments.Add(new SegmentRow { Label = "OPENING", Role = Role.None, Story = -1, FirstBlock = 0 });
                }
                int story = pinnedStory ? inputs.Story
                    : WeatherStories.Pick(cls == WeatherRegimeType.Clear || cls == WeatherRegimeType.Fair ? WeatherStories.FrontStories : WeatherStories.AllStories, cls, U(2));
                tape.OpensWithFront = WeatherStories.Get(story).BearsFront && (pinnedStory || cls == WeatherRegimeType.Clear || cls == WeatherRegimeType.Fair);
                for (int round = 0; round < 64; round++)
                {
                    float cursor = 0f;
                    foreach (Block b in blocks) cursor += b.Km;
                    if (cursor >= (pinnedStory ? 3000f : 3000f - TailMinMetres * 0.001f) || blocks.Count >= MaxKeyframes - 40) break;
                    if (round > 0 && !pinnedStory)
                    {
                        WeatherRegimeType previous = SkyLibrary.ClassOf(blocks[blocks.Count - 1].Sky);
                        story = WeatherStories.Pick(WeatherStories.AllStories, previous, U(3, round));
                    }
                    AppendStory(blocks, segments, story, skipFirst: round == 0 && !pinnedStory && WeatherStories.Get(story).Segments[0].OpeningCandidate, round, U);
                }
                if (!pinnedStory)
                {
                    // Stories end at 2600 km (positions grow a little when the ramps lengthen a segment) (the block that crosses it is cut), then the high-pressure tail: at least 300 km, the last keyframe holds beyond it.
                    float run = 0f;
                    int keep = 0;
                    while (keep < blocks.Count && run < 2600f) run += blocks[keep++].Km;
                    blocks.RemoveRange(keep, blocks.Count - keep);
                    if (run > 2600f) { Block last = blocks[keep - 1]; last.Km -= run - 2600f; blocks[keep - 1] = last; }
                    segments.RemoveAll(r => r.FirstBlock >= keep);
                    AppendStory(blocks, segments, (int)StoryKind.HighPressure, false, 99, U);
                    blocks.Add(new Block { Sky = 7, Km = 60f + 60f * U(4), Segment = segments.Count - 1 });
                }
            }

            tape.Build(blocks, segments, pinnedStory, openingSky >= 0);
            return tape;
        }

        private static WeatherRegimeType NoClass(uint seed)
        {
            // No authored class (a fixture): missions open flyable, mostly fair skies.
            float roll = WeatherMath.Hash01(seed, 7, 1);
            if (roll < 0.20f) return WeatherRegimeType.Clear;
            if (roll < 0.55f) return WeatherRegimeType.Fair;
            if (roll < 0.80f) return WeatherRegimeType.Scattered;
            if (roll < 0.92f) return WeatherRegimeType.Broken;
            return WeatherRegimeType.Overcast;
        }

        private static void AppendStory(List<Block> blocks, List<SegmentRow> segments, int story, bool skipFirst, int round, Func<int, int, int, float> u)
        {
            StoryDef def = WeatherStories.Get(story);
            for (int j = 0; j < def.Segments.Length; j++)
            {
                if (j == 0 && skipFirst) continue;
                SegmentDef seg = def.Segments[j];
                int row = segments.Count;
                segments.Add(new SegmentRow { Label = seg.Label, Role = seg.Role, Story = story, FirstBlock = blocks.Count });
                float km = WeatherMath.Lerp(seg.MinKm, seg.MaxKm, u(10 + round, j, story));
                int n = seg.StepKm > 0f ? Math.Max(1, (int)Math.Round(km / seg.StepKm)) : 1;
                int previous = -1;
                for (int b = 0; b < n; b++)
                {
                    int sky;
                    if (seg.Progressive) sky = seg.Pool[Math.Min(seg.Pool.Length - 1, (int)((long)b * seg.Pool.Length / n))];
                    else
                    {
                        int at = (int)(u(20 + round, j * 8 + b, story) * seg.Pool.Length);
                        if (at >= seg.Pool.Length) at = seg.Pool.Length - 1;
                        if (seg.Pool.Length > 1 && seg.Pool[at] == previous) at = (at + 1) % seg.Pool.Length;
                        sky = seg.Pool[at];
                    }
                    previous = sky;
                    var block = new Block { Sky = sky - 1, Km = km / n, Segment = row };
                    if (b == 0)
                    {
                        block.Lead = seg.Lead; block.Main = seg.Main;
                        if (seg.Marker.HasValue)
                        {
                            MarkerKind kind = seg.Marker.Value == MarkerKind.Cold && seg.SquallAllowed && sky == 37 ? MarkerKind.Squall : seg.Marker.Value;
                            block.Marker = kind;
                            float roll = u(30 + round, j, story);
                            block.MarkerStrength = roll < 0.5f ? 1f : WeatherMath.Lerp(0.7f, 1f, u(31 + round, j, story));
                        }
                    }
                    blocks.Add(block);
                }
            }
        }

        private void Build(List<Block> blocks, List<SegmentRow> segmentRows, bool pinnedStory, bool hasRun)
        {
            int n = blocks.Count;
            uint root = unchecked((uint)Deterministic.Hash((int)Inputs.Seed, Inputs.LayoutSalt, Inputs.MissionClass, 0x7A9E));
            var skies = new SkyParams[n];
            // Instances. The opening base reaches the opening run (auto) or the keyframes that start inside the first 250 km (a pinned story); a pinned sky is its own.
            float start = 0f;
            for (int i = 0; i < n; i++)
            {
                bool inOpening = Inputs.SkyPin < SkyLibrary.Count || (hasRun ? i == 0 : pinnedStory && start < OpeningRunMinMetres * 0.001f);
                float openingBase = inOpening ? Inputs.OpeningBase : 0f;
                uint seed = Inputs.SkyPin < SkyLibrary.Count ? unchecked(Inputs.Seed ^ (Inputs.LayoutSalt * 0x9E3779B1u)) : unchecked((uint)Deterministic.Hash((int)root, i, 0x1A5E));
                skies[i] = hasRun && i == 0 ? SkyLibrary.OpeningInstance(blocks[i].Sky, seed, openingBase, out _) : SkyLibrary.Instance(blocks[i].Sky, seed, openingBase);
                start += blocks[i].Km;
            }

            // Ramp widths, then positions: a segment is never shorter than half the sum of its two ramps, 12 km, or the clearance of a marker on either side.
            var width = new float[n + 1];
            for (int i = 1; i < n; i++)
                width[i] = RampWidth.Metres(skies[i - 1], skies[i], blocks[i].Marker.HasValue ? FrontLine.PeakRain(blocks[i].Marker.Value) : 0f);
            var at = new float[n + 1];
            for (int i = 0; i < n; i++)
            {
                float min = Math.Max(RampWidth.MinMetres, 0.5f * (width[i] + width[i + 1]));
                if (blocks[i].Marker.HasValue) min = Math.Max(min, FrontLine.MinSegmentMetres(blocks[i].Marker.Value));
                if (i + 1 < n && blocks[i + 1].Marker.HasValue) min = Math.Max(min, FrontLine.MinSegmentMetres(blocks[i + 1].Marker.Value));
                float length = Math.Max(blocks[i].Km * 1000f, min);
                if (i == 0 && hasRun) length = Math.Max(length, OpeningRunMinMetres);
                at[i + 1] = at[i] + length;
            }

            int count = 0;
            while (count < n && (count == 0 || at[count] < SpanMetres) && count < MaxKeyframes) count++;
            var frames = new TapeKeyframe[count];
            var markers = new List<TapeMarker>();
            var fronts = new List<TapeFront>();
            var segs = new List<TapeSegment>();
            int story = -1;
            float pendingLead = float.NaN;
            int pendingMarker = -1;
            for (int i = 0; i < count; i++)
            {
                SkyDef def = SkyLibrary.Get(blocks[i].Sky);
                frames[i] = new TapeKeyframe(i == 0 ? 0f : at[i], i == 0 ? 0f : width[i], skies[i], def.Genus.Genus, def.HasLow, blocks[i].Sky, blocks[i].Segment);
                if (blocks[i].Marker.HasValue) { markers.Add(new TapeMarker(blocks[i].Marker.Value, at[i], blocks[i].MarkerStrength, i)); pendingMarker = markers.Count - 1; }
                if (blocks[i].Lead && i > 0)
                {
                    pendingLead = at[i] - 0.5f * width[i];
                    story = segmentRows.Count > blocks[i].Segment ? segmentRows[blocks[i].Segment].Story : -1;
                    if (!blocks[i].Main) fronts.Add(new TapeFront(story, pendingLead, float.NaN, -1));
                }
                if (blocks[i].Main)
                {
                    float main = at[i] - 0.5f * width[i];
                    if (blocks[i].Lead && i > 0) fronts.Add(new TapeFront(story, main, main, pendingMarker));
                    else if (fronts.Count > 0 && float.IsNaN(fronts[fronts.Count - 1].SMain)) fronts[fronts.Count - 1] = new TapeFront(fronts[fronts.Count - 1].Story, fronts[fronts.Count - 1].SLead, main, -1);
                }
            }
            // The last round is cut at 3000 km: a lead whose main change never came is no front (Solve reads Fronts[0] only).
            fronts.RemoveAll(f => float.IsNaN(f.SMain));
            // A front's marker is the first marker on or after its s_main.
            for (int f = 0; f < fronts.Count; f++)
            {
                int m = -1;
                for (int k = 0; k < markers.Count && m < 0; k++) if (markers[k].S >= fronts[f].SMain) m = k;
                fronts[f] = new TapeFront(fronts[f].Story, fronts[f].SLead, fronts[f].SMain, m);
            }
            for (int r = 0; r < segmentRows.Count; r++)
            {
                int first = segmentRows[r].FirstBlock;
                if (first >= count) break;
                int last = r + 1 < segmentRows.Count ? Math.Min(count, segmentRows[r + 1].FirstBlock) : count;
                segs.Add(new TapeSegment(segmentRows[r].Label, segmentRows[r].Role, SkyLibrary.ClassOf(blocks[first].Sky), first == 0 ? 0f : at[first], last < n ? at[last] : SpanMetres,
                    segmentRows[r].Story, first));
            }
            if (segs.Count == 0) segs.Add(new TapeSegment("SKY", Role.None, SkyLibrary.ClassOf(blocks[0].Sky), 0f, SpanMetres, -1, 0));
            Keyframes = frames;
            Markers = markers.Count == 0 ? MarkerSet.Empty : new MarkerSet(markers.ToArray());
            Fronts = fronts.ToArray();
            Segments = segs.ToArray();
            OpeningEnd = hasRun && count > 1 ? at[1] : 0f;
        }
    }
}
