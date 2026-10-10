using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Modules.Autopilot.Domain;

namespace BoscaliSummer.Tests.Features.Autopilot
{
    /// <summary>
    /// The pure catalog names every row by a stable id; <c>MenuGame.Bind</c> (game code, not compilable offline) attaches the game call by that id.
    /// This reads the Runtime source and fails when a runnable row of any scenario has no binding, so a renamed or new row cannot silently become a dead button.
    /// </summary>
    internal static class MenuGameBindingTests
    {
        private static readonly string[] Prefixes = { "weapons/select/st", "comms/c", "support/perks/", "support/pit/station/", "targets/filters/", "wing/shape/" };

        public static void Run()
        {
            string runtime = Path.Combine(FindRepoRoot(), "modules", "Autopilot", "Runtime");
            string source = "";
            foreach (string file in Directory.GetFiles(runtime, "MenuGame*.cs")) source += File.ReadAllText(file);
            TestAssert.That(source.Length > 0, "MenuGame sources not found under " + runtime);

            var runnable = new HashSet<string>();
            foreach (var scenario in MenuWorlds.All())
                foreach (MenuRow row in MenuCatalogTests.AllRows(MenuCatalog.Build(scenario.Value)))
                    if (row.Kind != MenuKind.Submenu) runnable.Add(row.Id);
            TestAssert.That(runnable.Count > 40, "the fixtures cover the whole catalog (" + runnable.Count + " runnable rows)");
            foreach (string id in runnable)
            {
                string prefix = Array.Find(Prefixes, p => id.StartsWith(p, StringComparison.Ordinal));
                string literal = "\"" + (prefix ?? id) + "\"";
                TestAssert.That(source.Contains(literal), "MenuGame does not bind row " + id + " (looked for " + literal + ")");
            }
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "BoscaliSummer.sln")) && Directory.Exists(Path.Combine(directory.FullName, "modules")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the repo root (BoscaliSummer.sln + modules/) from test output.");
        }
    }
}
