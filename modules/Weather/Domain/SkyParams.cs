using System.Runtime.CompilerServices;
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
                Base = Scalar.Lerp(4200f, 3300f, s), Thick = Scalar.Lerp(400f, 1700f, s), Cover = cover,
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
                Base = 8600f, Thick = Scalar.Lerp(500f, 900f, v), Cover = cover,
                PeriodRung = 9f, Softness = 0.5f, Detail = 0.7f, Strength = 0.12f,
                Cells = 0f, Ripples = (1f - flat) * ripple, Streaks = (1f - flat) * (1f - ripple), Veil = flat,
                Curl = 0f, Aspect = Scalar.Lerp(7f, 1f, v), RippleWavelength = 0.85f * Ladder.PeriodMetres(9f),
            };
        }
    }

    /// <summary>
    /// One sky: the low-cloud genus, two thin layers and the CPU-only weather columns, all floats so a keyframe blend
    /// is a plain lerp (spec 4.1). <see cref="SkyColumns"/> flattens it to the 57-float row the baked tape stores and
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

        /// <summary>The two ends of a lerp from <paramref name="a"/> to <paramref name="b"/> as <see cref="Lerp"/> blends them: an absent layer has taken the other side's geometry. They do not depend
        /// on t, so a caller lerping the same pair at many t (the baker, along a ramp) makes them once and calls <see cref="LerpBetween"/>.</summary>
        public static void LerpEnds(in SkyParams a, in SkyParams b, out SkyParams x, out SkyParams y)
        {
            x = a; y = b;
            Inherit(ref x.Mid, in b.Mid); Inherit(ref y.Mid, in a.Mid);
            Inherit(ref x.High, in b.High); Inherit(ref y.High, in a.High);
        }

        /// <summary><see cref="Lerp"/> from the ends <see cref="LerpEnds"/> made.</summary>
        public static SkyParams LerpBetween(in SkyParams x, in SkyParams y, float t)
        {
            SkyParams result = Blend(x, y, t);
            // Aspect and RippleWavelength divide the noise coordinate, so lerping them would slide the pattern through scales across a
            // ramp (spec 6: noise is never sampled at a scale that varies in space or time). Hold them at the nearer keyframe's value;
            // the texel lerp of the baked tape softens the step over one texel (732 m).
            Hold(ref result.Mid, in x.Mid, in y.Mid, t);
            Hold(ref result.High, in x.High, in y.High, t);
            return result;
        }

        private static void Hold(ref SkyLayer layer, in SkyLayer from, in SkyLayer to, float t)
        {
            layer.Aspect = t < 0.5f ? from.Aspect : to.Aspect;
            layer.RippleWavelength = t < 0.5f ? from.RippleWavelength : to.RippleWavelength;
        }

        /// <summary>Plain lerp of all 57 columns (CumulusLevel included) with no layer inheritance and no hold: what the tape texel lerp does on the CPU and the GPU.
        /// Field by field with the same a + (b - a) * t per column as the array lerp it replaced (<see cref="SkyColumns.ReadBlend"/> is its twin over the baked array): no copies, no scratch.</summary>
        public static SkyParams Blend(in SkyParams a, in SkyParams b, float t)
        {
            var r = new SkyParams();
            r.LobeRung = SkyColumns.L(a.LobeRung, b.LobeRung, t);
            r.PuffDepth = SkyColumns.L(a.PuffDepth, b.PuffDepth, t);
            r.BaseSharp = SkyColumns.L(a.BaseSharp, b.BaseSharp, t);
            r.BaseWobble = SkyColumns.L(a.BaseWobble, b.BaseWobble, t);
            r.Dome = SkyColumns.L(a.Dome, b.Dome, t);
            r.Billow = SkyColumns.L(a.Billow, b.Billow, t);
            r.Anvil = SkyColumns.L(a.Anvil, b.Anvil, t);
            r.DeckDepth = SkyColumns.L(a.DeckDepth, b.DeckDepth, t);
            r.DeckSmooth = SkyColumns.L(a.DeckSmooth, b.DeckSmooth, t);
            r.Streets = SkyColumns.L(a.Streets, b.Streets, t);
            r.HorizonCover = SkyColumns.L(a.HorizonCover, b.HorizonCover, t);
            r.HorizonDepth = SkyColumns.L(a.HorizonDepth, b.HorizonDepth, t);
            r.Mid.Base = SkyColumns.L(a.Mid.Base, b.Mid.Base, t);
            r.Mid.Thick = SkyColumns.L(a.Mid.Thick, b.Mid.Thick, t);
            r.Mid.Cover = SkyColumns.L(a.Mid.Cover, b.Mid.Cover, t);
            r.Mid.PeriodRung = SkyColumns.L(a.Mid.PeriodRung, b.Mid.PeriodRung, t);
            r.Mid.Softness = SkyColumns.L(a.Mid.Softness, b.Mid.Softness, t);
            r.Mid.Detail = SkyColumns.L(a.Mid.Detail, b.Mid.Detail, t);
            r.Mid.Strength = SkyColumns.L(a.Mid.Strength, b.Mid.Strength, t);
            r.Mid.Cells = SkyColumns.L(a.Mid.Cells, b.Mid.Cells, t);
            r.Mid.Ripples = SkyColumns.L(a.Mid.Ripples, b.Mid.Ripples, t);
            r.Mid.Streaks = SkyColumns.L(a.Mid.Streaks, b.Mid.Streaks, t);
            r.Mid.Veil = SkyColumns.L(a.Mid.Veil, b.Mid.Veil, t);
            r.Mid.Curl = SkyColumns.L(a.Mid.Curl, b.Mid.Curl, t);
            r.Mid.Aspect = SkyColumns.L(a.Mid.Aspect, b.Mid.Aspect, t);
            r.Mid.RippleWavelength = SkyColumns.L(a.Mid.RippleWavelength, b.Mid.RippleWavelength, t);
            r.High.Base = SkyColumns.L(a.High.Base, b.High.Base, t);
            r.High.Thick = SkyColumns.L(a.High.Thick, b.High.Thick, t);
            r.High.Cover = SkyColumns.L(a.High.Cover, b.High.Cover, t);
            r.High.PeriodRung = SkyColumns.L(a.High.PeriodRung, b.High.PeriodRung, t);
            r.High.Softness = SkyColumns.L(a.High.Softness, b.High.Softness, t);
            r.High.Detail = SkyColumns.L(a.High.Detail, b.High.Detail, t);
            r.High.Strength = SkyColumns.L(a.High.Strength, b.High.Strength, t);
            r.High.Cells = SkyColumns.L(a.High.Cells, b.High.Cells, t);
            r.High.Ripples = SkyColumns.L(a.High.Ripples, b.High.Ripples, t);
            r.High.Streaks = SkyColumns.L(a.High.Streaks, b.High.Streaks, t);
            r.High.Veil = SkyColumns.L(a.High.Veil, b.High.Veil, t);
            r.High.Curl = SkyColumns.L(a.High.Curl, b.High.Curl, t);
            r.High.Aspect = SkyColumns.L(a.High.Aspect, b.High.Aspect, t);
            r.High.RippleWavelength = SkyColumns.L(a.High.RippleWavelength, b.High.RippleWavelength, t);
            r.CloudBase = SkyColumns.L(a.CloudBase, b.CloudBase, t);
            r.SheetCover = SkyColumns.L(a.SheetCover, b.SheetCover, t);
            r.CumulusCover = SkyColumns.L(a.CumulusCover, b.CumulusCover, t);
            r.CapacityCover = SkyColumns.L(a.CapacityCover, b.CapacityCover, t);
            r.CumulusLevel = SkyColumns.L(a.CumulusLevel, b.CumulusLevel, t);
            r.Convective = SkyColumns.L(a.Convective, b.Convective, t);
            r.Severity = SkyColumns.L(a.Severity, b.Severity, t);
            r.BaseVariation = SkyColumns.L(a.BaseVariation, b.BaseVariation, t);
            r.StratRain = SkyColumns.L(a.StratRain, b.StratRain, t);
            r.RainBase = SkyColumns.L(a.RainBase, b.RainBase, t);
            r.WindFactor = SkyColumns.L(a.WindFactor, b.WindFactor, t);
            r.Turbulence = SkyColumns.L(a.Turbulence, b.Turbulence, t);
            r.Haze = SkyColumns.L(a.Haze, b.Haze, t);
            r.Qnh = SkyColumns.L(a.Qnh, b.Qnh, t);
            r.Temperature = SkyColumns.L(a.Temperature, b.Temperature, t);
            r.OpacityMid = SkyColumns.L(a.OpacityMid, b.OpacityMid, t);
            r.OpacityHigh = SkyColumns.L(a.OpacityHigh, b.OpacityHigh, t);
            return r;
        }

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
        public const int Count = 57;

        // Columns that are read on their own, without building a row (see Names; the lattices and the baked tape's layer scan).
        public const int PuffDepthAt = 1, StreetsAt = 9, MidBaseAt = 12, HighBaseAt = 26, CloudBaseAt = 40,
            CumulusLevelAt = 44, ConvectiveAt = 45, SeverityAt = 46;

        /// <summary>Shader texture row of each column, or -1 for CPU-only columns (spec 7.2: rows 0-2 low genus, 3-6 mid, 7-10 high).</summary>
        public static readonly sbyte[] GpuRow =
        {
            0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
            3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5,
            6, 6, 7, 7, 7, 7, 8, 8, 8, 8, 9, 9,
            9, 9, 10, 10, -1, -1, -1, -1, -1, -1, -1, -1,
            -1, -1, -1, -1, -1, -1, -1, -1, -1,
        };

        public static readonly sbyte[] GpuChannel =
        {
            0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3,
            0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3,
            0, 1, 0, 1, 2, 3, 0, 1, 2, 3, 0, 1,
            2, 3, 0, 1, -1, -1, -1, -1, -1, -1, -1, -1,
            -1, -1, -1, -1, -1, -1, -1, -1, -1,
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
            return s;
        }

        /// <summary>The lerp of two rows of a column array: <c>Read(d, at1)</c> blended toward <c>Read(d, at2)</c> by t, column by column as a + (b - a) * t, without building the two rows.</summary>
        public static SkyParams ReadBlend(float[] d, int at1, int at2, float t)
        {
            var s = new SkyParams();
            s.LobeRung = L(d[at1 + 0], d[at2 + 0], t);
            s.PuffDepth = L(d[at1 + 1], d[at2 + 1], t);
            s.BaseSharp = L(d[at1 + 2], d[at2 + 2], t);
            s.BaseWobble = L(d[at1 + 3], d[at2 + 3], t);
            s.Dome = L(d[at1 + 4], d[at2 + 4], t);
            s.Billow = L(d[at1 + 5], d[at2 + 5], t);
            s.Anvil = L(d[at1 + 6], d[at2 + 6], t);
            s.DeckDepth = L(d[at1 + 7], d[at2 + 7], t);
            s.DeckSmooth = L(d[at1 + 8], d[at2 + 8], t);
            s.Streets = L(d[at1 + 9], d[at2 + 9], t);
            s.HorizonCover = L(d[at1 + 10], d[at2 + 10], t);
            s.HorizonDepth = L(d[at1 + 11], d[at2 + 11], t);
            s.Mid.Base = L(d[at1 + 12], d[at2 + 12], t);
            s.Mid.Thick = L(d[at1 + 13], d[at2 + 13], t);
            s.Mid.Cover = L(d[at1 + 14], d[at2 + 14], t);
            s.Mid.PeriodRung = L(d[at1 + 15], d[at2 + 15], t);
            s.Mid.Softness = L(d[at1 + 16], d[at2 + 16], t);
            s.Mid.Detail = L(d[at1 + 17], d[at2 + 17], t);
            s.Mid.Strength = L(d[at1 + 18], d[at2 + 18], t);
            s.Mid.Cells = L(d[at1 + 19], d[at2 + 19], t);
            s.Mid.Ripples = L(d[at1 + 20], d[at2 + 20], t);
            s.Mid.Streaks = L(d[at1 + 21], d[at2 + 21], t);
            s.Mid.Veil = L(d[at1 + 22], d[at2 + 22], t);
            s.Mid.Curl = L(d[at1 + 23], d[at2 + 23], t);
            s.Mid.Aspect = L(d[at1 + 24], d[at2 + 24], t);
            s.Mid.RippleWavelength = L(d[at1 + 25], d[at2 + 25], t);
            s.High.Base = L(d[at1 + 26], d[at2 + 26], t);
            s.High.Thick = L(d[at1 + 27], d[at2 + 27], t);
            s.High.Cover = L(d[at1 + 28], d[at2 + 28], t);
            s.High.PeriodRung = L(d[at1 + 29], d[at2 + 29], t);
            s.High.Softness = L(d[at1 + 30], d[at2 + 30], t);
            s.High.Detail = L(d[at1 + 31], d[at2 + 31], t);
            s.High.Strength = L(d[at1 + 32], d[at2 + 32], t);
            s.High.Cells = L(d[at1 + 33], d[at2 + 33], t);
            s.High.Ripples = L(d[at1 + 34], d[at2 + 34], t);
            s.High.Streaks = L(d[at1 + 35], d[at2 + 35], t);
            s.High.Veil = L(d[at1 + 36], d[at2 + 36], t);
            s.High.Curl = L(d[at1 + 37], d[at2 + 37], t);
            s.High.Aspect = L(d[at1 + 38], d[at2 + 38], t);
            s.High.RippleWavelength = L(d[at1 + 39], d[at2 + 39], t);
            s.CloudBase = L(d[at1 + 40], d[at2 + 40], t);
            s.SheetCover = L(d[at1 + 41], d[at2 + 41], t);
            s.CumulusCover = L(d[at1 + 42], d[at2 + 42], t);
            s.CapacityCover = L(d[at1 + 43], d[at2 + 43], t);
            s.CumulusLevel = L(d[at1 + 44], d[at2 + 44], t);
            s.Convective = L(d[at1 + 45], d[at2 + 45], t);
            s.Severity = L(d[at1 + 46], d[at2 + 46], t);
            s.BaseVariation = L(d[at1 + 47], d[at2 + 47], t);
            s.StratRain = L(d[at1 + 48], d[at2 + 48], t);
            s.RainBase = L(d[at1 + 49], d[at2 + 49], t);
            s.WindFactor = L(d[at1 + 50], d[at2 + 50], t);
            s.Turbulence = L(d[at1 + 51], d[at2 + 51], t);
            s.Haze = L(d[at1 + 52], d[at2 + 52], t);
            s.Qnh = L(d[at1 + 53], d[at2 + 53], t);
            s.Temperature = L(d[at1 + 54], d[at2 + 54], t);
            s.OpacityMid = L(d[at1 + 55], d[at2 + 55], t);
            s.OpacityHigh = L(d[at1 + 56], d[at2 + 56], t);
            return s;
        }

        /// <summary>One column of a lerp: the expression every blend uses (kept in one place so the twins cannot drift).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float L(float a, float b, float t) => a + (b - a) * t;

    }
}
