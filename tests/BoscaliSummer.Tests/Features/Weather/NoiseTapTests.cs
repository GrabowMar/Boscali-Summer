using System;
using System.IO;
using System.Text.RegularExpressions;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>Noise decorrelation on the CPU (spec 6): every tap of CloudBodies goes through TapeFrame.NoiseXZ (a source scan, the grep of
    /// the spec), a frame without a key changes nothing, a keyed one gives each tape its own clouds, and the streaks of a thin layer stay
    /// along the wind whatever the rotation.</summary>
    internal static class NoiseTapTests
    {
        public static void Run()
        {
            EveryCpuTapGoesThroughTheFrame();
            AFrameWithoutAKeyChangesNothing();
            EachTapeGetsItsOwnClouds();
            StreaksStayAlongTheWind();
            EveryShaderTapGoesThroughNoiseXZ();
        }

        internal static string RepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "BoscaliSummer.sln")) && Directory.Exists(Path.Combine(directory.FullName, "modules")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("repo root not found");
        }

        /// <summary>The file without // comments, so a comment cannot hide or fake a tap.</summary>
        internal static string Code(string relative)
        {
            string text = File.ReadAllText(Path.Combine(RepoRoot(), relative));
            return Regex.Replace(text, @"//[^\n]*", "");
        }

        private static void EveryCpuTapGoesThroughTheFrame()
        {
            string code = Code("modules/Weather/Visuals/CloudBodies.cs");
            int taps = 0;
            foreach (Match m in Regex.Matches(code, @"(?<![\w.])Sample\("))
            {
                string before = code.Substring(Math.Max(0, m.Index - 6), Math.Min(6, m.Index));
                if (before == "float ") continue; // the definition of the tap itself
                string argument = code.Substring(m.Index + m.Length, 24);
                TestAssert.That(Regex.IsMatch(argument, @"^-?[a-z]{0,2}n[xz]\b"), "a tap reads a noise-space coordinate, not a raw x or z: Sample(" + argument.Trim());
                taps++;
            }
            TestAssert.That(taps >= 15, "the scan saw the taps of CloudBodies: " + taps); // round 5: the thin-layer cell and streak taps are smooth value noise now (no texture)
            // The spec's grep: no raw x / scale tap anywhere in the CPU twin.
            TestAssert.That(!Regex.IsMatch(code, @"Sample\(\s*-?(x|z|px|pz|wx|wz|a|b)\s*/"), "no tap divides a raw coordinate by a scale");
        }

        private static WeatherPoint Cloudy() => new WeatherPoint { BackgroundCover = 0.7f, CloudBase = 1500f, LowTop = 3200f, CloudTop = 3200f, CellShape = 0.5f, RainRate = 0f };

        private static SkyParams Humilis()
        {
            SkyParams sky = SkyLibrary.Nominal(7);
            return sky;
        }

        private static void AFrameWithoutAKeyChangesNothing()
        {
            byte[] noise = CloudNoise3D.Generate(64, 47);
            SkyParams sky = Humilis();
            var plain = new CloudBodies(noise, 64, TapeSampler.Constant(sky), new TapeFrame(40f, 0f), nearDetail: true);
            var keyed = new CloudBodies(noise, 64, TapeSampler.Constant(sky), new TapeFrame(40f, 0f, noise: NoiseXform.Identity), nearDetail: true);
            for (int i = 0; i < 80; i++)
            {
                float x = i * 931f - 40000f, z = 25000f - i * 617f, y = 1500f + i * 11f;
                TestAssert.That(plain.Density(Cloudy(), x, y, z, 0f) == keyed.Density(Cloudy(), x, y, z, 0f), "the identity noise leaves every density alone");
            }
        }

        private static void EachTapeGetsItsOwnClouds()
        {
            byte[] noise = CloudNoise3D.Generate(64, 47);
            SkyParams sky = Humilis();
            var a = new CloudBodies(noise, 64, TapeSampler.Constant(sky), new TapeFrame(40f, 0f, noise: new NoiseXform(101u)), nearDetail: true);
            var b = new CloudBodies(noise, 64, TapeSampler.Constant(sky), new TapeFrame(40f, 0f, noise: new NoiseXform(202u)), nearDetail: true);
            var same = new CloudBodies(noise, 64, TapeSampler.Constant(sky), new TapeFrame(40f, 0f, noise: new NoiseXform(101u)), nearDetail: true);
            int differs = 0, cloudyA = 0, cloudyB = 0, n = 0;
            for (int i = 0; i < 400; i++)
            {
                float x = (i % 20) * 1900f - 19000f, z = (i / 20) * 1700f - 17000f, y = 1700f;
                float da = a.Density(Cloudy(), x, y, z, 0f), db = b.Density(Cloudy(), x, y, z, 0f);
                TestAssert.That(da == same.Density(Cloudy(), x, y, z, 0f), "the same layout seed gives the same clouds");
                if (Math.Abs(da - db) > 0.05f) differs++;
                if (da > 0.05f) cloudyA++;
                if (db > 0.05f) cloudyB++;
                n++;
            }
            TestAssert.That(differs > n * 0.3f, "two tapes do not share their cloud bodies: " + differs + " of " + n + " points differ");
            TestAssert.That(Math.Abs(cloudyA - cloudyB) < n * 0.25f && cloudyA > 0 && cloudyB > 0, "but they hold about as much cloud: " + cloudyA + " and " + cloudyB);
        }

        /// <summary>The spec's grep, on the production shader: every tex3Dlod of the noise volume starts from a noise-space position (ng, ngw,
        /// np, nsw, nw2, nlow, nwg, nhp, n1, n2: all NoiseXZ results), never from a raw g, wg, hp, p or w.</summary>
        private static void EveryShaderTapGoesThroughNoiseXZ()
        {
            string code = Code("modules/Weather/Assets/Source/FlightCloud.shader");
            int taps = 0;
            foreach (Match m in Regex.Matches(code, @"tex3Dlod\(\s*_CloudNoiseTex\s*,\s*float4\("))
            {
                string argument = code.Substring(m.Index + m.Length, 24);
                TestAssert.That(Regex.IsMatch(argument, @"^\s*(float2\(\s*-?)?n[a-z0-9]*\b"), "a shader tap reads a noise-space position: tex3Dlod(.. float4(" + argument.Trim());
                taps++;
            }
            TestAssert.That(taps >= 16, "the scan saw the taps of FlightCloud.shader: " + taps); // round 5: see the CPU scan
            TestAssert.That(!Regex.IsMatch(code, @"tex3Dlod\(\s*_CloudNoiseTex\s*,\s*float4\(\s*(float2\()?-?(g|wg|hp|p|w|sw|w2|world)\b"), "no tap starts from a raw position");
            TestAssert.That(code.Contains("NOISE_MID_ALIGNED") && code.Contains("NOISE_HIGH_ALIGNED") && code.Contains("NOISE_HORIZON"),
                "the streak taps and the horizon deck have their own noise layers");
        }

        private static void StreaksStayAlongTheWind()
        {
            // The streak taps of a thin layer must keep their stretch along the wind, so they read the noise through the aligned layers
            // (offset only, never rotated). Structural: the CPU twin and the production shader both name them.
            string cpu = Code("modules/Weather/Visuals/CloudBodies.cs");
            TestAssert.That(cpu.Contains("NoiseXform.MidAligned") && cpu.Contains("NoiseXform.HighAligned"), "the CPU streak taps use the aligned noise layers");
            // A layer with Cover 0.5 and streaks only still changes its picture with the tape (the offset), while the stretch axis is the wind's.
            byte[] noise = CloudNoise3D.Generate(64, 47);
            SkyLayer streaks = SkyArchetypes.Layer(LayerKind.CirrusFibratus).ToLayer(0.5f);
            streaks.Strength = 0.12f;
            var sky = new SkyParams { High = streaks, OpacityHigh = 0.15f };
            var a = new CloudBodies(noise, 64, TapeSampler.Constant(sky), new TapeFrame(0f, 0f, noise: new NoiseXform(11u)), 180f);
            var b = new CloudBodies(noise, 64, TapeSampler.Constant(sky), new TapeFrame(0f, 0f, noise: new NoiseXform(12u)), 180f);
            int differs = 0;
            for (int i = 0; i < 100; i++)
            {
                float x = (i % 10) * 6100f, z = (i / 10) * 5300f, y = streaks.Base + 250f;
                if (Math.Abs(a.Slab2D(streaks, x, y, z, 1) - b.Slab2D(streaks, x, y, z, 1)) > 0.02f) differs++;
            }
            TestAssert.That(differs > 20, "two tapes do not share their thin-layer clouds: " + differs + " of 100 points differ");
        }
    }
}
