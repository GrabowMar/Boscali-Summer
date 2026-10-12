using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The golden vector of spec 9.1: a hash over the keyframes, ramps, markers and fronts of tapes generated for about twenty fixed keys. Editing a sky or a
    /// story table changes it, which fails <c>TapeGoldenTests</c> (and the PatchProbe pin) until someone updates <c>WeatherNet.TapeGoldenVector</c> next to
    /// <c>ProtocolVersion</c> and bumps the protocol at release: two peers on different tables would draw different skies for the same key.
    /// </summary>
    internal static class TapeGolden
    {
        private const uint Basis = 2166136261u, Prime = 16777619u;

        public static uint Hash(WeatherTape tape)
        {
            uint h = Basis;
            var buffer = new float[SkyColumns.Count];
            foreach (TapeKeyframe k in tape.Keyframes)
            {
                h = Mix(h, k.S); h = Mix(h, k.Width); h = Mix(h, k.SkyIndex); h = Mix(h, (int)k.Genus);
                SkyColumns.Write(k.Sky, buffer, 0);
                for (int i = 0; i < buffer.Length; i++) h = Mix(h, buffer[i]);
            }
            foreach (TapeMarker m in tape.Markers.Items) { h = Mix(h, (int)m.Kind); h = Mix(h, m.S); h = Mix(h, m.Strength); }
            foreach (TapeFront f in tape.Fronts) { h = Mix(h, f.Story); h = Mix(h, f.SLead); h = Mix(h, f.SMain); }
            h = Mix(h, tape.MeanderAmplitude); h = Mix(h, tape.MeanderWavelength); h = Mix(h, tape.MeanderPhase);
            return Mix(h, tape.OpeningEnd);
        }

        /// <summary>The vector: the hash of the hashes of 20 fixed keys (seeds, stories, pins, classes, opening bases).</summary>
        public static uint Compute()
        {
            uint h = Basis;
            for (int i = 0; i < 20; i++)
            {
                byte story = i % 4 == 3 ? (byte)(i / 4 % WeatherStories.Count) : WeatherKey.None;
                byte pin = i == 17 ? (byte)(i + 3) : WeatherKey.None;
                var key = new WeatherKey((uint)(1000 + 37 * i), 0f, true, (byte)(i % WeatherKey.ClassCount), layoutSalt: (byte)(i % 3), story: story, skyPin: pin,
                    openingBase: i % 2 == 0 ? 0f : 1000f + 100f * i);
                h = Mix(h, (int)Hash(WeatherTape.Generate(TapeInputs.From(key))));
            }
            return h;
        }

        private static uint Mix(uint h, float value) => Mix(h, BitConverter.ToInt32(BitConverter.GetBytes(value), 0));

        private static uint Mix(uint h, int value)
        {
            unchecked
            {
                for (int b = 0; b < 4; b++) h = (h ^ (uint)((value >> (8 * b)) & 0xFF)) * Prime;
                return h;
            }
        }
    }
}
