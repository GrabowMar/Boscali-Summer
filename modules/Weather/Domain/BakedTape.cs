using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The tape frame (spec 3.1) reduced to what slice 2 needs: the position s along the tape axis of a map point. The axis
    /// u points toward where the wind comes from, so larger s is upwind and arrives later; one shared meander bends the
    /// boundaries into parallel wavy lines. <c>Offset</c> is the frame's position D at the origin: slice 2 holds it fixed
    /// (the legacy timeline still drives the sky); the tape slices make it move. FlightCloud.shader's TapeS is the twin.
    /// </summary>
    internal readonly struct TapeFrame
    {
        /// <summary>A meander wavelength below this many amplitudes could fold the boundaries; |M'| &lt;= 1 needs 8.1.</summary>
        public const float MinWavelengthPerAmplitude = 8.1f;

        public TapeFrame(float headingDeg, float offsetMetres, float amplitude = 0f, float wavelength = 100000f, float phase = 0f,
            NoiseXform noise = null)
        {
            WeatherMath.HeadingToVector(headingDeg, out UX, out UZ);
            Offset = offsetMetres;
            Amplitude = amplitude;
            Wavelength = Math.Max(wavelength, MinWavelengthPerAmplitude * amplitude);
            Phase = phase;
            this.noise = noise;
        }

        public readonly float UX, UZ, Offset, Amplitude, Wavelength, Phase;
        private readonly NoiseXform noise;

        /// <summary>The tape's noise rotation and offsets; <see cref="NoiseXform.Identity"/> for a frame built without a key.</summary>
        public NoiseXform Noise => noise ?? NoiseXform.Identity;

        /// <summary>Tape-frame coordinates of a world point, q = p + u*D (spec 3.1). The lattice, the base-height field and the
        /// noise live in this frame; slice 3 holds D at 0, so q = p.</summary>
        public void ToTape(float x, float z, out float qx, out float qz)
        {
            qx = x + UX * Offset;
            qz = z + UZ * Offset;
        }

        /// <summary>The world point of a tape-frame point (the inverse of <see cref="ToTape"/>).</summary>
        public void FromTape(float qx, float qz, out float x, out float z)
        {
            x = qx - UX * Offset;
            z = qz - UZ * Offset;
        }

        /// <summary>Position along the tape of a tape-frame point. The offset D cancels, so this is S of the world point it came from.</summary>
        public float STape(float qx, float qz) => qx * UX + qz * UZ + Meander(-qx * UZ + qz * UX);

        /// <summary>The one helper every noise tap goes through (spec 6): the point where the shared noise volume is read for a
        /// noise layer (<see cref="NoiseXform"/>.Low .. HighAligned). Taps divide the result by their scale; none divides a raw x, z.</summary>
        public void NoiseXZ(float x, float z, int layer, out float nx, out float nz) => Noise.Apply(x, z, layer, out nx, out nz);

        public float Meander(float across)
        {
            if (Amplitude == 0f) return 0f;
            float f = 2f * (float)Math.PI * across / Wavelength + Phase;
            return Amplitude * ((float)Math.Sin(f) + 0.35f * (float)Math.Sin(2.1f * f)) / 1.35f;
        }

        /// <summary>s = dot(p, u) + D + M(dot(p, w)); w is u turned a quarter circle.</summary>
        public float S(float x, float z) => x * UX + z * UZ + Offset + Meander(-x * UZ + z * UX);
    }

    /// <summary>
    /// A tape baked once into rows of <see cref="SkyColumns.Count"/> floats (spec 7.2): the array the CPU samples and the
    /// shader texture is uploaded from, so the two read the same numbers. A full tape has <see cref="FullTexels"/> texels over
    /// [0, 3000] km (0.73 km each); a constant tape (slice 2's live path) has one texel, and every lookup clamps to it.
    /// </summary>
    internal sealed class BakedTape
    {
        public const int FullTexels = 4096;
        public const float SpanMetres = 3000000f;

        private readonly float[] scratch = new float[SkyColumns.Count];
        private readonly byte[] genus;

        public BakedTape(int texels)
        {
            if (texels < 1 || texels > FullTexels) throw new ArgumentOutOfRangeException(nameof(texels));
            Texels = texels;
            Data = new float[texels * SkyColumns.Count];
            genus = new byte[texels];
        }

        public int Texels { get; }
        public float[] Data { get; }
        public bool IsConstant => Texels == 1;
        public float TexelMetres => SpanMetres / Texels;

        /// <summary>Bumped whenever the contents change; the shader texture rebuilds on a new value.</summary>
        public int Revision { get; private set; }

        /// <summary>Whether any texel has this layer (cover at least 0.02), and the altitude range the slab march chords through: the lowest base to the highest top over those texels.</summary>
        public bool MidPresent { get; private set; }
        public bool HighPresent { get; private set; }
        public float MidChord0 { get; private set; }
        public float MidChord1 { get; private set; }
        public float HighChord0 { get; private set; }
        public float HighChord1 { get; private set; }

        /// <summary>The widest chord over the thinnest local layer, per layer; spec 4.4 wants at most 4.</summary>
        public float MidChordRatio { get; private set; }
        public float HighChordRatio { get; private set; }

        public SkyParams Texel(int index) => SkyColumns.Read(Data, index * SkyColumns.Count);

        public void SetTexel(int index, in SkyParams sky) => SkyColumns.Write(sky, Data, index * SkyColumns.Count);

        public void SetTexel(int index, in SkyParams sky, LowGenus lowGenus)
        {
            SetTexel(index, sky);
            genus[index] = (byte)lowGenus;
        }

        /// <summary>The low genus of a texel: the genus of the keyframe on the near side of any ramp midpoint (spec 6 "Genus inside a ramp"),
        /// so the lattice reads its candidate count and radius range from it. Never blended. Humilis where the baker wrote none.</summary>
        public LowGenus GenusAt(int index) => (LowGenus)genus[index];

        /// <summary>Makes a one-texel tape hold this row. Allocation-free, and a no-op (same revision) when nothing changed.</summary>
        public void SetConstant(in SkyParams sky, LowGenus lowGenus = LowGenus.Humilis)
        {
            if (Texels != 1) throw new InvalidOperationException("SetConstant needs a one-texel tape");
            SkyColumns.Write(sky, scratch, 0);
            bool same = genus[0] == (byte)lowGenus;
            for (int i = 0; same && i < scratch.Length; i++)
                if (scratch[i] != Data[i]) same = false;
            if (same) return;
            Array.Copy(scratch, Data, scratch.Length);
            genus[0] = (byte)lowGenus;
            Finish();
        }

        /// <summary>Recomputes the presence flags and chords after the texels were written. Call once after a bake.</summary>
        public void Finish()
        {
            Revision++;
            Scan(0, out bool midPresent, out float mid0, out float mid1, out float midRatio);
            Scan(1, out bool highPresent, out float high0, out float high1, out float highRatio);
            MidPresent = midPresent; MidChord0 = mid0; MidChord1 = mid1; MidChordRatio = midRatio;
            HighPresent = highPresent; HighChord0 = high0; HighChord1 = high1; HighChordRatio = highRatio;
        }

        private void Scan(int layer, out bool present, out float chord0, out float chord1, out float ratio)
        {
            present = false; chord0 = 0f; chord1 = 0f; ratio = 0f;
            float lo = float.MaxValue, hi = float.MinValue, thin = float.MaxValue;
            for (int i = 0; i < Texels; i++)
            {
                SkyLayer l = Texel(i).Layer(layer);
                if (!l.Present) continue;
                present = true;
                lo = Math.Min(lo, l.Base);
                hi = Math.Max(hi, l.Top);
                thin = Math.Min(thin, l.Thick);
            }
            if (!present) return;
            chord0 = lo; chord1 = hi;
            ratio = thin > 0f ? (hi - lo) / thin : float.PositiveInfinity;
        }

        /// <summary>
        /// A hand-built two-keyframe tape (spec 11 item 2): sky <paramref name="a"/> downwind of
        /// <paramref name="boundaryMetres"/>, sky <paramref name="b"/> upwind of it, blended by a smoothstep over
        /// <paramref name="rampMetres"/>. Fixtures only: the density check and the bench use it to test spatial variation
        /// and CPU/GPU parity before real tapes exist.
        /// </summary>
        public static BakedTape TwoKeyframes(in SkyParams a, in SkyParams b, float boundaryMetres, float rampMetres,
            LowGenus genusA = LowGenus.Humilis, LowGenus genusB = LowGenus.Humilis)
        {
            var tape = new BakedTape(FullTexels);
            for (int i = 0; i < FullTexels; i++)
            {
                float s = (i + 0.5f) * tape.TexelMetres;
                float w = WeatherMath.Smoothstep(boundaryMetres - 0.5f * rampMetres, boundaryMetres + 0.5f * rampMetres, s);
                SkyParams row = SkyParams.Lerp(a, b, w);
                row.CumulusLevel = CumulusLevels.For(row.CumulusCover, row.CapacityCover);
                tape.SetTexel(i, row, s < boundaryMetres ? genusA : genusB);
            }
            tape.Finish();
            return tape;
        }
    }

    /// <summary>
    /// The CPU's reader of a baked tape (spec 7.2): the same manual lerp along s over the same array the shader reads, so
    /// parity is rounding only. Serves CloudBodies, the cookie, the field's sample and the METAR line.
    /// </summary>
    internal readonly struct TapeSampler
    {
        private readonly BakedTape tape;

        public TapeSampler(BakedTape tape) { this.tape = tape; }

        public static TapeSampler Constant(in SkyParams sky, LowGenus lowGenus = LowGenus.Humilis)
        {
            var baked = new BakedTape(1);
            baked.SetConstant(sky, lowGenus);
            return new TapeSampler(baked);
        }

        /// <summary>The low genus at position s: that of the nearest texel, never blended (the lattice reads its constants from it).</summary>
        public LowGenus GenusAt(float s)
        {
            if (float.IsNaN(s) || tape.Texels == 1) return tape.GenusAt(0);
            int i = (int)Math.Max(0f, Math.Min(tape.Texels - 1, (float)Math.Floor(s / tape.TexelMetres)));
            return tape.GenusAt(i);
        }

        public BakedTape Tape => tape;
        public bool IsValid => tape != null;
        public bool IsConstant => tape != null && tape.IsConstant;

        /// <summary>The row at position s (metres along the tape). s clamps to the tape's ends, like the shader.</summary>
        public SkyParams At(float s)
        {
            if (float.IsNaN(s)) s = 0f; // a bad position reads the first texel; a NaN index must never reach the array
            int n = tape.Texels;
            if (n == 1) return tape.Texel(0);
            float u = s / tape.TexelMetres - 0.5f;
            float floor = (float)Math.Floor(u);
            float f = u - floor;
            float last = n - 1;
            int i0 = (int)Math.Max(0f, Math.Min(last, floor));
            int i1 = (int)Math.Max(0f, Math.Min(last, floor + 1f));
            SkyParams a = tape.Texel(i0);
            if (i0 == i1) return a;
            SkyParams b = tape.Texel(i1);
            SkyParams row = SkyParams.Blend(a, b, f);
            row.CumulusLevel = f < 0.5f ? a.CumulusLevel : b.CumulusLevel; // baked per texel and never lerped (spec 4.1)
            return row;
        }
    }

    /// <summary>A hand-built tape and its frame, for the fixtures (density check, bench, tests) that need the sky to vary in space.</summary>
    internal sealed class TapeFixture
    {
        public TapeFixture(BakedTape tape, TapeFrame frame) { Sampler = new TapeSampler(tape); Frame = frame; }
        public TapeSampler Sampler { get; }
        public TapeFrame Frame { get; }
    }
}
