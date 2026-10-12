using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The seams the Station Board panel, the map overlay and the harnesses share (spec 5.18): small, but every later slice builds on them.</summary>
    internal static class StrContractsTests
    {
        public static void Run()
        {
            ItemIdsCompareByKindAndKey();
            AnActionKnowsWhetherItIsAllowed();
            UnknownIsNeverAConfidentZero();
        }

        private static void ItemIdsCompareByKindAndKey()
        {
            var a = new StrItemId(StrItemKind.Front, 3);
            TestAssert.That(a == new StrItemId(StrItemKind.Front, 3) && a.GetHashCode() == new StrItemId(StrItemKind.Front, 3).GetHashCode(), "equal ids hash alike");
            TestAssert.That(a != new StrItemId(StrItemKind.Proposal, 3) && a != new StrItemId(StrItemKind.Front, 4), "kind and key both count");
            TestAssert.That(StrItemId.None.IsNone && default(StrItemId) == StrItemId.None && !a.IsNone, "the default id is none");
        }

        private static void AnActionKnowsWhetherItIsAllowed()
        {
            TestAssert.That(new StrAct(StrWhy.None, null).Allowed && new StrAct(StrWhy.None, null).Text == "", "no reason means allowed, with empty text");
            TestAssert.That(!new StrAct(StrWhy.Stale, "REPORT STALE").Allowed && !new StrAct(StrWhy.Stale, "x").Sent, "a reason refuses and sends nothing");
            TestAssert.That(StrLayers.All == (StrLayers.Fronts | StrLayers.Operation | StrLayers.Sorties), "the three overlay layers make All");
        }

        private static void UnknownIsNeverAConfidentZero()
        {
            var model = new StrViewModel();
            TestAssert.That(float.IsNaN(model.AgeSeconds) && model.AgeText == "—", "a fresh model has no age and prints a dash");
            TestAssert.That(model.Wave.Cost == -1 && model.Wave.Pool == -1 && model.Wave.Affordable == StrTri.Unknown, "an unpriced wave is unknown, not free");
            TestAssert.That(model.Effort.LockSeconds == -1 && float.IsNaN(model.Effort.Pressure) && model.Effort.PressureText == "—", "an idle effort has no lock and no pressure");
            TestAssert.That(model.AlertLevel == 0 && model.AlertWord == "NOT MEASURED", "an unmeasured alert level says so");
            foreach (int count in model.SortieCounts) TestAssert.That(count == -1, "sortie counts start unknown");
            foreach (float share in model.SortieShares) TestAssert.That(float.IsNaN(share), "sortie shares start unknown");
            TestAssert.That(model.Proposals.Length == StrViewModel.MaxProposals && model.Fronts.Length == StrViewModel.MaxFronts, "the row tables have their ceilings");

            StrFeeds feeds = default;
            TestAssert.That(!feeds.Census.Known, "a feed the reader forgot to fill reads as an unmeasured census");
            TestAssert.That(!new StrUiState().ShowFronts && new StrUiState().Selected.IsNone, "a default ui state selects nothing");
        }
    }
}
