using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>
    /// The change stamp the feed reader fills (annex 05 P8): the builder returns without touching the model on an equal stamp, so a stamp that
    /// misses a displayed feed leaves the board stale. Every input the builder shows moves it; the decaying seconds the builder keys itself do not.
    /// </summary>
    internal static class StrFeedStampTests
    {
        public static void Run()
        {
            StrFeeds rich = StrFixtures.Feeds("rich");
            TestAssert.That(StrFeedStamp.Of(in rich) == StrFeedStamp.Of(in rich), "the same feeds give the same stamp");
            EveryDisplayedFeedMovesTheStamp(rich);
            WhatTheBuilderKeysItselfDoesNot(rich);
            ACardInPlaceOrANewListMovesIt(rich);
            ItAllocatesNothing(rich);
            AnUnknownCensusAndNullsAreSafe();
        }

        private static void Moves(StrFeeds before, Func<StrFeeds, StrFeeds> change, string what)
        {
            StrFeeds after = change(before);
            TestAssert.That(StrFeedStamp.Of(in before) != StrFeedStamp.Of(in after), what + " moves the stamp");
        }

        private static void Holds(StrFeeds before, Func<StrFeeds, StrFeeds> change, string what)
        {
            StrFeeds after = change(before);
            TestAssert.That(StrFeedStamp.Of(in before) == StrFeedStamp.Of(in after), what + " leaves the stamp alone");
        }

        private static void EveryDisplayedFeedMovesTheStamp(StrFeeds f)
        {
            Moves(f, x => { x.IsHost = !x.IsHost; return x; }, "host or client");
            Moves(f, x => { x.LocalId += 1; return x; }, "the local pilot");
            Moves(f, x => { x.FactionCode = "PALA"; return x; }, "the faction");
            Moves(f, x => { x.HasOwn = !x.HasOwn; return x; }, "having an own aircraft");
            Moves(f, x => { x.OwnX += 150f; return x; }, "150 m east");
            Moves(f, x => { x.OwnZ -= 250f; return x; }, "250 m south");
            Moves(f, x => { x.OwnX = float.NaN; return x; }, "an own position that is not finite");
            Moves(f, x => { x.WarAvailable = !x.WarAvailable; return x; }, "the war view being there");
            Moves(f, x => { x.WarState = TheaterViewState.Stale; return x; }, "the war view state");
            Moves(f, x => { x.War = StrFixtures.Snapshot(); return x; }, "a new snapshot");
            Moves(f, x => { x.WarCanCommand = !x.WarCanCommand; return x; }, "the right to command");
            Moves(f, x => { x.WarPending = !x.WarPending; return x; }, "a pending order");
            Moves(f, x => { x.WarLast = new TheaterCommandResult { Code = TheaterCommandCode.Accepted }; return x; }, "a command answer");
            StrFeeds answered = f; answered.WarLast = new TheaterCommandResult { Code = TheaterCommandCode.Accepted, LockSeconds = 90f };
            Moves(answered, x => { x.WarLast = new TheaterCommandResult { Code = TheaterCommandCode.Locked, LockSeconds = 90f }; return x; }, "another answer code");
            Moves(answered, x => { x.WarLast = new TheaterCommandResult { Code = TheaterCommandCode.Accepted, LockSeconds = 60f }; return x; }, "another lock time");
            Moves(answered, x => { x.WarLast = null; return x; }, "the answer expiring");
            Moves(f, x => { x.SortiesAvailable = !x.SortiesAvailable; return x; }, "the sortie view being there");
            Moves(f, x => { x.SortiesFresh = !x.SortiesFresh; return x; }, "the sortie list going stale");
            Moves(f, x => { x.SortieActionPending = !x.SortieActionPending; return x; }, "a pending accept");
            Moves(f, x => { x.SortieActiveLimit += 1; return x; }, "the sortie ceiling");
            Moves(f, x => { x.PostsAvailable = !x.PostsAvailable; return x; }, "the post feed being there");
            Moves(f, x => { x.PostCount -= 1; return x; }, "a post leaving");
            Moves(f, x => { x.Posts = new[] { x.Posts[0], x.Posts[1] }; x.Posts[0].Bounty += 100; return x; }, "a bounty");
            Moves(f, x => { x.Posts = new[] { x.Posts[0], x.Posts[1] }; x.Posts[1].X += 5000f; return x; }, "a post position");
            Moves(f, x => { x.Posts = new[] { x.Posts[0], x.Posts[1] }; x.Posts[0].Site = "AN OTHER BASE"; return x; }, "a post site name");
            Moves(f, x => { x.Posts = new[] { x.Posts[0], x.Posts[1] }; x.Posts[0].BountyCapped = true; return x; }, "a capped bounty");
            Moves(f, x => { x.WingCount -= 1; return x; }, "a wing entry leaving");
            Moves(f, x => { x.Wing = (StrWingIn[])x.Wing.Clone(); x.Wing[1].State = WingSortieState.Tasked; return x; }, "a wing state");
            Moves(f, x => { x.Wing = (StrWingIn[])x.Wing.Clone(); x.Wing[0].Refusal = "NO RECRUITED WING"; return x; }, "a wing refusal");
            Moves(f, x => { x.Wing = (StrWingIn[])x.Wing.Clone(); x.Wing[2].Words = "ON STATION"; return x; }, "the wing words");
            Moves(f, x => { x.PickerPrompt = "CLICK THE MAP"; return x; }, "a map gesture prompt");
            Moves(f, x => { x.KnownAirDefence += 1; return x; }, "the known air defence");
            Moves(f, x => { x.Census.AirFriendly = (short)(x.Census.AirFriendly + 1); return x; }, "a census count");
            Moves(f, x => { x.Census.Known = !x.Census.Known; return x; }, "the census being known");
            Moves(f, x => { x.Census.Threat = (byte)(x.Census.Threat + 1); return x; }, "the threat level");
            Moves(f, x => { x.Census.FrontMetres += 500f; return x; }, "the front length");
            Moves(f, x => { x.Census.ThreatWhy = x.Census.ThreatWhy == TheaterThreatWhy.Nominal ? TheaterThreatWhy.AirContact : TheaterThreatWhy.Nominal; return x; }, "the threat reason");
        }

        private static void WhatTheBuilderKeysItselfDoesNot(StrFeeds f)
        {
            Holds(f, x => { x.Stamp += 7; return x; }, "the stamp field itself");
            Holds(f, x => { x.WarAgeSeconds += 3f; return x; }, "a report ageing");
            Holds(f, x => { x.WarSincePublished += 3f; return x; }, "time since publication");
            Holds(f, x => { x.WaitSeconds += 3f; return x; }, "waiting for a first report");
            Holds(f, x => { x.PostsAgeSeconds += 3f; return x; }, "the post feed ageing");
            Holds(f, x => { x.CensusAgeSeconds += 3f; return x; }, "the census ageing");
            Holds(f, x => { x.OwnX += 30f; x.OwnZ += 30f; return x; }, "30 m of flight inside one 100 m cell");
        }

        private static void ACardInPlaceOrANewListMovesIt(StrFeeds f)
        {
            Moves(f, x =>
            {
                var cards = new[] { x.Sorties[0], x.Sorties[1], x.Sorties[2] };
                cards[1] = StrFixtures.Card(32, "COVER THE ADVANCE", ObjectiveFamily.AirCover, true, false, 700f);
                x.Sorties = cards;
                return x;
            }, "a card replaced by a newer one of the same id");
            Moves(f, x => { x.Sorties = new[] { x.Sorties[0], x.Sorties[1] }; return x; }, "a card leaving");
            Holds(f, x => { x.Sorties = new[] { x.Sorties[0], x.Sorties[1], x.Sorties[2] }; return x; }, "a new list of the same cards");
        }

        private static void ItAllocatesNothing(StrFeeds f)
        {
            int sink = 0;
            for (int i = 0; i < 100; i++) sink ^= StrFeedStamp.Of(in f);   // warm up the jit
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) sink ^= StrFeedStamp.Of(in f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestAssert.That(allocated == 0, "a stamp allocates nothing (" + allocated + " bytes over 1000 stamps, sink " + sink + ")");
        }

        private static void AnUnknownCensusAndNullsAreSafe()
        {
            var empty = new StrFeeds();   // every reference null, every number zero: the state before the reader has run
            TestAssert.That(StrFeedStamp.Of(in empty) == StrFeedStamp.Of(in empty), "all-null feeds stamp without throwing");
            StrFeeds unknown = StrFixtures.Feeds("unknown");
            TestAssert.That(StrFeedStamp.Of(in unknown) == StrFeedStamp.Of(in unknown), "a feed full of unknown encodings is stable");
            foreach (string name in StrFixtures.Names)
            {
                StrFeeds one = StrFixtures.Feeds(name);
                TestAssert.That(StrFeedStamp.Of(in one) == StrFeedStamp.Of(in one), name + " stamps stably");
            }
        }
    }
}
