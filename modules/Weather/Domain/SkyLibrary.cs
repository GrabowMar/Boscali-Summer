using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>Where a sky may sit in a story (spec 5.1). The stories arrive in a later slice; the roles are part of each sky's definition.</summary>
    [Flags]
    internal enum Role : ushort
    {
        None = 0, Ridge = 1, HighPress = 2, PreWarm = 4, WarmBody = 8, WarmSector = 16, ColdLine = 32,
        Clearing = 64, PostFrontal = 128, Build = 256, Cell = 512, Decay = 1024, Marine = 2048,
    }

    /// <summary>The jitter allowance of one sky layer group (spec 4.1): the share of its own base it may move, the absolute cover it may gain or lose, and the ladder rungs it may move.</summary>
    internal readonly struct JitterRow
    {
        public JitterRow(float baseFrac, float cover, int rungs) { BaseFrac = baseFrac; Cover = cover; Rungs = rungs; }
        public readonly float BaseFrac, Cover;
        public readonly int Rungs;

        public static readonly JitterRow Cumulus = new JitterRow(0.20f, 0.08f, 1);
        public static readonly JitterRow Stratocumulus = new JitterRow(0.12f, 0.05f, 1);
        public static readonly JitterRow Stratus = new JitterRow(0.08f, 0.02f, 0);
        public static readonly JitterRow ThinLayers = new JitterRow(0.05f, 0.08f, 1);
    }

    /// <summary>One authored sky: its nominal columns (complete and derived) plus what the rules and the jitter need to know about how it was built.</summary>
    internal sealed class SkyDef
    {
        public int Index;
        public string Name;
        public WeatherRegimeType Class;
        public Role Roles;
        public float OpeningWeight = 1f;
        /// <summary>Patchy: local cells over mostly open sky. Never in an opening pool.</summary>
        public bool Patchy;
        public bool HasLow, HasMid, HasHigh, Embedded;
        public GenusRow Genus;
        public LayerArchetype MidArchetype, HighArchetype;
        public JitterRow LowJitter;
        public SkyParams Nominal;
        public int Number => Index + 1;
    }

    /// <summary>The fluent sky builder of spec 4. A sky is archetypes plus overrides; there is no hand-written METAR string (8.4 derives it).</summary>
    internal sealed class SkyBuilder
    {
        private readonly string name;
        private readonly WeatherRegimeType cls;
        private LowGenus? genus;
        private float lowBase, cumulus, sheet, midBaseOverride, midCover, highCover, streets = 0f, hazeScale = 1f;
        private LayerKind? mid, high;
        private float rain, rainBase = -1f, convective, severity = -1f, weight = 1f;
        private bool patchy, embedded;
        private Role roles;

        internal SkyBuilder(string name, WeatherRegimeType cls) { this.name = name; this.cls = cls; }

        public SkyBuilder Low(LowGenus g, float baseM, float cumulus = 0f, float sheet = 0f)
        { genus = g; lowBase = baseM; this.cumulus = cumulus; this.sheet = sheet; return this; }
        public SkyBuilder Mid(LayerKind kind, float cover, float baseM = 0f) { mid = kind; midCover = cover; midBaseOverride = baseM; return this; }
        public SkyBuilder High(LayerKind kind, float cover) { high = kind; highCover = cover; return this; }
        public SkyBuilder Rain(float mmPerHour, float rainBaseM) { rain = mmPerHour; rainBase = rainBaseM; return this; }
        public SkyBuilder Cells(float convective, float severity = -1f) { this.convective = convective; this.severity = severity; return this; }
        public SkyBuilder Streets(float value) { streets = value; return this; }
        public SkyBuilder HazeScale(float value) { hazeScale = value; return this; }
        public SkyBuilder Weight(float value) { weight = value; return this; }
        public SkyBuilder Patchy() { patchy = true; return this; }
        public SkyBuilder Embedded() { embedded = true; return this; }
        public SkyBuilder Roles(params Role[] values) { foreach (Role r in values) roles |= r; return this; }

        internal SkyDef Build(int index)
        {
            SkyParams row = StateTable.Get(cls);
            var sky = new SkyParams
            {
                WindFactor = row.WindFactor, Turbulence = row.Turbulence, Haze = row.Haze / hazeScale, Qnh = row.Qnh,
                Temperature = row.Temperature, Severity = severity >= 0f ? severity : row.Severity,
                OpacityMid = 1f, OpacityHigh = 1f, Streets = streets,
                Convective = convective, StratRain = rain, SheetCover = sheet, CumulusCover = cumulus,
            };
            var def = new SkyDef { Index = index, Name = name, Class = cls, Roles = roles, OpeningWeight = weight, Patchy = patchy, Embedded = embedded };
            if (genus.HasValue)
            {
                def.HasLow = true;
                def.Genus = SkyArchetypes.Genus(genus.Value);
                SkyArchetypes.ApplyGenus(ref sky, def.Genus, lowBase);
                if (streets >= CumulusLattice.StreetThreshold)
                {
                    if (def.Genus.Genus != LowGenus.Humilis) throw new InvalidOperationException("street zones are baked for Humilis only: " + name);
                    sky.CapacityCover = SkyArchetypes.StreetCapacityHumilis;
                }
                def.LowJitter = DefaultJitter(def.Genus.Genus);
            }
            else
            {
                // No low layer: the Clear state's faint humilis at 2400 m with no cover of its own, so the legacy patch noise draws what a Clear sky always drew.
                SkyArchetypes.ApplyGenus(ref sky, SkyArchetypes.Genus(LowGenus.Humilis), 2400f);
                def.Genus = SkyArchetypes.Genus(LowGenus.Humilis);
            }
            if (mid.HasValue)
            {
                def.HasMid = true;
                def.MidArchetype = SkyArchetypes.Layer(mid.Value);
                sky.Mid = def.MidArchetype.ToLayer(midCover, midBaseOverride);
                sky.OpacityMid = def.MidArchetype.Opacity;
            }
            if (high.HasValue)
            {
                def.HasHigh = true;
                def.HighArchetype = SkyArchetypes.Layer(high.Value);
                sky.High = def.HighArchetype.ToLayer(highCover);
                sky.OpacityHigh = def.HighArchetype.Opacity;
            }
            if (embedded) sky.Mid.Base = SkyArchetypes.DeckTop(sky) - 300f;
            sky.RainBase = rainBase >= 0f ? rainBase : (rain > 0.1f || convective > 0f) && def.HasLow ? sky.CloudBase : 0f;
            SkyDerived.Apply(ref sky);
            def.Nominal = sky;
            return def;
        }

        private static JitterRow DefaultJitter(LowGenus g) => g switch
        {
            LowGenus.Stratocumulus or LowGenus.ScLid or LowGenus.Fractus => JitterRow.Stratocumulus,
            LowGenus.Stratus or LowGenus.Nimbostratus => JitterRow.Stratus,
            _ => JitterRow.Cumulus,
        };
    }

    /// <summary>
    /// The 47 skies of spec 4.4 (<see cref="Count"/>), authored as archetypes with overrides, plus the jittered instances
    /// a tape or a pin makes of them. The index is the <see cref="WeatherKey.SkyPin"/> value (the sky's number minus one).
    /// </summary>
    internal static class SkyLibrary
    {
        public const int Count = 47;

        private static readonly SkyDef[] Defs = BuildAll();

        public static SkyDef Get(int index) => Defs[index];
        public static WeatherRegimeType ClassOf(int index) => Defs[index].Class;

        private static SkyBuilder Sky(string name, WeatherRegimeType cls) => new SkyBuilder(name, cls);

        private static SkyDef[] BuildAll()
        {
            const WeatherRegimeType Clear = WeatherRegimeType.Clear, Fair = WeatherRegimeType.Fair, Scattered = WeatherRegimeType.Scattered,
                Broken = WeatherRegimeType.Broken, Overcast = WeatherRegimeType.Overcast, Rain = WeatherRegimeType.RainSquall, Storm = WeatherRegimeType.Storm;
            const LayerKind CiFib = LayerKind.CirrusFibratus, CiUnc = LayerKind.CirrusUncinus, CiSpi = LayerKind.CirrusSpissatus,
                Cc = LayerKind.Cirrocumulus, CsNeb = LayerKind.CirrostratusNebulosus, CsFib = LayerKind.CirrostratusFibratus,
                AcFloc = LayerKind.AcFloccus, AcUnd = LayerKind.AcUndulatus, AcStrat = LayerKind.AcStratiformis,
                AsTrans = LayerKind.AsTranslucidus, AsOp = LayerKind.AsOpacus;
            const LowGenus Hum = LowGenus.Humilis, Med = LowGenus.Mediocris, Cng = LowGenus.Congestus, Cb = LowGenus.Cumulonimbus,
                Sc = LowGenus.Stratocumulus, Scl = LowGenus.ScLid, Str = LowGenus.Stratus, Frc = LowGenus.Fractus, Ns = LowGenus.Nimbostratus;
            const Role Ridge = Role.Ridge, HighPress = Role.HighPress, PreWarm = Role.PreWarm, WarmBody = Role.WarmBody,
                WarmSector = Role.WarmSector, ColdLine = Role.ColdLine, Clearing = Role.Clearing, PostFrontal = Role.PostFrontal,
                Build = Role.Build, Cell = Role.Cell, Decay = Role.Decay, Marine = Role.Marine;

            SkyBuilder[] skies =
            {
                // Clear (7)
                Sky("Blue CAVOK", Clear).High(CiFib, 0.05f).Roles(HighPress).Weight(0.1f),
                Sky("Cirrus streaks", Clear).High(CiFib, 0.30f).Roles(Ridge, PreWarm, HighPress),
                Sky("Cirrus hooks and clumps", Clear).High(CiUnc, 0.50f).Roles(PreWarm, HighPress),
                Sky("Cirrostratus veil, milky sun", Clear).High(CsNeb, 0.45f).Roles(PreWarm),
                Sky("Mackerel sky", Clear).High(Cc, 0.80f).Roles(PreWarm, HighPress),
                Sky("Summer haze", Clear).High(CiFib, 0.10f).HazeScale(2f).Weight(0.1f),
                Sky("High altocumulus patches", Clear).Mid(AcFloc, 0.18f).High(CiFib, 0.10f).Roles(Ridge),
                // Fair (7)
                Sky("Fair-weather cumulus", Fair).Low(Hum, 1500f, cumulus: 0.15f).High(CiFib, 0.12f).Roles(Ridge, PostFrontal, HighPress),
                Sky("Cumulus and cirrus", Fair).Low(Hum, 1700f, cumulus: 0.12f).High(CiSpi, 0.30f).Roles(Ridge),
                Sky("Cloud streets", Fair).Low(Hum, 1400f, cumulus: 0.22f).Streets(0.8f).Roles(PostFrontal),
                Sky("Sparse popcorn", Fair).Low(Hum, 2000f, cumulus: 0.14f).Roles(Ridge, Build),
                Sky("Altocumulus swarm", Fair).Low(Hum, 1800f, cumulus: 0.08f).Mid(AcFloc, 0.30f).Roles(Ridge, Build),
                Sky("Humilis under cirrostratus", Fair).Low(Hum, 1500f, cumulus: 0.12f).High(CsNeb, 0.55f).Roles(PreWarm),
                Sky("Post-frontal clear-air cumulus", Fair).Low(Med, 2400f, cumulus: 0.18f).Roles(PostFrontal),
                // Scattered (7)
                Sky("Cumulus mediocris", Scattered).Low(Med, 1500f, cumulus: 0.35f).High(CiFib, 0.10f).Roles(Build),
                Sky("Towering cumulus with virga", Scattered).Low(Cng, 1400f, cumulus: 0.28f).Cells(0.15f).Roles(Build).Patchy(),
                Sky("Isolated cumulonimbus", Scattered).Low(Cb, 1300f, cumulus: 0.20f).High(CiSpi, 0.15f).Cells(0.25f).Roles(Cell).Patchy(),
                Sky("Altocumulus ripples over cumulus", Scattered).Low(Hum, 1700f, cumulus: 0.25f).Mid(AcUnd, 0.45f).Roles(Build, Clearing),
                Sky("Stratocumulus patches", Scattered).Low(Sc, 1300f, sheet: 0.30f).Roles(Clearing, Marine),
                Sky("Two decks", Scattered).Low(Med, 1500f, cumulus: 0.22f).Mid(AcFloc, 0.40f).High(CiFib, 0.10f).Roles(Build, Decay),
                Sky("Post-frontal showers", Scattered).Low(Cng, 1500f, cumulus: 0.30f).Cells(0.20f).Roles(PostFrontal).Patchy(),
                // Broken (7)
                Sky("Sc lid with gaps", Broken).Low(Scl, 1100f, sheet: 0.55f).Roles(WarmSector, Marine),
                Sky("Altostratus with holes", Broken).Low(Hum, 1600f, cumulus: 0.10f).Mid(AsTrans, 0.65f).Roles(PreWarm),
                Sky("Three decks", Broken).Low(Sc, 1300f, sheet: 0.45f).Mid(AsTrans, 0.50f).High(CiSpi, 0.40f).Roles(PreWarm, Decay),
                Sky("Cumulus under altocumulus deck", Broken).Low(Med, 1400f, cumulus: 0.30f).Mid(AcStrat, 0.70f).Roles(Build, Decay),
                Sky("Warm-sector stratocumulus", Broken).Low(Sc, 900f, sheet: 0.60f).High(CsFib, 0.30f).Roles(WarmSector),
                Sky("Lowering altostratus", Broken).Low(Frc, 700f, sheet: 0.15f).Mid(AsTrans, 0.80f, 3200f).High(CsNeb, 0.50f).Roles(PreWarm),
                Sky("Broken congestus line", Broken).Low(Cng, 1300f, cumulus: 0.50f).High(CiSpi, 0.20f).Cells(0.30f).Roles(Cell),
                // Overcast (6)
                Sky("Low stratus", Overcast).Low(Str, 450f, sheet: 0.95f).Roles(WarmSector, Marine),
                Sky("Grey sheet, altostratus above", Overcast).Low(Scl, 900f, sheet: 0.85f).Mid(AsOp, 0.90f).Roles(WarmSector),
                Sky("Double deck", Overcast).Low(Sc, 1000f, sheet: 0.80f).Mid(AsOp, 0.80f).Roles(WarmSector, Decay),
                Sky("Sea of cloud, clear above", Overcast).Low(Scl, 800f, sheet: 1.0f).Roles(Marine),
                Sky("Altostratus opacus", Overcast).Mid(AsOp, 1.0f).High(CsNeb, 0.70f).Roles(PreWarm),
                Sky("Pre-frontal nimbostratus, no rain yet", Overcast).Low(Ns, 1400f, sheet: 0.95f).Mid(AsOp, 1.0f).Embedded().Roles(WarmBody),
                // Rain (7)
                Sky("Steady rain", Rain).Low(Ns, 800f, sheet: 1.0f).Mid(AsOp, 1.0f).Embedded().Rain(4f, 800f).Roles(WarmBody),
                Sky("Drizzle under stratus", Rain).Low(Str, 350f, sheet: 1.0f).Rain(0.8f, 350f).Roles(WarmSector, Marine),
                Sky("Squall line", Rain).Low(Cb, 900f, cumulus: 0.50f, sheet: 0.60f).Cells(0.60f).Roles(ColdLine),
                Sky("Embedded showers in altostratus", Rain).Mid(AsOp, 1.0f).Rain(2.5f, 3300f).Roles(WarmBody),
                Sky("Warm-sector drizzle", Rain).Low(Scl, 600f, sheet: 1.0f).Rain(0.6f, 600f).Roles(WarmSector),
                Sky("Heavy steady rain", Rain).Low(Ns, 1000f, sheet: 1.0f).Mid(AsOp, 1.0f).Embedded().Rain(5f, 1000f).Roles(WarmBody),
                Sky("Showers from a congestus deck", Rain).Low(Cng, 1200f, cumulus: 0.40f, sheet: 0.65f).Cells(0.45f).Roles(Cell, Decay),
                // Storm (6)
                Sky("Multicell cluster", Storm).Low(Cb, 1000f, cumulus: 0.30f, sheet: 0.60f).Mid(AcFloc, 0.30f).High(CiSpi, 0.40f).Cells(0.7f, 0.6f).Roles(Cell),
                Sky("Embedded storms in nimbostratus", Storm).Low(Ns, 700f, sheet: 1.0f).Mid(AsOp, 1.0f).Embedded().Rain(6f, 700f).Cells(0.6f, 0.7f).Roles(WarmBody),
                Sky("Supercell day", Storm).Low(Cb, 1100f, sheet: 0.50f).High(CiSpi, 0.60f).Cells(0.8f, 1.0f).Roles(Cell),
                Sky("Cold-front storm line", Storm).Low(Cb, 900f, sheet: 0.80f).Mid(AsOp, 0.80f).High(CiSpi, 0.50f).Cells(0.9f, 0.9f).Roles(ColdLine),
                Sky("Evening storms with anvils", Storm).Low(Cb, 1200f, cumulus: 0.30f, sheet: 0.50f).Mid(AcStrat, 0.40f).High(CiSpi, 0.70f).Cells(0.7f, 0.7f).Roles(Decay),
                Sky("Thunderstorm under a cloud lid", Storm).Low(Cb, 1000f, sheet: 0.90f).High(CiSpi, 0.50f).Cells(0.8f, 0.6f).Roles(Cell),
            };
            if (skies.Length != Count) throw new InvalidOperationException("the sky library holds " + skies.Length + " skies, expected " + Count);
            var defs = new SkyDef[skies.Length];
            for (int i = 0; i < skies.Length; i++) defs[i] = skies[i].Build(i);
            return defs;
        }

        /// <summary>The skies of a class that may open a mission: every non-patchy one (spec 4.4).</summary>
        public static int[] OpeningPool(WeatherRegimeType cls)
        {
            var pool = new System.Collections.Generic.List<int>();
            for (int i = 0; i < Count; i++)
                if (Defs[i].Class == cls && !Defs[i].Patchy) pool.Add(i);
            return pool.ToArray();
        }

        /// <summary>A hashed pick from a class's opening pool; Blue CAVOK and Summer haze weigh 0.1, so they are rare.</summary>
        public static int PickOpening(WeatherRegimeType cls, uint seed)
        {
            int[] pool = OpeningPool(cls);
            float total = 0f;
            for (int i = 0; i < pool.Length; i++) total += Defs[pool[i]].OpeningWeight;
            float roll = WeatherMath.Hash01(seed, 0x0BE1, (int)cls) * total;
            for (int i = 0; i < pool.Length; i++)
            {
                roll -= Defs[pool[i]].OpeningWeight;
                if (roll < 0f) return pool[i];
            }
            return pool[pool.Length - 1];
        }

        // ---- instances ---------------------------------------------------------------------------

        /// <summary>
        /// The sky as a tape keyframe or a pin shows it: the nominal columns moved by their jitter row, the mission's
        /// <paramref name="openingBase"/> (0 = none) replacing the low base inside the genus band, and the separation rule
        /// re-applied. <paramref name="jitterScale"/> scales the base and cover amplitudes (0 = the nominal sky).
        /// </summary>
        public static SkyParams Instance(int index, uint seed, float openingBase = 0f, float jitterScale = 1f)
        {
            SkyDef def = Defs[index];
            SkyParams sky = def.Nominal;
            Jitter(def, ref sky, seed, jitterScale, 0);
            return Finish(def, sky, openingBase);
        }

        /// <summary>
        /// The instance an opening run uses (spec 4.4): the jitter amplitude is scaled by the largest of 1, 0.5, 0.25, 0 that
        /// keeps <see cref="SkyMath.ClassConditions"/> inside the sky's exact class band, so the mission's class holds.
        /// </summary>
        public static SkyParams OpeningInstance(int index, uint seed, float openingBase, out float scale)
        {
            SkyDef def = Defs[index];
            foreach (float s in new[] { 1f, 0.5f, 0.25f })
            {
                SkyParams candidate = Instance(index, seed, openingBase, s);
                if (SkyMath.ClassOf(candidate) == def.Class) { scale = s; return candidate; }
            }
            scale = 0f;
            return Instance(index, seed, openingBase, 0f);
        }

        private static float Signed(uint seed, int index, int slot) => 2f * WeatherMath.Hash01(seed, 0x5C1 + index, slot) - 1f;

        private static int RungStep(uint seed, int index, int slot, int rungs)
        {
            float u = WeatherMath.Hash01(seed, 0x5C1 + index, slot);
            return rungs <= 0 ? 0 : u < 0.25f ? -rungs : u > 0.75f ? rungs : 0;
        }

        /// <summary>direction 0 draws hashed amounts; +1 / -1 move every present cover by exactly its amplitude and touch nothing else.</summary>
        private static void Jitter(SkyDef def, ref SkyParams sky, uint seed, float scale, int direction)
        {
            if (scale <= 0f && direction == 0) return;
            int i = def.Index;
            JitterRow low = def.LowJitter, thin = JitterRow.ThinLayers;
            float Amount(float amplitude, int slot) => direction != 0 ? direction * amplitude : Signed(seed, i, slot) * amplitude * scale;
            if (def.HasLow)
            {
                if (direction == 0)
                {
                    float moved = sky.CloudBase * (1f + Signed(seed, i, 0) * low.BaseFrac * scale);
                    sky.CloudBase = Scalar.Clamp(moved, def.Genus.BandMin, def.Genus.BandMax);
                    sky.LobeRung = Scalar.Clamp(sky.LobeRung + RungStep(seed, i, 5, low.Rungs), 0f, Ladder.LobeRungs - 1);
                }
                if (sky.CumulusCover > 0f) sky.CumulusCover = Scalar.Clamp01(sky.CumulusCover + Amount(low.Cover, 1));
                if (sky.SheetCover > 0f) sky.SheetCover = Scalar.Clamp01(sky.SheetCover + Amount(low.Cover, 2));
            }
            if (def.HasMid)
            {
                sky.Mid.Cover = Scalar.Clamp01(sky.Mid.Cover + Amount(thin.Cover, 3));
                if (direction == 0)
                {
                    sky.Mid.Base = Scalar.Clamp(sky.Mid.Base * (1f + Signed(seed, i, 6) * thin.BaseFrac * scale),
                        def.MidArchetype.BandMin, def.MidArchetype.BandMax);
                    sky.Mid.PeriodRung = Scalar.Clamp(sky.Mid.PeriodRung + RungStep(seed, i, 8, thin.Rungs), 0f, Ladder.PeriodRungs - 1);
                }
            }
            if (def.HasHigh)
            {
                sky.High.Cover = Scalar.Clamp01(sky.High.Cover + Amount(thin.Cover, 4));
                if (direction == 0)
                {
                    sky.High.Base = Scalar.Clamp(sky.High.Base * (1f + Signed(seed, i, 7) * thin.BaseFrac * scale),
                        def.HighArchetype.BandMin, def.HighArchetype.BandMax);
                    sky.High.PeriodRung = Scalar.Clamp(sky.High.PeriodRung + RungStep(seed, i, 9, thin.Rungs), 0f, Ladder.PeriodRungs - 1);
                }
            }
        }

        private static SkyParams Finish(SkyDef def, SkyParams sky, float openingBase)
        {
            if (def.HasLow && openingBase > 0f)
                sky.CloudBase = Scalar.Clamp(openingBase, def.Genus.BandMin, def.Genus.BandMax);
            if (def.HasLow)
            {
                float top = SkyArchetypes.DeckTop(sky);
                if (def.Embedded) sky.Mid.Base = Scalar.Clamp(top - 300f, def.MidArchetype.BandMin, def.MidArchetype.BandMax);
                else if (def.HasMid && !def.Genus.Convective)
                    sky.Mid.Base = Math.Min(def.MidArchetype.BandMax, Math.Max(sky.Mid.Base, top + 300f));
            }
            SkyDerived.Apply(ref sky);
            return sky;
        }
    }
}
