using System;
using System.IO;
using BoscaliSummer.Core;
using BoscaliSummer.Framework.Features;

namespace BoscaliSummer.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            VerifyFeatureGraph();
            VerifyServiceRegistry();
            VerifyRadioLibrary();
            VerifyPngIconHeader();

            Assert(Deterministic.Hash(1, 2, 3, 4) == Deterministic.Hash(1, 2, 3, 4), "hash must be stable");
            Assert(Deterministic.Hash(1, 2, 3, 4) != Deterministic.Hash(1, 2, 3, 5), "salt must affect hash");
            Assert(Deterministic.HashString("Airbase Alpha") == Deterministic.HashString("Airbase Alpha"), "string hash must be stable");
            Assert(Deterministic.HashString("Airbase Alpha") != Deterministic.HashString("Airbase Bravo"), "names must separate seeds");

            for (int i = -1000; i <= 1000; i++)
            {
                float value = Deterministic.UnitFloat(Deterministic.Hash(i, i * 7, -i));
                Assert(value >= 0f && value < 1f, "unit float outside [0,1)");
            }

            Assert(Deterministic.CellKey(0f, 0f, 32f) == Deterministic.CellKey(31.99f, 31.99f, 32f), "same positive cell split");
            Assert(Deterministic.CellKey(-0.01f, -0.01f, 32f) == Deterministic.CellKey(-31.99f, -31.99f, 32f), "negative floor cell split");
            Assert(Deterministic.CellKey(-0.01f, 0f, 32f) != Deterministic.CellKey(0f, 0f, 32f), "negative and positive cells collided");

            Console.WriteLine("BoscaliSummer.Tests: all framework and deterministic assertions passed.");
            return 0;
        }

        private static void VerifyFeatureGraph()
        {
            FeatureMetadata[] features =
            {
                new FeatureMetadata("urban-combat", "Urban combat", "fire-and-destruction"),
                new FeatureMetadata("networking", "Networking"),
                new FeatureMetadata("fire-and-destruction", "Fire and destruction", "networking")
            };
            int[] order = FeatureGraph.Sort(features);
            Assert(order.Length == 3, "feature graph dropped an entry");
            Assert(order[0] == 1 && order[1] == 2 && order[2] == 0,
                "feature dependencies were not ordered before consumers");
            Assert(FeatureId.IsValid("support-calls"), "valid feature ID was rejected");
            Assert(!FeatureId.IsValid("Support Calls"), "invalid feature ID was accepted");

            AssertThrows<InvalidOperationException>(() => FeatureGraph.Sort(new[]
            {
                new FeatureMetadata("same", "One"),
                new FeatureMetadata("same", "Two")
            }), "duplicate feature IDs were accepted");
            AssertThrows<InvalidOperationException>(() => FeatureGraph.Sort(new[]
            {
                new FeatureMetadata("dependent", "Dependent", "missing")
            }), "missing feature dependency was accepted");
            AssertThrows<InvalidOperationException>(() => FeatureGraph.Sort(new[]
            {
                new FeatureMetadata("cycle-a", "Cycle A", "cycle-b"),
                new FeatureMetadata("cycle-b", "Cycle B", "cycle-a")
            }), "feature dependency cycle was accepted");
        }

        private static void VerifyServiceRegistry()
        {
            var registry = new ServiceRegistry();
            var expected = new ExampleService();
            registry.Add(expected);
            Assert(registry.TryGet(out ExampleService actual) && ReferenceEquals(expected, actual),
                "registered service could not be resolved");
            Assert(ReferenceEquals(expected, registry.GetRequired<ExampleService>()),
                "required service lookup returned the wrong instance");
            AssertThrows<InvalidOperationException>(() => registry.Add(new ExampleService()),
                "duplicate service registration was accepted");
            AssertThrows<InvalidOperationException>(() => registry.GetRequired<MissingService>(),
                "missing required service was accepted");
        }

        private static void VerifyRadioLibrary()
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
                Assert(library.TrackCount == 4, "radio scan accepted an unsupported or nested track");
                Assert(library.Channels.Length == 3, "radio scan produced the wrong station count");
                Assert(library.Channels[0].Name == "LOCAL", "root tracks did not become LOCAL");
                Assert(library.Channels[1].Name == "01 Day Ops", "stations were not sorted");
                Assert(library.Channels[1].Tracks[0].Title == "Alpha", "tracks were not sorted");
                Assert(RadioLibrary.IsSupportedExtension(".WAV"), "WAV extension was rejected");
                Assert(!RadioLibrary.IsSupportedExtension(".mp3"), "unprobed MP3 extension was accepted");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void VerifyPngIconHeader()
        {
            byte[] valid = MakePngHeader(256, 128);
            Assert(PngIconHeader.IsSupported(valid, out int width, out int height),
                "valid bounded PNG header was rejected");
            Assert(width == 256 && height == 128, "PNG dimensions were read incorrectly");
            Assert(!PngIconHeader.IsSupported(MakePngHeader(257, 128), out _, out _),
                "oversized PNG width was accepted");
            Assert(!PngIconHeader.IsSupported(MakePngHeader(128, 0), out _, out _),
                "zero-height PNG was accepted");
            valid[1] = 0;
            Assert(!PngIconHeader.IsSupported(valid, out _, out _),
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

        private static void AssertThrows<TException>(Action action, string message)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(message);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class ExampleService { }
        private sealed class MissingService { }
    }
}
