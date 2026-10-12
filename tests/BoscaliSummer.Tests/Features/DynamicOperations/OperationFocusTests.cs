using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Tests.Features.DynamicOperations
{
    /// <summary>The operation focus a sortie ties to, and the link a card carries (spec C-06, C-07).</summary>
    internal static class OperationFocusTests
    {
        public static void Run()
        {
            CoversByIdOrAnchor();
            DefaultMeansNoOperation();
            LinkClipsAndTies();
            CardsChainToTheLinkConstructor();
        }

        private static void CoversByIdOrAnchor()
        {
            var focus = new OperationFocus(7, FocusRole.Assault, "PORT AURELIA", "PORT AURELIA", 100f, -200f, 4000f);
            TestAssert.That(focus.IsLive, "a positive id is live");
            TestAssert.That(focus.Covers(7, ""), "the same id is covered");
            TestAssert.That(focus.Covers(3, "PORT AURELIA"), "a redirect to the same anchor keeps an older tie");
            TestAssert.That(!focus.Covers(3, "OTHER"), "another anchor and id is not covered");
            TestAssert.That(!focus.Covers(0, "PORT AURELIA"), "an untied card (id 0) is never covered");
            TestAssert.That(!focus.Covers(3, null), "a missing anchor cannot match");
            TestAssert.That(!new OperationFocus(7, FocusRole.Assault, "", "", 0f, 0f, 0f).Covers(3, ""),
                "an empty anchor never matches an empty anchor");
        }

        private static void DefaultMeansNoOperation()
        {
            OperationFocus none = default;
            TestAssert.That(!none.IsLive && !none.Covers(1, "A"), "the default focus is no operation");
            var outcome = new SortieOutcome(1, 2, null, 0, null, 3f, 4f, null, false);
            TestAssert.That(outcome.Title == "" && outcome.AnchorKey == "" && outcome.Pilot == "",
                "an outcome never carries a null string");
        }

        private static void LinkClipsAndTies()
        {
            var link = new SortieLink(7, FocusRole.Defend, new string('P', 41), true, true, 12500f, -6750f, 1500f, true);
            TestAssert.That(link.Place.Length == SortieLink.MaximumPlace && link.Tied, "place is clipped to 40 and the link is tied");
            var general = new SortieLink(0, FocusRole.None, null, false, false, 0f, 0f, 0f, false);
            TestAssert.That(!general.Tied && general.Place == "", "operation id 0 is not tied and has an empty place");
        }

        private static void CardsChainToTheLinkConstructor()
        {
            var tasking = new ObjectiveTasking(ObjectiveFamily.Strike, ObjectiveAsset.Site, ObjectiveContact.Fixed, 0f, "", "", "",
                new[] { new ObjectivePhase(1, "A", "B", ObjectivePhaseStatus.Current) });
            TestAssert.That(tasking.SubjectId == -1, "a tasking without a subject reads -1");
            TestAssert.That(new ObjectiveTasking(ObjectiveFamily.Strike, ObjectiveAsset.Site, ObjectiveContact.Fixed, 0f, "", "", "",
                new[] { new ObjectivePhase(1, "A", "B", ObjectivePhaseStatus.Current) }, subjectId: 63).SubjectId == 63,
                "a subject id is carried");
            var legacy = new SecondaryObjectiveView(1, "T", "D", "X", "S", "R", 0f, 10f, 5, 1, false);
            var withTasking = new SecondaryObjectiveView(1, "T", "D", "X", "S", "R", 0f, 10f, 5, 1, false, false, true, true, 1f, 2f, 3f, "P", tasking);
            var tied = new SecondaryObjectiveView(1, "T", "D", "X", "S", "R", 0f, 10f, 5, 1, false, true, false, false, 1f, 2f, 3f, "P", tasking,
                new SortieLink(7, FocusRole.Assault, "A", true, true, 1f, 2f, 3f, false));
            TestAssert.That(legacy.Link == null && withTasking.Link == null, "the older constructors leave the card untied");
            TestAssert.That(tied.Link != null && tied.Link.OperationId == 7 && tied.Tasking == tasking, "the new constructor carries the link");
        }
    }
}
