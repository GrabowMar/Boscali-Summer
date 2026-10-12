using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>Select-and-fly (annex 05 P8): every kind of board item resolves to the place its own row holds, and an item with no known place flies nowhere.</summary>
    internal static class StrAnchorsTests
    {
        public static void Run()
        {
            StrViewModel rich = StrFixtures.Model("rich");
            TestAssert.That(rich.Effort.Live && rich.ProposalCount >= 1 && rich.FrontCount >= 1 && rich.SortieCount >= 1 && rich.PostCount >= 1,
                "the rich fixture has one of every kind");

            Agrees(rich, rich.Effort.Id, rich.Effort.X, rich.Effort.Z, "the main effort");
            for (int i = 0; i < rich.ProposalCount; i++) Agrees(rich, rich.Proposals[i].Id, rich.Proposals[i].X, rich.Proposals[i].Z, "proposal " + i);
            for (int i = 0; i < rich.FrontCount; i++) Agrees(rich, rich.Fronts[i].Id, rich.Fronts[i].X, rich.Fronts[i].Z, "front " + i);
            for (int i = 0; i < rich.SortieCount; i++) Agrees(rich, rich.Sorties[i].Id, rich.Sorties[i].X, rich.Sorties[i].Z, "sortie " + i);
            for (int i = 0; i < rich.PostCount; i++) Agrees(rich, rich.Posts[i].Id, rich.Posts[i].X, rich.Posts[i].Z, "post " + i);

            TestAssert.That(!StrAnchors.TryGet(rich, StrItemId.None, out float x, out float z) && float.IsNaN(x) && float.IsNaN(z), "no item has no place");
            TestAssert.That(!StrAnchors.TryGet(rich, new StrItemId(StrItemKind.Proposal, 9999), out x, out z), "a proposal that left the deck has no place");
            TestAssert.That(!StrAnchors.TryGet(rich, new StrItemId(StrItemKind.Sortie, 9999), out x, out z), "a sortie that left the board has no place");
            TestAssert.That(!StrAnchors.TryGet(rich, new StrItemId(StrItemKind.Effort, rich.Effort.Id.Key + 1), out x, out z), "an operation that is not the live one has no place");
            TestAssert.That(!StrAnchors.TryGet(null, rich.Effort.Id, out x, out z), "no model has no place");

            StrViewModel proposals = StrFixtures.Model("proposals");
            TestAssert.That(!StrAnchors.TryGet(proposals, new StrItemId(StrItemKind.Effort, 21), out x, out z), "no live operation, no effort place");

            // A sortie whose site is not known (not accepted and no link) must not fly the map to the origin.
            StrViewModel unknown = StrFixtures.Model("unknown");
            for (int i = 0; i < unknown.SortieCount; i++)
                if (!unknown.Sorties[i].SiteKnown)
                    TestAssert.That(!StrAnchors.TryGet(unknown, unknown.Sorties[i].Id, out x, out z), "a sortie with no known site has no place");
            for (int i = 0; i < unknown.FrontCount; i++)
                if (float.IsNaN(unknown.Fronts[i].X) || float.IsNaN(unknown.Fronts[i].Z))
                    TestAssert.That(!StrAnchors.TryGet(unknown, unknown.Fronts[i].Id, out x, out z), "a front with no position has no place");
        }

        private static void Agrees(StrViewModel vm, StrItemId id, float wantX, float wantZ, string what)
        {
            bool known = !float.IsNaN(wantX) && !float.IsNaN(wantZ);
            bool got = StrAnchors.TryGet(vm, id, out float x, out float z);
            TestAssert.That(got == known, what + (known ? " has a place" : " has no place"));
            if (known) TestAssert.That(x == wantX && z == wantZ, what + " resolves to its row (" + x + "," + z + " against " + wantX + "," + wantZ + ")");
        }
    }
}
