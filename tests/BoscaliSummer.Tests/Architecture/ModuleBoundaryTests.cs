using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BoscaliSummer.Tests.Architecture
{
    internal static class ModuleBoundaryTests
    {
        private static readonly Regex FeatureImport = new Regex(
            @"BoscaliSummer\.Features\.(?<feature>[A-Za-z0-9_]+)",
            RegexOptions.CultureInvariant);
        private static readonly Regex FireWireImplementation = new Regex(
            @"\b(ModNet|FireIgnitedMessage|RuinCreatedMessage)\b",
            RegexOptions.CultureInvariant);

        public static void Run()
        {
            string sourceRoot = FindRepoRoot();
            string featuresRoot = Path.Combine(sourceRoot, "modules");

            foreach (string featurePath in Directory.GetDirectories(featuresRoot))
            {
                string[] sourceFiles = Directory.GetFiles(featurePath, "*.cs", SearchOption.AllDirectories);
                if (sourceFiles.Length == 0) continue;
                string featureName = Path.GetFileName(featurePath);
                TestAssert.That(File.Exists(Path.Combine(featurePath, featureName + "Feature.cs")),
                    featureName + " is missing its explicit IModFeature descriptor");
                TestAssert.That(File.Exists(Path.Combine(featurePath, "AGENTS.md")),
                    featureName + " is missing its local AGENTS.md invariant doc");

                foreach (string file in sourceFiles)
                {
                    string source = File.ReadAllText(file);
                    foreach (Match match in FeatureImport.Matches(source))
                        TestAssert.That(match.Groups["feature"].Value == featureName,
                            Relative(sourceRoot, file) + " imports sibling feature " +
                            match.Groups["feature"].Value);
                    if (featureName != "FireAndDestruction")
                    {
                        TestAssert.That(!source.Contains("BoscaliSummer.Fire"),
                            Relative(sourceRoot, file) + " imports Fire implementation namespace");
                        TestAssert.That(!FireWireImplementation.IsMatch(source),
                            Relative(sourceRoot, file) + " references Fire networking implementation");
                    }
                    if (featureName != "UrbanCombat")
                        TestAssert.That(!source.Contains("BoscaliSummer.Garrisons"),
                            Relative(sourceRoot, file) + " imports Urban Combat implementation namespace");
                }
            }

            VerifySharedArea(sourceRoot, "Framework");
            VerifySharedArea(sourceRoot, "Infrastructure");
            TestAssert.That(File.Exists(Path.Combine(featuresRoot, "Hud", "Runtime", "ThirdPersonHudController.cs")) &&
                !File.Exists(Path.Combine(featuresRoot, "Support", "Runtime", "ThirdPersonHudController.cs")),
                "local HUD ownership must remain in Hud, independent of gameplay modules");
            string oldNetworking = Path.Combine(sourceRoot, "Infrastructure", "Networking");
            TestAssert.That(!Directory.Exists(oldNetworking) || !Directory.EnumerateFiles(oldNetworking).Any(),
                "feature-owned networking leaked into shared Infrastructure/Networking");
            TestAssert.That(File.Exists(Path.Combine(featuresRoot, "FireAndDestruction", "Networking", "ModNet.cs")),
                "Fire and Destruction does not own its networking bridge");
            TestAssert.That(File.Exists(Path.Combine(featuresRoot, "Radio", "Runtime", "RadioLibrary.cs")),
                "Radio does not own its library scanner");

            VerifyKitIsolation(sourceRoot);
            VerifyMigratedConsoles(sourceRoot, MigratedConsoleFolders);
        }

        // ------------------------------------------------------------------ kit v2 (FUI program)

        /// <summary>Console folders rebuilt on kit v2 (P2 slices append here). They may not use v1 kit APIs or literals.</summary>
        internal static readonly string[] MigratedConsoleFolders = { };

        /// <summary>Data-visualisation files inside migrated folders that legitimately draw raw colours.</summary>
        private static readonly string[] DataVizAllowlist =
        {
            "modules/Command/Presentation/MapUi/MfdTerrainRelief.cs",
            "modules/Command/Presentation/MapUi/MfdMapDeck.cs",
            "modules/Support/Presentation/SupportTacticalIcons.cs",
            "modules/Progression/Presentation/EmblemRenderer.cs",
            "modules/Radio/Presentation/RadioWaterfall.cs",
        };

        private static readonly Regex KitReachesMod = new Regex(
            @"^\s*using\s+BoscaliSummer\b|\bBoscaliSummer\.[A-Z]", RegexOptions.CultureInvariant | RegexOptions.Multiline);

        private static readonly Regex V1KitOrLiteral = new Regex(
            @"\bAvKit\.|\bAvStyled\.|\bAvScreen\.|\bnew AvButton\b|\bAvButton\.|\bnew Color\(|\bfontSize\s*=|\bAvFont\.Font\s*=",
            RegexOptions.CultureInvariant);

        /// <summary>The shared kit is compiled into Wing Command too: it must never reach into Boscali.</summary>
        internal static void VerifyKitIsolation(string sourceRoot)
        {
            foreach (string area in new[] { "Avionics", "AvionicsUi" })
                foreach (string file in Directory.GetFiles(Path.Combine(sourceRoot, area), "*.cs", SearchOption.AllDirectories))
                {
                    if (file.EndsWith("Tests.cs", StringComparison.Ordinal)) continue;
                    string code = string.Join("\n", File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith("//")));
                    TestAssert.That(!KitReachesMod.IsMatch(code), Relative(sourceRoot, file) + " references BoscaliSummer from the shared kit");
                }
        }

        internal static void VerifyMigratedConsoles(string sourceRoot, string[] folders)
        {
            foreach (string folder in folders)
                foreach (string file in Directory.GetFiles(Path.Combine(sourceRoot, folder), "*.cs", SearchOption.AllDirectories))
                {
                    string rel = Relative(sourceRoot, file).Replace('\\', '/');
                    if (DataVizAllowlist.Contains(rel)) continue;
                    string code = string.Join("\n", File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith("//")));
                    Match m = V1KitOrLiteral.Match(code);
                    TestAssert.That(!m.Success, rel + " is a migrated console but uses '" + m.Value + "' (v1 kit or a literal colour/size)");
                }
        }

        private static void VerifySharedArea(string sourceRoot, string area)
        {
            foreach (string file in Directory.GetFiles(
                Path.Combine(sourceRoot, area), "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                TestAssert.That(!FeatureImport.IsMatch(source),
                    Relative(sourceRoot, file) + " imports a concrete feature");
                TestAssert.That(!source.Contains("BoscaliSummer.Fire") &&
                    !source.Contains("BoscaliSummer.Garrisons"),
                    Relative(sourceRoot, file) + " imports a legacy feature implementation namespace");
                TestAssert.That(!FireWireImplementation.IsMatch(source),
                    Relative(sourceRoot, file) + " contains Fire networking implementation");
            }
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "BoscaliSummer.sln")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "modules")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the repo root (BoscaliSummer.sln + modules/) from test output.");
        }

        private static string Relative(string root, string path) =>
            Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    }
}
