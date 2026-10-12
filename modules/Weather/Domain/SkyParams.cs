using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The 14 columns of one thin layer, mid (altocumulus / altostratus) or high (cirrus / cirrocumulus / cirrostratus)
    /// (spec 4.1 "Layer x2", 4.3). All floats so a keyframe blend is a plain lerp. <see cref="Cells"/>,
    /// <see cref="Ripples"/>, <see cref="Streaks"/> and <see cref="Veil"/> are continuous weights that the shader
    /// normalises, so a fade between two layer kinds never switches category.
    /// </summary>
    internal struct SkyLayer
    {
        /// <summary>A layer with less cover than this is absent: it draws nothing and inherits its neighbour's geometry in a blend.</summary>
        public const float PresentCover = 0.02f;

        public float Base, Thick, Cover;
        /// <summary>Feature period on the period ladder (Ladder.PeriodMetres); fractional only inside a ramp.</summary>
        public float PeriodRung;
        public float Softness, Detail, Strength;
        public float Cells, Ripples, Streaks, Veil;
        public float Curl, Aspect, RippleWavelength;

        public float Top => Base + Thick;
        public bool Present => Cover >= PresentCover;

        /// <summary>The old single middle layer (MidCover / MidSheet) as the new columns, for the seven designated class skies.</summary>
        public static SkyLayer LegacyMid(float cover, float sheet)
        {
            float s = Scalar.Clamp01(sheet);
            float veil = WeatherMath.Smoothstep(0.4f, 1f, s), ripple = 0.7f * (1f - s);
            return new SkyLayer
            {
                Base = WeatherMath.Lerp(4200f, 3300f, s), Thick = WeatherMath.Lerp(400f, 1700f, s), Cover = cover,
                PeriodRung = 7f, Softness = 0.5f, Detail = 0.7f, Strength = 0.42f,
                Cells = 0f, Ripples = (1f - veil) * ripple, Streaks = (1f - veil) * (1f - ripple), Veil = veil,
                Curl = 0f, Aspect = 1.4f, RippleWavelength = 0.85f * Ladder.PeriodMetres(7f),
            };
        }

        /// <summary>The old single high layer (HighCover / HighVeil) as the new columns.</summary>
        public static SkyLayer LegacyHigh(float cover, float veil)
        {
            float v = Scalar.Clamp01(veil);
            float flat = WeatherMath.Smoothstep(0.6f, 1f, v), ripple = 4f * v * (1f - v);
            return new SkyLayer
            {
                Base = 8600f, Thick = WeatherMath.Lerp(500f, 900f, v), Cover = cover,
                PeriodRung = 9f, Softness = 0.5f, Detail = 0.7f, Strength = 0.12f,
                Cells = 0f, Ripples = (1f - flat) * ripple, Streaks = (1f - flat) * (1f - ripple), Veil = flat,
                Curl = 0f, Aspect = WeatherMath.Lerp(7f, 1f, v), RippleWavelength = 0.85f * Ladder.PeriodMetres(9f),
            };
        }
    }

    /// <summary>
    /// One sky: the low-cloud genus, two thin layers and the CPU-only weather columns, all floats so a keyframe blend
    /// is a plain lerp (spec 4.1). <see cref="SkyColumns"/> flattens it to the 60-float row the baked tape stores and
    /// the shader reads. A sky has exactly one low genus, one base and one deck depth.
    /// </summary>
    internal struct SkyParams
    {
        // Low genus (shader rows 0-2).
        public float LobeRung, PuffDepth, BaseSharp, BaseWobble, Dome, Billow, Anvil, DeckDepth, DeckSmooth, Streets;
        /// <summary>Baked (SkyDerived): the horizon deck's cover and depth.</summary>
        public float HorizonCover, HorizonDepth;

        // Thin layers (shader rows 3-6 and 7-10).
        public SkyLayer Mid, High;

        // CPU only.
        public float CloudBase, SheetCover, CumulusCover, CapacityCover;
        /// <summary>Baked per tape texel, never authored: the activation level of a cumulus group centred here (spec 4.1, 6).</summary>
        public float CumulusLevel;
        public float Convective, Severity, BaseVariation, StratRain, RainBase;
        public float WindFactor, Turbulence, Haze, Qnh, Temperature, OpacityMid, OpacityHigh;

        // INTERIM(slice 4): the legacy frontal shield and sky split still run on these until the front markers replace them.
        public float Frontal, Split;

        public SkyLayer Layer(int index) => index == 0 ? Mid : High;

        /// <summary>Lerp of every column. A layer that is absent on one side takes the other side's base, thickness, period rung, aspect and
        /// ripple wavelength first, so a layer fading in never sweeps through altitudes or scales; aspect and ripple wavelength are held at
        /// the nearer side unless <paramref name="holdScales"/> is false.</summary>
        public static SkyParams Lerp(in SkyParams a, in SkyParams b, float t, bool holdScales = true)
        {
            SkyParams x = a, y = b;
            Inherit(ref x.Mid, in b.Mid); Inherit(ref y.Mid, in a.Mid);
            Inherit(ref x.High, in b.High); Inherit(ref y.High, in a.High);
            SkyParams result = Blend(x, y, t);
            if (holdScales)
            {
                // Aspect and RippleWavelength divide the noise coordinate, so lerping them would slide the pattern through scales across a
                // ramp (spec 6: noise is never sampled at a scale that varies in space or time). Hold them at the nearer keyframe's value;
                // the texel lerp of the baked tape softens the step over one texel (732 m). INTERIM(slice 4): the legacy class fade
                // (StateTable.At) passes holdScales = false and keeps today's smooth change of the high layer's streak length.
                Hold(ref result.Mid, in x.Mid, in y.Mid, t);
                Hold(ref result.High, in x.High, in y.High, t);
            }
            return result;
        }

        private static void Hold(ref SkyLayer layer, in SkyLayer from, in SkyLayer to, float t)
        {
            layer.Aspect = t < 0.5f ? from.Aspect : to.Aspect;
            layer.RippleWavelength = t < 0.5f ? from.RippleWavelength : to.RippleWavelength;
        }

        /// <summary>Plain lerp of all 60 columns (CumulusLevel included) with no layer inheritance and no hold: what the tape texel lerp does on the CPU and the GPU.</summary>
        public static SkyParams Blend(in SkyParams a, in SkyParams b, float t)
        {
            float[] x = lerpA ?? (lerpA = new float[SkyColumns.Count]);
            float[] y = lerpB ?? (lerpB = new float[SkyColumns.Count]);
            SkyColumns.Write(a, x, 0);
            SkyColumns.Write(b, y, 0);
            for (int i = 0; i < x.Length; i++) x[i] += (y[i] - x[i]) * t;
            return SkyColumns.Read(x, 0);
        }

        [ThreadStatic] private static float[] lerpA, lerpB;

        private static void Inherit(ref SkyLayer layer, in SkyLayer donor)
        {
            if (layer.Present || !donor.Present) return;
            layer.Base = donor.Base;
            layer.Thick = donor.Thick;
            layer.PeriodRung = donor.PeriodRung;
            layer.Aspect = donor.Aspect;
            layer.RippleWavelength = donor.RippleWavelength;
        }
    }

    /// <summary>The column table of <see cref="SkyParams"/>: array order, names and where each column sits in the shader texture.</summary>
    internal static class SkyColumns
    {
        public const int Count = 59;

        public static readonly string[] Names =
        {
            "LobeRung", "PuffDepth", "BaseSharp", "BaseWobble", "Dome", "Billow",
            "Anvil", "DeckDepth", "DeckSmooth", "Streets", "HorizonCover", "HorizonDepth",
            "MidBase", "MidThick", "MidCover", "MidPeriodRung", "MidSoftness", "MidDetail",
            "MidStrength", "MidCells", "MidRipples", "MidStreaks", "MidVeil", "MidCurl",
            "MidAspect", "MidRippleWavelength", "HighBase", "HighThick", "HighCover", "HighPeriodRung",
            "HighSoftness", "HighDetail", "HighStrength", "HighCells", "HighRipples", "HighStreaks",
            "HighVeil", "HighCurl", "HighAspect", "HighRippleWavelength", "CloudBase", "SheetCover",
            "CumulusCover", "CapacityCover", "CumulusLevel", "Convective", "Severity", "BaseVariation",
            "StratRain", "RainBase", "WindFactor", "Turbulence", "Haze", "Qnh",
            "Temperature", "OpacityMid", "OpacityHigh", "Frontal", "Split",
        };

        /// <summary>Shader texture row of each column, or -1 for CPU-only columns (spec 7.2: rows 0-2 low genus, 3-6 mid, 7-10 high).</summary>
        public static readonly sbyte[] GpuRow =
        {
            0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
            3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5,
            6, 6, 7, 7, 7, 7, 8, 8, 8, 8, 9, 9,
            9, 9, 10, 10, -1, -1, -1, -1, -1, -1, -1, -1,
            -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        };

        public static readonly sbyte[] GpuChannel =
        {
            0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3,
            0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3,
            0, 1, 0, 1, 2, 3, 0, 1, 2, 3, 0, 1,
            2, 3, 0, 1, -1, -1, -1, -1, -1, -1, -1, -1,
            -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
        };

        public static void Write(in SkyParams s, float[] to, int at)
        {
            to[at + 0] = s.LobeRung;
            to[at + 1] = s.PuffDepth;
            to[at + 2] = s.BaseSharp;
            to[at + 3] = s.BaseWobble;
            to[at + 4] = s.Dome;
            to[at + 5] = s.Billow;
            to[at + 6] = s.Anvil;
            to[at + 7] = s.DeckDepth;
            to[at + 8] = s.DeckSmooth;
            to[at + 9] = s.Streets;
            to[at + 10] = s.HorizonCover;
            to[at + 11] = s.HorizonDepth;
            to[at + 12] = s.Mid.Base;
            to[at + 13] = s.Mid.Thick;
            to[at + 14] = s.Mid.Cover;
            to[at + 15] = s.Mid.PeriodRung;
            to[at + 16] = s.Mid.Softness;
            to[at + 17] = s.Mid.Detail;
            to[at + 18] = s.Mid.Strength;
            to[at + 19] = s.Mid.Cells;
            to[at + 20] = s.Mid.Ripples;
            to[at + 21] = s.Mid.Streaks;
            to[at + 22] = s.Mid.Veil;
            to[at + 23] = s.Mid.Curl;
            to[at + 24] = s.Mid.Aspect;
            to[at + 25] = s.Mid.RippleWavelength;
            to[at + 26] = s.High.Base;
            to[at + 27] = s.High.Thick;
            to[at + 28] = s.High.Cover;
            to[at + 29] = s.High.PeriodRung;
            to[at + 30] = s.High.Softness;
            to[at + 31] = s.High.Detail;
            to[at + 32] = s.High.Strength;
            to[at + 33] = s.High.Cells;
            to[at + 34] = s.High.Ripples;
            to[at + 35] = s.High.Streaks;
            to[at + 36] = s.High.Veil;
            to[at + 37] = s.High.Curl;
            to[at + 38] = s.High.Aspect;
            to[at + 39] = s.High.RippleWavelength;
            to[at + 40] = s.CloudBase;
            to[at + 41] = s.SheetCover;
            to[at + 42] = s.CumulusCover;
            to[at + 43] = s.CapacityCover;
            to[at + 44] = s.CumulusLevel;
            to[at + 45] = s.Convective;
            to[at + 46] = s.Severity;
            to[at + 47] = s.BaseVariation;
            to[at + 48] = s.StratRain;
            to[at + 49] = s.RainBase;
            to[at + 50] = s.WindFactor;
            to[at + 51] = s.Turbulence;
            to[at + 52] = s.Haze;
            to[at + 53] = s.Qnh;
            to[at + 54] = s.Temperature;
            to[at + 55] = s.OpacityMid;
            to[at + 56] = s.OpacityHigh;
            to[at + 57] = s.Frontal;
            to[at + 58] = s.Split;
        }

        public static SkyParams Read(float[] from, int at)
        {
            var s = new SkyParams();
            s.LobeRung = from[at + 0];
            s.PuffDepth = from[at + 1];
            s.BaseSharp = from[at + 2];
            s.BaseWobble = from[at + 3];
            s.Dome = from[at + 4];
            s.Billow = from[at + 5];
            s.Anvil = from[at + 6];
            s.DeckDepth = from[at + 7];
            s.DeckSmooth = from[at + 8];
            s.Streets = from[at + 9];
            s.HorizonCover = from[at + 10];
            s.HorizonDepth = from[at + 11];
            s.Mid.Base = from[at + 12];
            s.Mid.Thick = from[at + 13];
            s.Mid.Cover = from[at + 14];
            s.Mid.PeriodRung = from[at + 15];
            s.Mid.Softness = from[at + 16];
            s.Mid.Detail = from[at + 17];
            s.Mid.Strength = from[at + 18];
            s.Mid.Cells = from[at + 19];
            s.Mid.Ripples = from[at + 20];
            s.Mid.Streaks = from[at + 21];
            s.Mid.Veil = from[at + 22];
            s.Mid.Curl = from[at + 23];
            s.Mid.Aspect = from[at + 24];
            s.Mid.RippleWavelength = from[at + 25];
            s.High.Base = from[at + 26];
            s.High.Thick = from[at + 27];
            s.High.Cover = from[at + 28];
            s.High.PeriodRung = from[at + 29];
            s.High.Softness = from[at + 30];
            s.High.Detail = from[at + 31];
            s.High.Strength = from[at + 32];
            s.High.Cells = from[at + 33];
            s.High.Ripples = from[at + 34];
            s.High.Streaks = from[at + 35];
            s.High.Veil = from[at + 36];
            s.High.Curl = from[at + 37];
            s.High.Aspect = from[at + 38];
            s.High.RippleWavelength = from[at + 39];
            s.CloudBase = from[at + 40];
            s.SheetCover = from[at + 41];
            s.CumulusCover = from[at + 42];
            s.CapacityCover = from[at + 43];
            s.CumulusLevel = from[at + 44];
            s.Convective = from[at + 45];
            s.Severity = from[at + 46];
            s.BaseVariation = from[at + 47];
            s.StratRain = from[at + 48];
            s.RainBase = from[at + 49];
            s.WindFactor = from[at + 50];
            s.Turbulence = from[at + 51];
            s.Haze = from[at + 52];
            s.Qnh = from[at + 53];
            s.Temperature = from[at + 54];
            s.OpacityMid = from[at + 55];
            s.OpacityHigh = from[at + 56];
            s.Frontal = from[at + 57];
            s.Split = from[at + 58];
            return s;
        }

    }
}
