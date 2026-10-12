using System;
using System.IO;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The Met Office replaced the old console: nothing may still name it, and the manager must build the new window (and nothing else) on Ctrl+O.</summary>
    internal static class WeatherConsoleScanTests
    {
        private const string OldWindow = "WeatherConsole" + "Window";

        public static void Run()
        {
            string root = FindRepoRoot();
            foreach (string folder in new[] { "modules", "Core", "tests", "AvionicsUi" })
                foreach (string file in Directory.GetFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
                {
                    if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
                    TestAssert.That(!File.ReadAllText(file).Contains(OldWindow), Path.GetFileName(file) + " still names the deleted console window");
                }
            TestAssert.That(!File.Exists(Path.Combine(root, "modules", "Weather", "Presentation", OldWindow + ".cs")), "the old console file is deleted, not kept beside the new one");
            string manager = File.ReadAllText(Path.Combine(root, "modules", "Weather", "Runtime", "WeatherManager.cs"));
            TestAssert.That(manager.Contains("MetOfficeWindow.Create(transform, adapter, adapter)"), "Ctrl+O builds the Met Office over one adapter (read and write seams share the host log)");
            TestAssert.That(manager.Contains("private MetOfficeWindow console;"), "the manager owns the Met Office window");
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
