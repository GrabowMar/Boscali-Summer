using System.Collections.Generic;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using BoscaliSummer.Features.DynamicOperations.Domain;
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
            TestAssert.That(MfdSecondaryObjectives.Humanize("ThreatenPALADsrtWest") == "Threaten PALA Dsrt West" &&
                MfdSecondaryObjectives.Humanize("SinkPALACarrierWithNotification") == "Sink PALA Carrier With Notification" &&
                MfdSecondaryObjectives.Humanize("DestroyDsrtDepots") == "Destroy Dsrt Depots" &&
                MfdSecondaryObjectives.Humanize("Capture Airstrip") == "Capture Airstrip",
                "CamelCase and acronym boundaries are humanized for player display");
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
            TestAssert.That(MfdSecondaryObjectives.MaxCards == OperationBoard.MaximumCards,
                "The board never builds more dossiers than the host's own board can issue");
            TestAssert.That(MfdSecondaryObjectives.CardHeightFor(764f, 3) == MfdSecondaryObjectives.RowPitch &&
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
                TestAssert.That(rows < 2 || height <= MfdSecondaryObjectives.RowPitch,
                    "A multi-row dossier never paints over the card above it at height " + body);
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
                MfdSecondaryObjectives.ChipLabel(Card(2, 1200f, active: true)) == "T-20:00" &&
                MfdSecondaryObjectives.ChipLabel(Card(8, 45f, active: true)) == "T-45s",
                "Offers keep the board's phase wording; accepted work uses the cockpit clock");
            TestAssert.That(MfdSecondaryObjectives.ChipLabel(Card(3, 0f, offered: true)) == "OFFER ENDED" &&
                MfdSecondaryObjectives.ChipLabel(Card(4, 0f, active: true)) == "TIME ENDED" &&
                MfdSecondaryObjectives.ChipLabel(Card(5, 0f)) == "CLOSED",
                "A lapsed clock says which phase ended rather than a bare zero");
            TestAssert.That(MfdSecondaryObjectives.ChipLabel(Card(6, 0f, complete: true)) == "PAID" &&
                MfdSecondaryObjectives.ChipLabel(Card(7, float.NaN, active: true)) == "TIME UNKNOWN" &&
                MfdSecondaryObjectives.ChipLabel(null) == "LINK LOST",
                "Completed, unknown and lost contracts are all named");

            TestAssert.That(MfdSecondaryObjectives.TitleLine(5, "SURVEY THE AFTERMATH") ==
                OperationMarkerCopy.Title(5, "SURVEY THE AFTERMATH") &&
                MfdSecondaryObjectives.TitleLine(5, null) == OperationMarkerCopy.Title(5, null),
                "The numbered title line matches the cockpit marker's own rule");
            foreach (float seconds in new[] { 45f, 59.5f, 60f, 299.4f, 1200f })
                TestAssert.That(MfdSecondaryObjectives.Countdown(seconds) == OperationMarkerCopy.Clock(seconds),
                    "The accepted clock matches the HUD at " + seconds + "s");
            TestAssert.That(MfdSecondaryObjectives.UrgentSeconds == OperationMarkerCopy.UrgentSeconds,
                "The board and the cockpit agree on when a contract reads urgent");

            SecondaryObjectiveView live = Card(1, 300f, offered: true);
            SecondaryObjectiveView lapsed = Card(2, 0f, offered: true);
            SecondaryObjectiveView unknown = Card(3, float.NaN, offered: true);
            TestAssert.That(MfdSecondaryObjectives.CanAccept(live, true) &&
                MfdSecondaryObjectives.AcceptLabel(live, true) == "ACCEPT CONTRACT",
                "A live offer with a free roster accepts");
            TestAssert.That(!MfdSecondaryObjectives.CanAccept(live, false) &&
                MfdSecondaryObjectives.AcceptLabel(live, false) == "ACTIVE LIMIT REACHED",
                "A full roster disables accept with the ceiling named");
            TestAssert.That(!MfdSecondaryObjectives.CanAccept(lapsed, true) &&
                MfdSecondaryObjectives.AcceptLabel(lapsed, true) == "OFFER ENDED" &&
                !MfdSecondaryObjectives.CanAccept(unknown, true) &&
                MfdSecondaryObjectives.AcceptLabel(unknown, true) == "OFFER ENDED",
                "A lapsed or unclocked offer can never be accepted");
            TestAssert.That(MfdSecondaryObjectives.AcceptLabel(Card(4, 600f, active: true), true) == "TRACKED ON MAP" &&
                MfdSecondaryObjectives.AcceptLabel(Card(5, 0f), true) == "CONTRACT ENDED" &&
                MfdSecondaryObjectives.AcceptLabel(null, true) == "CONTRACT ENDED",
                "Accepted, closed and lost cards name their non-action instead of offering one");

            string paid = MfdSecondaryObjectives.PayoutLabel(Card(1, 0f, complete: true));
            string closed = MfdSecondaryObjectives.PayoutLabel(Card(3, 0f));
            TestAssert.That(paid.StartsWith("PAID  $") && paid.EndsWith("+   125 XP") &&
                MfdSecondaryObjectives.PayoutLabel(Card(2, 300f, offered: true)).StartsWith("$") &&
                closed.StartsWith("UNPAID  $") && closed.EndsWith("+   125 XP"),
                "Pay is only reported as collected when the host reported completion");
            TestAssert.That(paid.Contains("$1,400"),
                "Pay figures group with the invariant comma, never the machine's separator");

            TestAssert.That(MfdSecondaryObjectives.BoardSummary(3, 1, 2, 4) == "3 OFFERS  ·  1/2 ACTIVE  ·  4 CLOSED" &&
                MfdSecondaryObjectives.BoardSummary(1, 0, 2, 0).StartsWith("1 OFFER  ·  ") &&
                MfdSecondaryObjectives.ShortCount(1, 0) == "1",
                "A host that reports no ceiling yields a plain count");
            TestAssert.That(!string.IsNullOrEmpty(MfdSecondaryObjectives.EmptyMessage(0, BoardEmptyReason.Ready)) &&
                !string.IsNullOrEmpty(MfdSecondaryObjectives.EmptyMessage(1, BoardEmptyReason.Ready)) &&
                !string.IsNullOrEmpty(MfdSecondaryObjectives.EmptyMessage(2, BoardEmptyReason.Ready)),
                "Every filter has an empty state that explains the host cycle");
            TestAssert.That(MfdSecondaryObjectives.EmptyMessage(0, BoardEmptyReason.Unavailable).StartsWith("CONTRACT BOARD UNAVAILABLE") &&
                MfdSecondaryObjectives.EmptyMessage(0, BoardEmptyReason.LinkLost).StartsWith("WAITING FOR THE HOST BOARD") &&
                MfdSecondaryObjectives.EmptyMessage(0, BoardEmptyReason.LimitReached).StartsWith("ACTIVE LIMIT REACHED") &&
                MfdSecondaryObjectives.EmptyMessage(0, BoardEmptyReason.DirectorExhausted).StartsWith("NO CONTRACTS TO ISSUE") &&
                MfdSecondaryObjectives.EmptyMessage(0, BoardEmptyReason.Unavailable) !=
                MfdSecondaryObjectives.EmptyMessage(0, BoardEmptyReason.DirectorExhausted),
                "An empty tab names the real reason, not the contract cycle");
        }

        private static SecondaryObjectiveView Card(int id, float seconds,
            bool offered = false, bool active = false, bool complete = false) =>
            new SecondaryObjectiveView(id, "COVER THE SUPPLY RUN", "brief", "HLT Munitions Truck",
                active ? "RETURN TO BASE" : "OFFERED", "Faction morale +3", complete ? 1f : 0f, seconds,
                1400, 125, complete, isOffered: offered, isActive: active, hasMarker: active,
                x: 100f, z: -200f, radius: 1500f);
    }
}
