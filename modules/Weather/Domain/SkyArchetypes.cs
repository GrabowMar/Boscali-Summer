using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    internal enum LowGenus : byte
    {
        Humilis, Mediocris, Congestus, Cumulonimbus, Stratocumulus, ScLid, Stratus, Fractus, Nimbostratus,
    }

    /// <summary>Thin-layer archetypes of spec 4.3: altocumulus and altostratus (mid), cirrus, cirrocumulus and cirrostratus (high).</summary>
    internal enum LayerKind : byte
    {
        AcFloccus, AcUndulatus, AcStratiformis, AsTranslucidus, AsOpacus,
        CirrusFibratus, CirrusUncinus, CirrusSpissatus, Cirrocumulus, CirrostratusNebulosus, CirrostratusFibratus,
    }

    /// <summary>One row of the low-genus table (spec 4.2). Values are initial and are tuned on the bench.</summary>
    internal readonly struct GenusRow
    {
        public GenusRow(LowGenus genus, float lobeRung, float puffDepth, float baseSharp, float baseWobble, float dome,
            float billow, float anvil, float deckDepth, float deckSmooth, float bandMin, float bandMax, float baseVariation,
            int candidates = 0, float capacity = 0f, float radiusMin = 0f, float radiusMax = 0f, float aspectMax = 1f)
        {
            Genus = genus; LobeRung = lobeRung; PuffDepth = puffDepth; BaseSharp = baseSharp; BaseWobble = baseWobble;
            Dome = dome; Billow = billow; Anvil = anvil; DeckDepth = deckDepth; DeckSmooth = deckSmooth;
            BandMin = bandMin; BandMax = bandMax; BaseVariation = baseVariation;
            Candidates = candidates; Capacity = capacity; RadiusMin = radiusMin; RadiusMax = radiusMax; AspectMax = aspectMax;
        }

        public readonly LowGenus Genus;
        public readonly float LobeRung, PuffDepth, BaseSharp, BaseWobble, Dome, Billow, Anvil, DeckDepth, DeckSmooth;
        public readonly float BandMin, BandMax, BaseVariation;
        /// <summary>Cumulus lattice (slice 3): candidates per 12 km cell is 0..N, the group radius range in km, the aspect ceiling and the baked capacity cover.</summary>
        public readonly int Candidates;
        public readonly float Capacity, RadiusMin, RadiusMax, AspectMax;

        /// <summary>Cumulus genera heap and may sit under a higher layer; the stratiform ones may not (the separation rule).</summary>
        public bool Convective => Genus <= LowGenus.Cumulonimbus;
    }

    /// <summary>One thin-layer archetype row (spec 4.3).</summary>
    internal readonly struct LayerArchetype
    {
        public LayerArchetype(LayerKind kind, bool high, float baseM, float thick, float periodRung, float softness, float detail,
            float cells, float ripples, float streaks, float veil, float opacity, float aspect, float curl = 0f, float rippleWavelength = 0f)
        {
            Kind = kind; High = high; Base = baseM; Thick = thick; PeriodRung = periodRung; Softness = softness; Detail = detail;
            Cells = cells; Ripples = ripples; Streaks = streaks; Veil = veil; Opacity = opacity; Aspect = aspect; Curl = curl;
            RippleWavelength = rippleWavelength;
        }

        public readonly LayerKind Kind;
        public readonly bool High;
        public readonly float Base, Thick, PeriodRung, Softness, Detail, Cells, Ripples, Streaks, Veil, Opacity, Aspect, Curl, RippleWavelength;

        /// <summary>Genus band for the base: Ac 3000-6000, As 2500-5500, Ci / Cc / Cs 6500-11000 (spec 4.4).</summary>
        public float BandMin => High ? 6500f : Kind <= LayerKind.AcStratiformis ? 3000f : 2500f;
        public float BandMax => High ? 11000f : Kind <= LayerKind.AcStratiformis ? 6000f : 5500f;

        /// <summary>The layer's columns with the given cover and optional base. Strength is derived later (SkyDerived).</summary>
        public SkyLayer ToLayer(float cover, float baseOverride = 0f)
        {
            float ripplePeriod = RippleWavelength > 0f ? RippleWavelength : 0.85f * Ladder.PeriodMetres(PeriodRung);
            return new SkyLayer
            {
                Base = baseOverride > 0f ? baseOverride : Base, Thick = Thick, Cover = cover, PeriodRung = PeriodRung,
                Softness = Softness, Detail = Detail, Strength = 0f, Cells = Cells, Ripples = Ripples, Streaks = Streaks,
                Veil = Veil, Curl = Curl, Aspect = Aspect, RippleWavelength = ripplePeriod,
            };
        }
    }

    internal static class SkyArchetypes
    {
        private static readonly GenusRow[] Genera =
        {
            //          genus                    lobe puff  sharp wob  dome  billow anvil deck  smooth band lo/hi   var  N   cap   R lo/hi   aspect
            new GenusRow(LowGenus.Humilis,        1,  500f,  40f, 30f, 0.90f, 0.65f, 0.00f,    0f, 0.00f,  700f, 2500f, 150f, 13, 0.36f, 2.0f, 4.0f, 1.30f),
            new GenusRow(LowGenus.Mediocris,      2, 1300f,  50f, 50f, 1.00f, 0.75f, 0.00f,    0f, 0.00f,  800f, 2800f, 200f,  9, 0.50f, 3.0f, 6.0f, 1.30f),
            new GenusRow(LowGenus.Congestus,      3, 3500f,  60f, 70f, 1.25f, 0.80f, 0.00f,    0f, 0.00f,  800f, 2500f, 250f, 12, 0.64f, 3.5f, 6.0f, 1.45f),
            new GenusRow(LowGenus.Cumulonimbus,   5, 2000f,  65f, 80f, 1.35f, 0.75f, 0.90f,    0f, 0.00f,  600f, 2200f, 300f, 20, 0.65f, 3.0f, 5.3f, 1.00f),
            new GenusRow(LowGenus.Stratocumulus,  4,  850f, 110f, 70f, 0.50f, 0.45f, 0.00f,  800f, 0.25f,  500f, 2200f, 250f),
            new GenusRow(LowGenus.ScLid,          3,  600f, 140f, 50f, 0.35f, 0.35f, 0.00f,  650f, 0.45f,  500f, 2000f, 200f),
            new GenusRow(LowGenus.Stratus,        4,  500f, 170f, 25f, 0.10f, 0.10f, 0.00f,  500f, 0.95f,  300f,  900f, 150f),
            new GenusRow(LowGenus.Fractus,        1,  300f, 120f, 60f, 0.30f, 0.55f, 0.00f,  300f, 0.60f,  200f,  800f, 400f),
            new GenusRow(LowGenus.Nimbostratus,   4, 2400f, 210f, 80f, 0.20f, 0.20f, 0.15f, 2500f, 0.90f,  300f, 1500f, 300f),
        };

        private static readonly LayerArchetype[] Layers =
        {
            //                 kind                              high   base  thick rung soft  detail  cells ripples streaks veil  opacity aspect curl wave
            new LayerArchetype(LayerKind.AcFloccus,              false, 4200f,  500f, 4f, 0.30f, 0.70f, 1.0f, 0.0f, 0.0f, 0.0f, 0.60f, 1.0f),
            new LayerArchetype(LayerKind.AcUndulatus,            false, 4500f,  400f, 5f, 0.25f, 0.50f, 0.2f, 0.8f, 0.0f, 0.0f, 0.50f, 1.0f, 0f, 900f),
            new LayerArchetype(LayerKind.AcStratiformis,         false, 3800f,  600f, 4f, 0.35f, 0.60f, 0.7f, 0.0f, 0.0f, 0.3f, 0.70f, 1.0f),
            new LayerArchetype(LayerKind.AsTranslucidus,         false, 3800f, 1200f, 9f, 0.80f, 0.20f, 0.0f, 0.0f, 0.2f, 0.8f, 0.70f, 1.4f),
            new LayerArchetype(LayerKind.AsOpacus,               false, 3300f, 1800f, 10f, 0.90f, 0.10f, 0.0f, 0.0f, 0.0f, 1.0f, 1.00f, 1.0f),
            new LayerArchetype(LayerKind.CirrusFibratus,         true,  8800f,  500f, 10f, 0.40f, 0.30f, 0.0f, 0.0f, 1.0f, 0.0f, 0.15f, 5.0f),
            new LayerArchetype(LayerKind.CirrusUncinus,          true,  8700f,  500f, 10f, 0.40f, 0.30f, 0.0f, 0.0f, 1.0f, 0.0f, 0.15f, 4.0f, 1f),
            new LayerArchetype(LayerKind.CirrusSpissatus,        true,  8600f,  700f, 9f, 0.50f, 0.40f, 0.4f, 0.0f, 0.6f, 0.0f, 0.20f, 3.0f),
            new LayerArchetype(LayerKind.Cirrocumulus,           true,  7500f,  400f, 2f, 0.20f, 0.80f, 1.00f, 0.00f, 0.0f, 0.0f, 0.15f, 1.0f),
            new LayerArchetype(LayerKind.CirrostratusNebulosus,  true,  8500f,  900f, 10f, 1.00f, 0.05f, 0.0f, 0.0f, 0.0f, 1.0f, 0.25f, 1.0f),
            new LayerArchetype(LayerKind.CirrostratusFibratus,   true,  8700f,  800f, 10f, 0.60f, 0.25f, 0.0f, 0.0f, 0.5f, 0.5f, 0.30f, 3.0f),
        };

        public static GenusRow Genus(LowGenus genus) => Genera[(int)genus];
        public static LayerArchetype Layer(LayerKind kind) => Layers[(int)kind];
        public static int GenusCount => Genera.Length;
        public static int LayerCount => Layers.Length;

        /// <summary>The low-genus columns of a sky at the given base. Cover columns are the caller's.</summary>
        public static void ApplyGenus(ref SkyParams sky, in GenusRow g, float baseM)
        {
            sky.CloudBase = baseM;
            sky.LobeRung = g.LobeRung; sky.PuffDepth = g.PuffDepth; sky.BaseSharp = g.BaseSharp; sky.BaseWobble = g.BaseWobble;
            sky.Dome = g.Dome; sky.Billow = g.Billow; sky.Anvil = g.Anvil; sky.DeckDepth = g.DeckDepth; sky.DeckSmooth = g.DeckSmooth;
            sky.BaseVariation = g.BaseVariation; sky.CapacityCover = g.Capacity;
        }

        /// <summary>Top of the low layer: base + max(puff depth, deck depth) + half the base wobble (spec 7.3 DeckTop).</summary>
        public static float DeckTop(in SkyParams sky) =>
            sky.CloudBase + Math.Max(sky.PuffDepth, sky.DeckDepth) + sky.BaseWobble * 0.5f;
    }
}
