using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NOAvionics;

namespace BoscaliSummer.Tests.Avionics
{
    internal static class AvBundleManifestTests
    {
        public static void Run()
        {
            string path = Path.Combine(RepoRoot(), "AvionicsUi", "Assets", "avionics-ui.manifest.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement manifest = document.RootElement;
            var fonts = manifest.GetProperty("fonts").EnumerateArray().Select(font => font.GetString()).ToArray();
            foreach (AvFace face in Enum.GetValues<AvFace>())
                TestAssert.That(fonts.Contains(AvTypeScale.AssetName(face)), "manifest contains runtime font " + face);
            var shaders = manifest.GetProperty("shaders").EnumerateArray().Select(shader => shader.GetString()).ToArray();
            foreach (string shader in new[] { "NOA/UI/Fx", "NOA/UI/Glass", "NOA/UI/Blur" })
                TestAssert.That(shaders.Contains(shader), "manifest lists shader " + shader);
            JsonElement icons = manifest.GetProperty("icons");
            TestAssert.That(icons.EnumerateObject().Count() == AvIconTable.Count, "manifest and runtime icon table have identical counts");
            foreach (AvIcon icon in Enum.GetValues<AvIcon>())
            {
                if (icon == AvIcon.None) continue;
                string name = AvIconTable.TablerName(icon);
                TestAssert.That(icons.GetProperty(name).GetString() == ((int)AvIconTable.Char(icon)).ToString("x4"),
                    "baked glyph matches runtime icon " + name);
            }
            JsonElement missing = manifest.GetProperty("missing");
            TestAssert.That(Missing(missing, AvFace.Icons).Length == 0, "every icon glyph is present in its atlas");
            foreach (var faces in new[] { (AvFace.Cond, AvFace.Mono), (AvFace.CondStrong, AvFace.MonoStrong) })
            {
                var monoMissing = Missing(missing, faces.Item2);
                foreach (string codepoint in Missing(missing, faces.Item1))
                    TestAssert.That(!monoMissing.Contains(codepoint), "fallback covers missing glyph U+" + codepoint);
            }
        }

        private static string[] Missing(JsonElement missing, AvFace face) =>
            missing.GetProperty(AvTypeScale.AssetName(face)).GetString().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        private static string RepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "BoscaliSummer.csproj"))) dir = Path.GetDirectoryName(dir);
            return dir ?? throw new InvalidOperationException("repo root not found");
        }
    }
}
