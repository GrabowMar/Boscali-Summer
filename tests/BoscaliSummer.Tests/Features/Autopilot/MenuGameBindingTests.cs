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
        private static readonly string[] Prefixes = { "weapons/select/st", "weapons/cargo/st", "comms/c", "support/perks/", "support/pit/station/", "targets/filters/", "wing/shape/", "hud/page1/", "hud/page2/", "hud/page3/", "hud/page4/", "hud/parts/", "cockpit/feel/" };

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
            // a switch row is bound through a table keyed by its slug: every slug the catalog lists must be a key there
            foreach (MenuSwitch s in MenuCatalog.BoardParts) TestAssert.That(source.Contains("[\"parts/" + s.Slug + "\"]"), "MenuGame has no option behind the board part " + s.Slug);
            foreach (MenuSwitch s in MenuCatalog.FeelSwitches) TestAssert.That(source.Contains("[\"" + s.Slug + "\"]"), "MenuGame has no option behind the cabin effect " + s.Slug);
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
