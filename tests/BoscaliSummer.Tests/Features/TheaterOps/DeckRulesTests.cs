using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>The live deck: stable ids across refreshes, three slots, no card on the operation's own front (annex 01 E2).</summary>
    internal static class DeckRulesTests
    {
        public static void Run()
        {
            AFreshDeckTakesTheBestThreeInOrder();
            ARefreshWithTheSameOffersKeepsEveryIdAndRevision();
            ADroppedFrontFreesExactlyItsSlot();
            AKindChangeOnTheSameKeyReplacesTheCard();
            TheExcludedKeyNeverAppearsAndIsDroppedWhenPresent();
            TheDeckNeverExceedsThreeAndNeverRepeatsAFront();
            SurvivorsTakeTheCurrentScore();
            RecommendedIsTheBestScoreWithTiesToTheLowerIndex();
            BadInputIsHarmless();
            AFullDeckGivesUpItsWeakestNonDefenceForABaseDefence();
        }

        private static List<DeckOffer> Offers(params (string key, TheaterOpKind kind, float score)[] list)
        {
            var offers = new List<DeckOffer>();
            foreach (var o in list) offers.Add(new DeckOffer(o.key, o.kind, o.score));
            return offers;
        }

        private static string Ids(List<DeckCard> deck)
        {
            var parts = new List<string>();
            foreach (DeckCard c in deck) parts.Add(c.Key + ":" + c.Id + "/" + c.Revision);
            return string.Join(" ", parts);
        }

        private static void AFreshDeckTakesTheBestThreeInOrder()
        {
            var deck = new List<DeckCard>();
            int nextId = 1, revision = 0;
            var ranked = Offers(("A", TheaterOpKind.Assault, 9f), ("B", TheaterOpKind.Recon, 7f), ("C", TheaterOpKind.Defend, 5f), ("D", TheaterOpKind.Assault, 3f));
            bool changed = DeckRules.Maintain(deck, ranked, null, ref nextId, ref revision);
            TestAssert.That(changed, "dealing cards is a change");
            TestAssert.Eq(Ids(deck), "A:1/1 B:2/2 C:3/3", "the best three in ranked order, ids from 1 and a new revision each");
            TestAssert.Eq(nextId, 4, "the id counter moved past the dealt ids");
            TestAssert.Eq(revision, 3, "the revision counter moved once per card");
            TestAssert.Eq(deck[1].Kind, TheaterOpKind.Recon, "a card remembers its kind");
        }

        private static void ARefreshWithTheSameOffersKeepsEveryIdAndRevision()
        {
            var deck = new List<DeckCard>();
            int nextId = 1, revision = 0;
            var ranked = Offers(("A", TheaterOpKind.Assault, 9f), ("B", TheaterOpKind.Recon, 7f), ("C", TheaterOpKind.Defend, 5f));
            DeckRules.Maintain(deck, ranked, null, ref nextId, ref revision);
            string before = Ids(deck);
            bool changed = DeckRules.Maintain(deck, ranked, null, ref nextId, ref revision);
            TestAssert.That(!changed, "a refresh with the same offers changes no card");
            TestAssert.Eq(Ids(deck), before, "every id and revision survives");
            TestAssert.Eq(nextId, 4, "no id was spent");
            TestAssert.Eq(revision, 3, "no revision was spent");
            DeckRules.Maintain(deck, Offers(("C", TheaterOpKind.Defend, 5f), ("A", TheaterOpKind.Assault, 9f), ("B", TheaterOpKind.Recon, 7f)), null, ref nextId, ref revision);
            TestAssert.Eq(Ids(deck), before, "a reordered ranking does not reorder or renumber the cards");
        }

        private static void ADroppedFrontFreesExactlyItsSlot()
        {
            var deck = new List<DeckCard>();
            int nextId = 1, revision = 0;
            DeckRules.Maintain(deck, Offers(("A", TheaterOpKind.Assault, 9f), ("B", TheaterOpKind.Recon, 7f), ("C", TheaterOpKind.Defend, 5f)), null, ref nextId, ref revision);
            var next = Offers(("A", TheaterOpKind.Assault, 9f), ("C", TheaterOpKind.Defend, 5f), ("D", TheaterOpKind.Assault, 4f), ("E", TheaterOpKind.Assault, 3f));
            bool changed = DeckRules.Maintain(deck, next, null, ref nextId, ref revision);
            TestAssert.That(changed, "a card appearing and one disappearing is a change");
            TestAssert.Eq(Ids(deck), "A:1/1 C:3/3 D:4/4", "the survivors keep their numbers, the refill takes the next id and revision, and appends");
            TestAssert.Eq(nextId, 5, "one id spent");
            TestAssert.Eq(revision, 4, "one revision spent");
        }

        private static void AKindChangeOnTheSameKeyReplacesTheCard()
        {
            var deck = new List<DeckCard>();
            int nextId = 1, revision = 0;
            DeckRules.Maintain(deck, Offers(("A", TheaterOpKind.Assault, 9f), ("B", TheaterOpKind.Recon, 7f)), null, ref nextId, ref revision);
            bool changed = DeckRules.Maintain(deck, Offers(("A", TheaterOpKind.Recon, 9f), ("B", TheaterOpKind.Recon, 7f)), null, ref nextId, ref revision);
            TestAssert.That(changed, "a kind change is a change");
            TestAssert.Eq(Ids(deck), "B:2/2 A:3/3", "the card on A is a new card (identity is key and kind), appended after the survivor");
            TestAssert.Eq(deck[1].Kind, TheaterOpKind.Recon, "the new card carries the new kind");
        }

        private static void TheExcludedKeyNeverAppearsAndIsDroppedWhenPresent()
        {
            var deck = new List<DeckCard>();
            int nextId = 1, revision = 0;
            var ranked = Offers(("A", TheaterOpKind.Assault, 9f), ("B", TheaterOpKind.Recon, 7f), ("C", TheaterOpKind.Defend, 5f), ("D", TheaterOpKind.Assault, 3f));
            DeckRules.Maintain(deck, ranked, "A", ref nextId, ref revision);
            TestAssert.Eq(Ids(deck), "B:1/1 C:2/2 D:3/3", "the excluded front is skipped and the next best fills the slot");
            DeckRules.Maintain(deck, ranked, "B", ref nextId, ref revision);
            TestAssert.Eq(Ids(deck), "C:2/2 D:3/3 A:4/4", "a card that is now the operation's front is dropped and its slot refilled");
            foreach (DeckCard c in deck) TestAssert.That(c.Key != "B", "the operation's front is not in the deck");
        }

        private static void TheDeckNeverExceedsThreeAndNeverRepeatsAFront()
        {
            var deck = new List<DeckCard>();
            int nextId = 1, revision = 0;
            var ranked = Offers(("A", TheaterOpKind.Assault, 9f), ("A", TheaterOpKind.Recon, 8f), ("B", TheaterOpKind.Recon, 7f), ("C", TheaterOpKind.Defend, 5f), ("D", TheaterOpKind.Assault, 3f), ("E", TheaterOpKind.Assault, 2f));
            DeckRules.Maintain(deck, ranked, null, ref nextId, ref revision);
            TestAssert.Eq(deck.Count, DeckRules.Slots, "three slots, no more");
            TestAssert.Eq(Ids(deck), "A:1/1 B:2/2 C:3/3", "a front is offered once even when ranked twice");
            var crowded = new List<DeckCard>();
            for (int i = 0; i < 5; i++) crowded.Add(new DeckCard { Id = 10 + i, Revision = 10 + i, Key = "K" + i, Kind = TheaterOpKind.Assault, Score = 5f });
            var all = new List<DeckOffer>();
            for (int i = 0; i < 5; i++) all.Add(new DeckOffer("K" + i, TheaterOpKind.Assault, 5f));
            bool changed = DeckRules.Maintain(crowded, all, null, ref nextId, ref revision);
            TestAssert.That(changed, "trimming a crowded deck is a change");
            TestAssert.Eq(Ids(crowded), "K0:10/10 K1:11/11 K2:12/12", "a crowded deck keeps the first three in order");
        }

        private static void SurvivorsTakeTheCurrentScore()
        {
            var deck = new List<DeckCard>();
            int nextId = 1, revision = 0;
            DeckRules.Maintain(deck, Offers(("A", TheaterOpKind.Assault, 9f), ("B", TheaterOpKind.Recon, 7f)), null, ref nextId, ref revision);
            TestAssert.That(deck[0].Recommended && !deck[1].Recommended, "the best score is the staff's choice");
            bool changed = DeckRules.Maintain(deck, Offers(("A", TheaterOpKind.Assault, 2f), ("B", TheaterOpKind.Recon, 7f)), null, ref nextId, ref revision);
            TestAssert.That(!changed, "a score moving changes no (Id, Revision)");
            TestAssert.Near(deck[0].Score, 2f, "the survivor reads the new score");
            TestAssert.That(!deck[0].Recommended && deck[1].Recommended, "the staff's choice follows the score");
        }

        private static void RecommendedIsTheBestScoreWithTiesToTheLowerIndex()
        {
            TestAssert.Eq(DeckRules.Recommended(new List<DeckCard>()), -1, "an empty deck has no choice");
            TestAssert.Eq(DeckRules.Recommended(null), -1, "no deck has no choice");
            var deck = new List<DeckCard>
            {
                new DeckCard { Score = 4f }, new DeckCard { Score = 8f }, new DeckCard { Score = 8f },
            };
            TestAssert.Eq(DeckRules.Recommended(deck), 1, "the best score wins and a tie keeps the lower index");
            TestAssert.That(!deck[1].Recommended, "Recommended() reads and sets nothing");
            deck[0].Score = float.NaN;
            deck.RemoveRange(1, 2);
            TestAssert.Eq(DeckRules.Recommended(deck), 0, "a lone card with no usable score is still the only choice");
            deck.Add(new DeckCard { Score = 1f });
            TestAssert.Eq(DeckRules.Recommended(deck), 1, "a real score beats a NaN one");
        }

        // Cards keep their places, so an own base under attack cannot wait for a slot: DropWeakest frees one, never from another base defence.
        private static void AFullDeckGivesUpItsWeakestNonDefenceForABaseDefence()
        {
            var deck = new List<DeckCard>
            {
                new DeckCard { Id = 1, Key = "A", Kind = TheaterOpKind.Assault, Score = 9f },
                new DeckCard { Id = 2, Key = "#B", Kind = TheaterOpKind.Defend, Score = 2f },
                new DeckCard { Id = 3, Key = "C", Kind = TheaterOpKind.Recon, Score = 4f },
            };
            TestAssert.Eq(DeckRules.DropWeakest(deck), "C", "the weakest card that is not a base defence goes, whatever the defence scores");
            TestAssert.Eq(deck.Count, 2, "and its slot is free");
            deck.Add(new DeckCard { Id = 4, Key = "D", Kind = TheaterOpKind.Assault, Score = 4f });
            deck.Add(new DeckCard { Id = 5, Key = "E", Kind = TheaterOpKind.Recon, Score = 4f });
            TestAssert.Eq(DeckRules.DropWeakest(deck), "E", "a tie gives up the later card");
            var defences = new List<DeckCard>
            {
                new DeckCard { Key = "#X", Kind = TheaterOpKind.Defend, Score = 1f }, new DeckCard { Key = "#Y", Kind = TheaterOpKind.Defend, Score = 2f },
            };
            TestAssert.That(DeckRules.DropWeakest(defences) == null && defences.Count == 2, "a deck of base defences keeps them all");
            TestAssert.That(DeckRules.DropWeakest(null) == null && DeckRules.DropWeakest(new List<DeckCard>()) == null, "nothing to drop is null");
            TestAssert.That(DeckRules.IsBaseDefence(TheaterOpKind.Defend, "#B") && !DeckRules.IsBaseDefence(TheaterOpKind.Assault, "#B") &&
                !DeckRules.IsBaseDefence(TheaterOpKind.Defend, "@B") && !DeckRules.IsBaseDefence(TheaterOpKind.Defend, ""), "a base defence is a DEFEND on a # key");
        }

        private static void BadInputIsHarmless()
        {
            int nextId = 1, revision = 0;
            TestAssert.That(!DeckRules.Maintain(null, Offers(("A", TheaterOpKind.Assault, 1f)), null, ref nextId, ref revision), "no deck, no change");
            var deck = new List<DeckCard>();
            DeckRules.Maintain(deck, Offers(("A", TheaterOpKind.Assault, 9f), ("B", TheaterOpKind.Recon, 7f)), null, ref nextId, ref revision);
            bool changed = DeckRules.Maintain(deck, null, null, ref nextId, ref revision);
            TestAssert.That(changed && deck.Count == 0, "no ranking empties the deck");
            DeckRules.Maintain(deck, Offers(("", TheaterOpKind.Assault, 9f), (null, TheaterOpKind.Recon, 7f), ("B", TheaterOpKind.Recon, 6f)), "", ref nextId, ref revision);
            TestAssert.Eq(deck.Count, 1, "an offer with no key is never dealt");
            TestAssert.Eq(deck[0].Key, "B", "and an empty exclude key excludes nothing");
        }
    }
}
