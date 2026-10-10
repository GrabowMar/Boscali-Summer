using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BoscaliSummer.Modules.Immersion.Domain;

namespace BoscaliSummer.Tests.Features.Immersion
{
    internal static class KneeboardCardTests
    {
        private static KeyValuePair<string, int> W(string name, int count) => new KeyValuePair<string, int>(name, count);

        public static void Run()
        {
            string[] card = KneeboardCard.Lines("Viper [KR-67 Ifrit]", null,
                new[] { W("AIM-9", 2), W("AGM", 4), W("AIM-9", 2) }, "Maris Airport", "Free Flight", 13.5f);
            TestAssert.That(card[0] == "MISSION CARD", "The card opens with its title.");
            string all = string.Join("\n", card);
            TestAssert.That(all.Contains("VIPER") && all.Contains("KR-67 IFRIT") && all.Contains("MARIS AIRPORT") && all.Contains("FREE FLIGHT"),
                "Callsign and airframe come from the unit name; text is upper case.");
            TestAssert.That(all.Contains("13:30 L"), "Local time is HH:MM L.");
            int agm = Array.FindIndex(card, l => l.Contains("4x AGM")), aim = Array.FindIndex(card, l => l.Contains("4x AIM-9"));
            TestAssert.That(agm > 0 && aim > agm, "Loadout sums duplicate stations and sorts by count, then name.");

            var many = new List<KeyValuePair<string, int>>();
            for (int i = 0; i < 9; i++) many.Add(W("WPN-" + i, 9 - i));
            string[] crowded = KneeboardCard.Lines("Lancer", "FS-20 Vortex", many, "Home", "Strike", 6f);
            TestAssert.That(Array.Exists(crowded, l => l.Contains("+4 MORE")) && !Array.Exists(crowded, l => l.Contains("WPN-6")),
                "More than six weapons show five rows and a remainder.");
            TestAssert.That(crowded.Length <= KneeboardCard.MaxLines, "The card never exceeds its line budget.");

            string[] empty = KneeboardCard.Lines(null, null, null, null, null, float.NaN);
            TestAssert.That(empty[0] == "MISSION CARD" && Array.Exists(empty, l => l.Contains("--:--")) &&
                Array.Exists(empty, l => l.EndsWith(" -", StringComparison.Ordinal)), "Missing data reads as dashes, never throws.");

            string[] wide = KneeboardCard.Lines("A Very Long Callsign Indeed Here [An Extremely Long Airframe Name]", null,
                new[] { W("An Absurdly Long Weapon Designation", 12) }, "A Base With A Very Long Name", "Operation Long Name", 23.99f);
            foreach (string[] lines in new[] { card, crowded, empty, wide })
                foreach (string line in lines)
                    TestAssert.That(line.Length <= KneeboardCard.Width, "Every line fits the card: " + line);
            TestAssert.That(Array.Exists(wide, l => l.EndsWith("...", StringComparison.Ordinal)) && Array.Exists(wide, l => l.Contains("23:59 L")),
                "Overlong text is cut with an ellipsis; the clock never rolls to 24:00.");

            TestAssert.That(KneeboardCard.Wrap(-1, 12) == 11 && KneeboardCard.Wrap(12, 12) == 0 && KneeboardCard.Wrap(5, 12) == 5 &&
                KneeboardCard.Wrap(3, 0) == 0, "The card picker wraps both ways and survives an empty gallery.");

            string root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "BoscaliSummer.sln"))) root = Path.GetDirectoryName(root);
            string[] listed = Regex.Matches(File.ReadAllText(Path.Combine(root, "modules/Immersion/Visuals/KneeboardGallery.cs")),
                "\\(\"[^\"]+\", \"([a-z0-9-]+)\"\\)").Cast<Match>().Select(m => m.Groups[1].Value).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            string[] shipped = Directory.GetFiles(Path.Combine(root, "modules/Immersion/Assets/Kneeboard"), "*.png")
                .Select(Path.GetFileNameWithoutExtension).OrderBy(s => s, StringComparer.Ordinal).ToArray();
            TestAssert.That(listed.Length > 0 && listed.SequenceEqual(shipped),
                "Every picture card the gallery lists ships as a PNG and no PNG is orphaned: listed " + listed.Length + ", shipped " + shipped.Length);

            var schedule = new KneeboardSchedule();
            TestAssert.That(schedule.BeginBind() && !schedule.BeginBind(), "A bind is attempted once per cockpit entry, never every frame.");
            schedule.Bound(101);
            TestAssert.That(schedule.TakeRender() && !schedule.TakeRender(), "A new aircraft's card renders once.");
            schedule.Recorded(45f);
            TestAssert.That(schedule.UseCamera, "The first render pays the text system's one-off start-up and never latches the fallback.");
            schedule.Detach();
            TestAssert.That(schedule.BeginBind(), "Re-entering the cockpit may bind again.");
            schedule.Bound(101);
            TestAssert.That(!schedule.TakeRender(), "Re-entering the same aircraft's cockpit keeps the card; no re-render.");
            schedule.MarkDirty();
            TestAssert.That(schedule.TakeRender(), "Rearm or loadout change re-renders the card.");
            schedule.Recorded(1.2f);
            TestAssert.That(schedule.UseCamera, "A warm render within 2 ms keeps the camera path.");
            schedule.MarkDirty(); schedule.TakeRender(); schedule.Recorded(3f);
            TestAssert.That(!schedule.UseCamera, "A warm render over 2 ms switches later cards off the camera.");
            schedule.Detach(); schedule.BeginBind(); schedule.Bound(202);
            TestAssert.That(schedule.TakeRender() && schedule.UseCamera, "A different aircraft gets a fresh card and a fresh budget.");
            schedule.Reset();
            TestAssert.That(schedule.BeginBind() && !schedule.TakeRender(), "Reset forgets the aircraft and pending renders.");
        }
    }
}
