using System;
using System.IO;
using System.Text.RegularExpressions;

namespace BoscaliSummer.Tests.Avionics
{
    /// <summary>The baked manifest is the contract P1's AvIconTable and AvType read; check it offline.</summary>
    internal static class AvBundleManifestTests
    {
        public static void Run()
        {
            string path = Path.Combine(RepoRoot(), "AvionicsUi", "Assets", "avionics-ui.manifest.json");
            if (!File.Exists(path)) { Console.WriteLine("SKIP AvBundleManifestTests: no baked manifest"); return; }
            string json = File.ReadAllText(path);

            foreach (string font in new[] { "NOA Barlow Condensed Medium SDF", "NOA Barlow Condensed SemiBold SDF",
                         "NOA JetBrains Mono Regular SDF", "NOA JetBrains Mono Bold SDF", "NOA Icons SDF" })
                TestAssert.That(json.Contains("\"" + font + "\""), "manifest lists font " + font);
            foreach (string shader in new[] { "NOA/UI/Fx", "NOA/UI/Glass", "NOA/UI/Blur" })
                TestAssert.That(json.Contains("\"" + shader + "\""), "manifest lists shader " + shader);

            string iconList = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "AvionicsUiAssets", "icons.txt"));
            foreach (string raw in iconList.Split('\n'))
            {
                string name = raw.Trim();
                if (name.Length == 0 || name[0] == '#') continue;
                TestAssert.That(Regex.IsMatch(json, "\"" + Regex.Escape(name) + "\": \"[0-9a-f]{4}\""), "icon baked: " + name);
            }

            TestAssert.That(Missing(json, "NOA Icons SDF").Length == 0, "every listed icon glyph is in the icon atlas");
            // Condensed faces may lack a symbol; the mono face is their fallback and must have it.
            string monoMissing = " " + Missing(json, "NOA JetBrains Mono Regular SDF") + " ";
            foreach (string cp in Missing(json, "NOA Barlow Condensed Medium SDF").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                TestAssert.That(!monoMissing.Contains(" " + cp + " "), "glyph U+" + cp + " missing from Barlow is covered by JetBrains Mono");
        }

        private static string Missing(string json, string font)
        {
            Match m = Regex.Match(json, "\"" + Regex.Escape(font) + "\": \"([0-9A-F ]*)\"");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        private static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "BoscaliSummer.csproj"))) dir = Path.GetDirectoryName(dir);
            return dir ?? throw new InvalidOperationException("repo root not found");
        }
    }
}
