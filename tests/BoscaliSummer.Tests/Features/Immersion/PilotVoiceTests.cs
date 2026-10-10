using System;
using System.IO;
using System.Text;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.Immersion.Domain;

namespace BoscaliSummer.Tests.Features.Immersion
{
    internal static class PilotVoiceTests
    {
        private const int Rate = 22050;

        public static void Run()
        {
            short[] ramp = { 0, 16384, -16384, 32767, -32768 };
            float[] decoded = PcmWav.Decode(Wav(1, 16, Rate, ramp, null), Rate);
            TestAssert.That(decoded != null && decoded.Length == 5 && decoded[0] == 0f && decoded[1] == .5f && decoded[2] == -.5f &&
                decoded[3] > .9999f && decoded[3] < 1f && decoded[4] == -1f, "Mono 16-bit PCM decodes to floats in [-1, 1).");
            float[] padded = PcmWav.Decode(Wav(1, 16, Rate, ramp, "LIST"), Rate);
            TestAssert.That(padded != null && padded.Length == 5 && padded[1] == .5f, "Chunks before the data are skipped, odd sizes padded.");
            TestAssert.That(PcmWav.Decode(Wav(2, 16, Rate, ramp, null), Rate) == null, "Stereo is refused.");
            TestAssert.That(PcmWav.Decode(Wav(1, 8, Rate, ramp, null), Rate) == null, "8-bit is refused.");
            TestAssert.That(PcmWav.Decode(Wav(1, 16, 44100, ramp, null), Rate) == null,
                "Another sample rate is refused; clips are never resampled at runtime.");
            byte[] cut = Wav(1, 16, Rate, ramp, null);
            Array.Resize(ref cut, cut.Length - 3);
            TestAssert.That(PcmWav.Decode(cut, Rate) == null, "A truncated file is refused.");
            TestAssert.That(PcmWav.Decode(null, Rate) == null && PcmWav.Decode(new byte[64], Rate) == null,
                "Missing or foreign data decodes to nothing.");

            TestAssert.That(BreathSound.Keh4 - BreathSound.Keh0 == 4 && BreathSound.Gasp1 - BreathSound.Gasp0 == 1,
                "Variant sounds are contiguous, so a random pick stays inside its family.");

            string root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "BoscaliSummer.sln"))) root = Path.GetDirectoryName(root);
            string[] files = Directory.GetFiles(Path.Combine(root, "modules/Immersion/Assets/Voice"), "*.wav");
            TestAssert.That(files.Length > 0, "Recorded pilot voices ship.");
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                TestAssert.That(name == name.ToLowerInvariant() && Enum.TryParse(name, true, out BreathSound sound) &&
                    Enum.IsDefined(typeof(BreathSound), sound), name + ".wav is named after the BreathSound it replaces, in lower case.");
                float[] a = PcmWav.Decode(File.ReadAllBytes(file), Rate);
                TestAssert.That(a != null, name + ".wav is mono 16-bit " + Rate + " Hz.");
                float seconds = a.Length / (float)Rate, peak = 0f;
                double sum = 0;
                foreach (float v in a) { peak = Math.Max(peak, Math.Abs(v)); sum += v * v; }
                TestAssert.That(seconds >= .2f && seconds <= 2.5f, name + " lasts 0.2-2.5 s, got " + seconds);
                TestAssert.That(peak > .5f && peak <= .75f && Math.Sqrt(sum / a.Length) > .01,
                    name + " is levelled to about -3 dBFS and audible, peak " + peak);
                TestAssert.That(Math.Abs(a[0]) < .01f && Math.Abs(a[a.Length - 1]) < .01f, name + " starts and ends silent (no click).");
            }
        }

        // A minimal RIFF/WAVE file; extraChunk inserts an odd-sized chunk (plus its pad byte) before the data.
        private static byte[] Wav(short channels, short bits, int rate, short[] samples, string extraChunk)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(0); w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write(channels); w.Write(rate);
                w.Write(rate * channels * bits / 8); w.Write((short)(channels * bits / 8)); w.Write(bits);
                if (extraChunk != null) { w.Write(Encoding.ASCII.GetBytes(extraChunk)); w.Write(3); w.Write(new byte[] { 1, 2, 3, 0 }); }
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(samples.Length * 2);
                foreach (short sample in samples) w.Write(sample);
                w.Flush();
                return stream.ToArray();
            }
        }
    }
}
