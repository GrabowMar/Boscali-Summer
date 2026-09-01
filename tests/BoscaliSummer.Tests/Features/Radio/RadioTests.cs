using System;
using System.IO;
using BoscaliSummer.Features.Radio.Presentation;
using BoscaliSummer.Features.Radio.Runtime;

namespace BoscaliSummer.Tests.Features.Radio
{
    internal static class RadioTests
    {
        public static void Run()
        {
            string root = Path.Combine(
                Path.GetTempPath(), "BoscaliSummer.RadioTests." + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(Path.Combine(root, "02 Night Ops"));
                Directory.CreateDirectory(Path.Combine(root, "01 Day Ops"));
                Directory.CreateDirectory(Path.Combine(root, "01 Day Ops", "Nested"));
                Directory.CreateDirectory(Path.Combine(root, "Empty Starter"));
                File.WriteAllBytes(Path.Combine(root, "Root Track.ogg"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(root, "Ignored.mp3"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(root, "01 Day Ops", "Bravo.wav"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(root, "01 Day Ops", "Alpha.ogg"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(root, "01 Day Ops", "Nested", "Too Deep.ogg"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(root, "02 Night Ops", "Night.OGG"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(root, "Empty Starter", "station.png"), new byte[] { 1 });

                RadioLibrary library = RadioLibrary.Scan(root);
                TestAssert.That(library.TrackCount == 4, "radio scan accepted an unsupported or nested track");
                TestAssert.That(library.Channels.Length == 3, "radio scan produced the wrong station count");
                TestAssert.That(library.Channels[0].Name == "LOCAL", "root tracks did not become LOCAL");
                TestAssert.That(library.Channels[1].Name == "01 Day Ops", "stations were not sorted");
                TestAssert.That(library.Channels[1].Tracks[0].Title == "Alpha", "tracks were not sorted");
                TestAssert.That(RadioLibrary.IsSupportedExtension(".WAV"), "WAV extension was rejected");
                TestAssert.That(!RadioLibrary.IsSupportedExtension(".mp3"), "unprobed MP3 extension was accepted");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }

            byte[] valid = MakePngHeader(256, 128);
            TestAssert.That(PngIconHeader.IsSupported(valid, out int width, out int height),
                "valid bounded PNG header was rejected");
            TestAssert.That(width == 256 && height == 128, "PNG dimensions were read incorrectly");
            TestAssert.That(!PngIconHeader.IsSupported(MakePngHeader(257, 128), out _, out _),
                "oversized PNG width was accepted");
            TestAssert.That(!PngIconHeader.IsSupported(MakePngHeader(128, 0), out _, out _),
                "zero-height PNG was accepted");
            valid[1] = 0;
            TestAssert.That(!PngIconHeader.IsSupported(valid, out _, out _),
                "invalid PNG signature was accepted");
        }

        private static byte[] MakePngHeader(uint width, uint height)
        {
            byte[] data = new byte[24];
            byte[] signature = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
            Array.Copy(signature, data, signature.Length);
            data[12] = (byte)'I';
            data[13] = (byte)'H';
            data[14] = (byte)'D';
            data[15] = (byte)'R';
            WriteBigEndian(data, 16, width);
            WriteBigEndian(data, 20, height);
            return data;
        }

        private static void WriteBigEndian(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }
    }
}
