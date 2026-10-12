using System;
using System.IO;
using System.Text.RegularExpressions;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The golden vector of spec 9.1: the tapes of twenty fixed keys hash to the constant that sits next to <c>WeatherNet.ProtocolVersion</c>. Editing a sky or a story table
    /// fails this test until someone updates <c>TapeGoldenVector</c> there and bumps the protocol at release.</summary>
    internal static class TapeGoldenTests
    {
        public static void Run()
        {
            string net = File.ReadAllText(Path.Combine(FindRepoRoot(), "modules", "Weather", "Networking", "WeatherNet.cs"));
            Match m = Regex.Match(net, @"TapeGoldenVector\s*=\s*0x([0-9A-Fa-f]{8})u");
            TestAssert.That(m.Success, "WeatherNet.cs declares TapeGoldenVector next to ProtocolVersion");
            uint pinned = Convert.ToUInt32(m.Groups[1].Value, 16), actual = TapeGolden.Compute();
            TestAssert.That(pinned == actual, "the tape tables changed: set WeatherNet.TapeGoldenVector = 0x" + actual.ToString("X8") + "u and bump ProtocolVersion at release (pinned 0x" + pinned.ToString("X8") + ")");
            TestAssert.That(actual == TapeGolden.Compute(), "the vector is stable between two computations");
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "BoscaliSummer.sln")) && Directory.Exists(Path.Combine(directory.FullName, "modules"))) return directory.FullName;
                directory = directory.Parent;
            }
            throw new InvalidOperationException("repository root not found");
        }
    }
}
