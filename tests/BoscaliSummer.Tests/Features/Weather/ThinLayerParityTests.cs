using System;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// A pre-GPU estimate of the density check's thin-layer parity. The GPU filters a 3D texture with 8-bit weights (truncated), and the smooth test
    /// volume's gains magnify that to a few thousandths; the density check holds 0.012. <see cref="CloudBodies.QuantiseWeights"/> makes the CPU
    /// twin truncate its weights the same way, and the delta between the exact and the truncated twin tracked the GPU's own (look pass round 1: 0.0197
    /// emulated against 0.0200 measured on the keyed cell probe). A formula change that pushes this over the density check's tolerance fails here in a
    /// minute instead of after a twelve-minute Unity run. The probes are the density check's (a 5 x 5 grid at the layer's mid height, the check's seed and
    /// the identity frame); the bound is the check's tolerance.
    /// </summary>
    internal static class ThinLayerParityTests
    {
        public static void Run()
        {
            byte[] bytes = SmoothNoise(16);
            (string, SkyLayer)[] cases =
            {
                ("cells", Layer(1, 0, 0, 0, 0f, 5f)), ("ripples", Layer(0, 1, 0, 0, 0f, 6f)), ("streaks", Layer(0, 0, 1, 0, 0f, 8f)),
                ("mix+curl", Layer(0.3f, 0.3f, 0.3f, 0.1f, 0.8f, 7f)), ("period-ramp", Layer(0.4f, 0.3f, 0.3f, 0f, 0f, 6.5f)),
                ("period-edge", Layer(0.4f, 0.3f, 0.3f, 0f, 0f, 6.3f)),
            };
            try
            {
                foreach ((string name, SkyLayer layer) in cases)
                    foreach (bool keyed in new[] { false, true })
                    {
                        var sky = new SkyParams { Mid = layer };
                        TapeFrame frame = keyed ? new TapeFrame(0f, 0f, noise: new NoiseXform(0x5EED1234u)) : default;
                        var cpu = keyed ? new CloudBodies(bytes, 16, TapeSampler.Constant(sky), frame) : new CloudBodies(bytes, 16, sky);
                        float height = layer.Base + layer.Thick * .5f, worst = 0f;
                        for (int z = -12000; z <= 12000; z += 6000)
                            for (int x = -12000; x <= 12000; x += 6000)
                            {
                                CloudBodies.QuantiseWeights = false;
                                float exact = cpu.Slab2D(layer, x, height, z);
                                CloudBodies.QuantiseWeights = true;
                                float truncated = cpu.Slab2D(layer, x, height, z);
                                worst = Math.Max(worst, Math.Abs(exact - truncated));
                            }
                        Console.WriteLine("  parity " + name + (keyed ? " keyed " : " identity ") + worst.ToString("0.0000"));
                        TestAssert.That(worst < 0.012f, name + (keyed ? " (keyed)" : " (identity)") + ": the 8-bit weight delta of the thin-layer field is " + worst.ToString("0.0000") + ", the density check's tolerance is 0.012");
                    }
            }
            finally { CloudBodies.QuantiseWeights = false; }
        }

        private static SkyLayer Layer(float cells, float ripples, float streaks, float veil, float curl, float rung)
        {
            SkyLayer l = SkyArchetypes.Layer(LayerKind.AcFloccus).ToLayer(0.55f);
            l.Cells = cells; l.Ripples = ripples; l.Streaks = streaks; l.Veil = veil; l.Curl = curl; l.PeriodRung = rung;
            l.Aspect = 3f; l.RippleWavelength = 900f; l.Strength = 0.36f;
            return l;
        }

        /// <summary>The density check's smooth volume: a wrapped sum of low sine waves per channel, 128 +- 24.</summary>
        private static byte[] SmoothNoise(int n)
        {
            var bytes = new byte[n * n * n * 4];
            int[][] waves = { new[] { 1, 2, 1 }, new[] { 2, 1, 3 }, new[] { 1, 3, 2 }, new[] { 3, 1, 1 } };
            for (int z = 0; z < n; z++)
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int at = ((z * n + y) * n + x) * 4;
                        for (int c = 0; c < 4; c++)
                        {
                            double phase = 2.0 * Math.PI * (waves[c][0] * x + waves[c][1] * y + waves[c][2] * z) / n + c * 1.3;
                            bytes[at + c] = (byte)Math.Round(128.0 + 14.0 * Math.Sin(phase) + 10.0 * Math.Sin(2.0 * phase + 0.7 * c));
                        }
                    }
            return bytes;
        }
    }
}
