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
            @"\b(ModNet|FireIgnitedMessage|BuildingDamagedMessage|RuinCreatedMessage)\b",
            RegexOptions.CultureInvariant);

        public static void Run()
        {
            string sourceRoot = FindSourceRoot();
            string featuresRoot = Path.Combine(sourceRoot, "Features");

            foreach (string featurePath in Directory.GetDirectories(featuresRoot))
            {
                string featureName = Path.GetFileName(featurePath);
                TestAssert.That(File.Exists(Path.Combine(featurePath, "AGENTS.md")),
                    featureName + " is missing its local AGENTS.md scope file");
                TestAssert.That(File.Exists(Path.Combine(featurePath, featureName + "Feature.cs")),
                    featureName + " is missing its explicit IModFeature descriptor");

                foreach (string file in Directory.GetFiles(featurePath, "*.cs", SearchOption.AllDirectories))
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
            string oldNetworking = Path.Combine(sourceRoot, "Infrastructure", "Networking");
            TestAssert.That(!Directory.Exists(oldNetworking) || !Directory.EnumerateFiles(oldNetworking).Any(),
                "feature-owned networking leaked into shared Infrastructure/Networking");
            TestAssert.That(File.Exists(Path.Combine(featuresRoot, "FireAndDestruction", "Networking", "ModNet.cs")),
                "Fire and Destruction does not own its networking bridge");
            TestAssert.That(File.Exists(Path.Combine(featuresRoot, "Radio", "Runtime", "RadioLibrary.cs")),
                "Radio does not own its library scanner");
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

        private static string FindSourceRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "src", "BoscaliSummer");
                if (Directory.Exists(Path.Combine(candidate, "Features"))) return candidate;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate src/BoscaliSummer from test output.");
        }

        private static string Relative(string root, string path) =>
            Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    }
}
