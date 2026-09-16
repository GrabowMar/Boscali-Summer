using System.Collections.Generic;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class MfdSecondaryObjectivesTests
    {
        public static void Run()
        {
            TestAssert.That(MfdSecondaryObjectives.PageCount(3, 1) == 3 && MfdSecondaryObjectives.ClampPage(9, 3, 1) == 2,
                "Compact MFDs page one readable card at a time");
            TestAssert.That(MfdSecondaryObjectives.PlainObjective("_BDF_PUSH - <color=#B2B2B2>Capture\n airstrip</color>") == "BDF PUSH - Capture airstrip",
                "Markup is removed before rendering and internal separators become readable spaces");
            TestAssert.That(MfdSecondaryObjectives.PlainObjective("<color=#B2B2") == "" && MfdSecondaryObjectives.PlainObjective(null) == "OBJECTIVE",
                "Incomplete markup never leaks into objective labels");
            TestAssert.That(MfdSecondaryObjectives.PageCount(0, 3) == 1 &&
                MfdSecondaryObjectives.PageCount(3, 3) == 1 &&
                MfdSecondaryObjectives.PageCount(7, 3) == 3, "A page count follows the row count the panel could build");
            TestAssert.That(MfdSecondaryObjectives.ClampPage(1, 5, 2) == 1 &&
                MfdSecondaryObjectives.ClampPage(2, 3, 2) == 1 &&
                MfdSecondaryObjectives.ClampPage(-1, 3, 2) == 0 &&
                MfdSecondaryObjectives.ClampPage(3, 3, 2) == 1,
                "Secondary page survives refresh and clamps when the objective list shrinks");
            TestAssert.That(MfdSecondaryObjectives.TimeLabel(false, 60.1f) == "LEFT 01:01" &&
                MfdSecondaryObjectives.TimeLabel(false, 0.1f) == "LEFT 00:01",
                "Remaining time rounds upward until the host deadline");
            TestAssert.That(MfdSecondaryObjectives.TimeLabel(false, 0f) == "ENDED" &&
                MfdSecondaryObjectives.TimeLabel(true, 100f) == "COMPLETE",
                "Only authoritative completion produces the completed timer state");
            TestAssert.That(MfdSecondaryObjectives.TimeLabel(false, float.NaN) == "TIME UNKNOWN" &&
                MfdSecondaryObjectives.TimeLabel(false, float.PositiveInfinity) == "TIME UNKNOWN",
                "Missing deadline data never claims expiration");

            RunBoardLayout();
            RunBoardCopy();
        }

        private static void RunBoardLayout()
        {
            TestAssert.That(MfdSecondaryObjectives.RowsFor(464f) == 1 &&
                MfdSecondaryObjectives.RowsFor(764f) == 3 &&
                MfdSecondaryObjectives.RowsFor(2000f) == MfdSecondaryObjectives.MaxCards,
                "The board fills a tall bezel and never builds more rows than it can show");
            TestAssert.That(MfdSecondaryObjectives.RowsFor(0f) == 1,
                "A body measured at zero still yields one dossier instead of an empty page");
            TestAssert.That(MfdSecondaryObjectives.CardHeightFor(764f, 3) == MfdSecondaryObjectives.MaxCardHeight &&
                MfdSecondaryObjectives.CardHeightFor(340f, 1) == 200f &&
                MfdSecondaryObjectives.CardHeightFor(764f, 1) == MfdSecondaryObjectives.MaxCardHeight,
                "A dossier grows into slack but never past its readable maximum");
            for (float body = 460f; body <= 900f; body += 17f)
            {
                int rows = MfdSecondaryObjectives.RowsFor(body);
                float height = MfdSecondaryObjectives.CardHeightFor(body, rows);
                float available = body - 140f;
                TestAssert.That(height >= MfdSecondaryObjectives.MinCardHeight &&
                    height <= MfdSecondaryObjectives.MaxCardHeight &&
                    (rows - 1) * MfdSecondaryObjectives.RowPitch + height <= available,
                    "No dossier grid overflows its body at height " + body);
            }

            var clockEntries = new List<SecondaryObjectiveView>
            {
                Card(4, 600f, offered: true), Card(2, 30f, offered: true), Card(9, float.NaN, offered: true)
            };
            MfdSecondaryObjectives.SortForDisplay(clockEntries, MfdSecondaryObjectives.FilterAvailable);
            TestAssert.That(clockEntries[0].Id == 2 && clockEntries[1].Id == 4 && clockEntries[2].Id == 9,
                "Offers read soonest-deadline-first and an unknown clock never jumps the queue");

            var closedEntries = new List<SecondaryObjectiveView> { Card(3, 0f), Card(11, 0f), Card(7, 0f) };
            MfdSecondaryObjectives.SortForDisplay(closedEntries, MfdSecondaryObjectives.FilterResults);
            TestAssert.That(closedEntries[0].Id == 11 && closedEntries[2].Id == 3,
                "Closed work reads newest-first");
            MfdSecondaryObjectives.SortForDisplay(closedEntries, MfdSecondaryObjectives.FilterActive);
            TestAssert.That(closedEntries.Count == 3, "Sorting never drops a dossier");
        }

        private static void RunBoardCopy()
        {
            TestAssert.That(MfdSecondaryObjectives.ChipLabel(Card(1, 300f, offered: true)) == "OFFER 05:00" &&
                MfdSecondaryObjectives.ChipLabel(Card(2, 1200f, active: true)) == "LEFT 20:00",
                "The chip names the phase the clock belongs to");
            TestAssert.That(MfdSecondaryObjectives.ChipLabel(Card(3, 0f, offered: true)) == "OFFER ENDED" &&
                MfdSecondaryObjectives.ChipLabel(Card(4, 0f, active: true)) == "TIME ENDED" &&
                MfdSecondaryObjectives.ChipLabel(Card(5, 0f)) == "CLOSED",
                "A lapsed clock says which phase ended rather than a bare zero");
            TestAssert.That(MfdSecondaryObjectives.ChipLabel(Card(6, 0f, complete: true)) == "PAID" &&
                MfdSecondaryObjectives.ChipLabel(Card(7, float.NaN, active: true)) == "TIME UNKNOWN" &&
                MfdSecondaryObjectives.ChipLabel(null) == "LINK LOST",
                "Completed, unknown and lost contracts are all named");

            string paid = MfdSecondaryObjectives.PayoutLabel(Card(1, 0f, complete: true));
            string closed = MfdSecondaryObjectives.PayoutLabel(Card(3, 0f));
            TestAssert.That(paid.StartsWith("PAID  $") && paid.EndsWith("+   125 XP") &&
                MfdSecondaryObjectives.PayoutLabel(Card(2, 300f, offered: true)).StartsWith("$") &&
                closed.StartsWith("UNPAID  $") && closed.EndsWith("+   125 XP"),
                "Pay is only reported as collected when the host reported completion");

            TestAssert.That(MfdSecondaryObjectives.BoardSummary(3, 1, 2, 4) == "3 OFFERS  ·  1/2 ACTIVE  ·  4 CLOSED" &&
                MfdSecondaryObjectives.ShortCount(1, 0) == "1",
                "A host that reports no ceiling yields a plain count");
            TestAssert.That(!string.IsNullOrEmpty(MfdSecondaryObjectives.EmptyMessage(0)) &&
                !string.IsNullOrEmpty(MfdSecondaryObjectives.EmptyMessage(1)) &&
                !string.IsNullOrEmpty(MfdSecondaryObjectives.EmptyMessage(2)),
                "Every filter has an empty state that explains the host cycle");
        }

        private static SecondaryObjectiveView Card(int id, float seconds,
            bool offered = false, bool active = false, bool complete = false) =>
            new SecondaryObjectiveView(id, "COVER THE SUPPLY RUN", "brief", "HLT Munitions Truck",
                active ? "RETURN TO BASE" : "OFFERED", "Faction morale +3", complete ? 1f : 0f, seconds,
                1400, 125, complete, isOffered: offered, isActive: active, hasMarker: active,
                x: 100f, z: -200f, radius: 1500f);
    }
}
