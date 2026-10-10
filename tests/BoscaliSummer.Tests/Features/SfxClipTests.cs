using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BoscaliSummer.Core.Util;

namespace BoscaliSummer.Tests.Features
{
    /// <summary>The recorded effect clips in Core/Fx/Sfx: well-formed, all used, and nothing asks for a clip that is not shipped.</summary>
    internal static class SfxClipTests
    {
        private const int Rate = 22050;

        public static void Run()
        {
            string root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "BoscaliSummer.sln"))) root = Path.GetDirectoryName(root);
            var clips = new HashSet<string>(Directory.GetFiles(Path.Combine(root, "Core/Fx/Sfx"), "*.wav").Select(Path.GetFileNameWithoutExtension));
            TestAssert.That(clips.Count > 0, "Effect clips ship.");
            foreach (string name in clips)
            {
                float[] a = PcmWav.Decode(File.ReadAllBytes(Path.Combine(root, "Core/Fx/Sfx", name + ".wav")), Rate);
                TestAssert.That(a != null, name + ".wav is mono 16-bit " + Rate + " Hz.");
                float seconds = a.Length / (float)Rate, peak = 0f;
                foreach (float v in a) peak = Math.Max(peak, Math.Abs(v));
                TestAssert.That(name == name.ToLowerInvariant() && seconds >= .15f && seconds <= 3.5f, name + " is lower case and 0.15-3.5 s, got " + seconds);
                TestAssert.That(peak > .5f && peak <= .75f, name + " is levelled to about -3 dBFS, peak " + peak);
                TestAssert.That(Math.Abs(a[0]) < .02f && Math.Abs(a[a.Length - 1]) < .02f, name + " starts and ends silent (no click).");
            }

            var unused = new HashSet<string>(clips);
            var requested = new Regex("SfxClips\\.Get\\(\"([^\"]+)\"\\)", RegexOptions.CultureInvariant);
            foreach (string area in new[] { "modules", "Core" })
                foreach (string file in Directory.GetFiles(Path.Combine(root, area), "*.cs", SearchOption.AllDirectories))
                {
                    string source = File.ReadAllText(file);
                    foreach (Match match in requested.Matches(source))
                        TestAssert.That(clips.Contains(match.Groups[1].Value), Path.GetFileName(file) + " asks for a clip that is not shipped: " + match.Groups[1].Value);
                    unused.RemoveWhere(name => source.Contains("\"" + name + "\""));
                }
            TestAssert.That(unused.Count == 0, "Every shipped clip is named somewhere in the code; unused: " + string.Join(", ", unused));
        }
    }
}
