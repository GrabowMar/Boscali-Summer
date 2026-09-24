using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Comms.Domain;
using BoscaliSummer.Features.Comms.Presentation;

namespace BoscaliSummer.Tests.Features.Comms
{
    internal static class CommsTests
    {
        private const int Blue = 101;
        private const int Red = 202;

        private static readonly CommsSender Host = new CommsSender { Id = 1, Name = "Host", Faction = Blue, Moderator = true };
        private static readonly CommsSender Wing = new CommsSender { Id = 2, Name = "Wing", Faction = Blue };
        private static readonly CommsSender Bandit = new CommsSender { Id = 3, Name = "Bandit", Faction = Red };
        private static readonly CommsSender Third = new CommsSender { Id = 4, Name = "Third", Faction = Blue };

        public static void Run()
        {
            CatalogIsWellFormed();
            TextIsSanitised();
            CodecRoundTripsAndValidates();
            SimplifyKeepsShapeAndBudget();
            ShapesAreClosedWhereTheyShouldBe();
            TeamPostsNeverReachTheOtherSide();
            PlacementIsValidated();
            AuthorBudgetRetiresOldest();
            OnlyAuthorOrHostErases();
            RateLimitStopsSpam();
            CallsDropAPingAtTheCaller();
            PollsCountOneBallotPerVoter();
            PollsCloseOnTime();
            DuelThrowStaysSecretAndResolves();
            HuntRanksAndRevealsOnlyAtTheEnd();
            SnapshotShowsOnlyWhatTheViewerMaySee();
            ClientAppliesTheWholeLifecycle();
            ClientMutesAndSkipsSelfNoise();
            GlyphsStayInTheUnitBox();
        }

        private static void CatalogIsWellFormed()
        {
            TestAssert.That(CommsCatalog.PalettePings <= CommsCatalog.Pings.Length, "palette pings exist");
            for (int i = 0; i < CommsCatalog.Calls.Length; i++)
            {
                BrevityCall call = CommsCatalog.Calls[i];
                TestAssert.That(!string.IsNullOrEmpty(call.Code) && call.Code.Length <= 14, "call code fits a button: " + call.Code);
                TestAssert.That(!call.MarksPosition || CommsCatalog.ValidPing(call.PingAtSelf), "a located call names a real ping");
            }
            foreach (PollTemplate template in CommsCatalog.PollTemplates)
            {
                TestAssert.That(CommsPollBook.TryShape(template.Question, template.Options, out _, out string[] options),
                    "every poll template is a valid poll: " + template.Question);
                TestAssert.That(options.Length == template.Options.Length, "template options survive cleaning");
            }
            TestAssert.That(CommsCatalog.DiceSides.Length == CommsCatalog.DiceNames.Length, "dice and names align");
            TestAssert.That(CommsCatalog.PenWidths.Length == CommsCatalog.PenWidthNames.Length, "widths and names align");
            TestAssert.That(CommsCatalog.PollDuration(-5) == CommsCatalog.PollDurations[0] &&
                            CommsCatalog.PollDuration(99) == CommsCatalog.PollDurations[CommsCatalog.PollDurations.Length - 1],
                "duration lookups clamp");
            foreach (PingKind ping in CommsCatalog.Pings) TestAssert.That(Array.IndexOf(CommsGlyphs.Keys, ping.Glyph) >= 0, "ping glyph drawn: " + ping.Glyph);
            foreach (StickerKind sticker in CommsCatalog.Stickers) TestAssert.That(Array.IndexOf(CommsGlyphs.Keys, sticker.Glyph) >= 0, "sticker glyph drawn: " + sticker.Glyph);
        }

        private static void TextIsSanitised()
        {
            TestAssert.That(CommsText.Clean("<size=400>hi</size>", 40) == "SIZE=400HI/SIZE", "markup brackets are stripped");
            TestAssert.That(CommsText.Clean("  sam \n\t site  ", 40) == "SAM SITE", "whitespace collapses");
            TestAssert.That(CommsText.Clean("abcdefghij", 4) == "ABCD", "length is capped");
            TestAssert.That(CommsText.Clean("keep case", 20, upper: false) == "keep case", "case can be kept");
            TestAssert.That(CommsText.Name(null) == "PILOT" && CommsText.Name("<>") == "PILOT", "a name is never empty");
            string[] options = CommsText.SplitOptions("yes,, YES , no; maybe/later|x", 4);
            TestAssert.That(options.Length == 4 && options[0] == "YES" && options[1] == "NO" && options[2] == "MAYBE",
                "options split, dedupe and cap");
            TestAssert.That(CommsText.Bearing(0, 0, 0, 100) == 0 && CommsText.Bearing(0, 0, 100, 0) == 90 &&
                            CommsText.Bearing(0, 0, 0, -100) == 180 && CommsText.Bearing(0, 0, -100, 0) == 270,
                "bearings are true, north up");
            TestAssert.That(CommsText.Distance(12400f, true) == "12.4 KM" && CommsText.Distance(640f, true) == "640 M",
                "metric distances");
            TestAssert.That(CommsText.Distance(1852f * 3f, false) == "3.0 NM", "imperial distances are nautical miles");
            TestAssert.That(CommsText.Countdown(42.2f) == "43s" && CommsText.Countdown(185f) == "3m 05s", "countdowns");
        }

        private static void CodecRoundTripsAndValidates()
        {
            int[] encoded = StrokeCodec.Encode(new[] { 1000f, -2000f, 1001f, -2001f, 1400f, -1600f, 1400f, -1600f });
            TestAssert.That(encoded.Length == 4, "sub-quantum moves and duplicates collapse");
            int[] deltas = StrokeCodec.ToDeltas(encoded);
            int[] back = StrokeCodec.FromDeltas(deltas);
            for (int i = 0; i < encoded.Length; i++) TestAssert.That(back[i] == encoded[i], "delta coding round-trips");
            TestAssert.That(Math.Abs(StrokeCodec.Restore(encoded[0]) - 1000f) <= StrokeCodec.Quantum, "restore is within a quantum");

            TestAssert.That(StrokeCodec.Valid(new[] { 1, 2 }, 1, 1), "a point is valid");
            TestAssert.That(!StrokeCodec.Valid(new[] { 1, 2, 3 }, 1, 4), "an odd count is not");
            TestAssert.That(!StrokeCodec.Valid(null, 1, 1), "null is not");
            TestAssert.That(!StrokeCodec.Valid(new[] { int.MaxValue, 0 }, 1, 1), "coordinates past the world limit are not");
            TestAssert.That(!StrokeCodec.Valid(new[] { 1, 2 }, 2, 4), "a stroke needs two points");
            TestAssert.That(StrokeCodec.Quantise(float.NaN) == 0 && StrokeCodec.Quantise(1e12f) == StrokeCodec.Quantise(StrokeCodec.WorldLimit),
                "NaN and huge inputs quantise safely");
        }

        private static void SimplifyKeepsShapeAndBudget()
        {
            var straight = new float[200];
            for (int i = 0; i < 100; i++) { straight[i * 2] = i * 10f; straight[i * 2 + 1] = 0f; }
            float[] thinned = StrokeCodec.Simplify(straight, 1f);
            TestAssert.That(thinned.Length == 4 && thinned[2] == 990f, "a straight line keeps only its ends");

            var corner = new float[] { 0, 0, 50, 0, 100, 0, 100, 50, 100, 100 };
            TestAssert.That(StrokeCodec.Simplify(corner, 1f).Length == 6, "a corner survives");

            var zigzag = new float[4000];
            for (int i = 0; i < 2000; i++) { zigzag[i * 2] = i * 5f; zigzag[i * 2 + 1] = i % 2 == 0 ? 0f : 400f; }
            float[] capped = StrokeCodec.Simplify(zigzag, 0f);
            TestAssert.That(capped.Length / 2 <= StrokeCodec.MaxPoints && capped.Length >= 4, "a huge stroke fits the budget");
        }

        private static void ShapesAreClosedWhereTheyShouldBe()
        {
            float[][] circle = CommsShapes.Build(CommsShape.Circle, 0f, 0f, 3000f, 4000f);
            TestAssert.That(circle.Length == 1 && circle[0].Length == (CommsShapes.CircleSegments + 1) * 2, "circle is one ring");
            float r = (float)Math.Sqrt(circle[0][10] * circle[0][10] + circle[0][11] * circle[0][11]);
            TestAssert.That(Math.Abs(r - 5000f) < 1f, "circle radius is the drag length");
            float[][] box = CommsShapes.Build(CommsShape.Box, 0f, 0f, 10f, 20f);
            TestAssert.That(box[0].Length == 10 && box[0][0] == box[0][8] && box[0][1] == box[0][9], "box closes");
            float[][] arrow = CommsShapes.Build(CommsShape.Arrow, 0f, 0f, 0f, 10000f);
            TestAssert.That(arrow.Length == 2 && arrow[1][3] == 10000f, "arrow head meets the tip");
            TestAssert.That(arrow[1][1] < 10000f && arrow[1][5] < 10000f, "arrow head points back along the shaft");
            TestAssert.That(arrow[1][0] < 0f && arrow[1][4] > 0f || arrow[1][0] > 0f && arrow[1][4] < 0f, "barbs sit either side");
        }

        private static void TeamPostsNeverReachTheOtherSide()
        {
            var host = new CommsAuthority(7);
            var output = new List<CommsOutbound>();
            host.Handle(Wing, Ping(CommsCatalog.PingSam, CommsChannel.Team), 0f, output);
            TestAssert.That(output.Count == 1 && output[0].Envelope.Event == CommsEvent.Item, "team ping becomes one item");
            TestAssert.That(output[0].Reaches(Third.Id, Third.Faction), "a teammate hears it");
            TestAssert.That(!output[0].Reaches(Bandit.Id, Bandit.Faction), "the enemy never does");

            output.Clear();
            host.Handle(Wing, Ping(CommsCatalog.PingMark, CommsChannel.All), 1f, output);
            TestAssert.That(output[0].Reaches(Bandit.Id, Bandit.Faction), "an ALL post reaches everyone");

            output.Clear();
            host.Rules.AllowAllChannel = false;
            host.Handle(Wing, Ping(CommsCatalog.PingMark, CommsChannel.All), 2f, output);
            TestAssert.That(output.Count == 1 && output[0].Envelope.Event == CommsEvent.Notice &&
                            output[0].Route == CommsRoute.One && output[0].Player == Wing.Id,
                "a disabled ALL channel refuses quietly, to the sender only");
        }

        private static void PlacementIsValidated()
        {
            var host = new CommsAuthority(7);
            var output = new List<CommsOutbound>();
            var bad = Ping(99, CommsChannel.Team);
            host.Handle(Wing, bad, 0f, output);
            TestAssert.That(Refused(output), "an unknown ping kind is refused");

            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.Place, Kind = (byte)CommsItemKind.Label, Points = new[] { 0, 0 }, Text = "<>" }, 0f, output);
            TestAssert.That(Refused(output), "a label of only markup is refused");

            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.Place, Kind = (byte)CommsItemKind.Label, Points = new[] { 0, 0 }, Text = "farp here" }, 0f, output);
            TestAssert.That(!Refused(output) && output[0].Envelope.Text == "FARP HERE", "a label is cleaned and stored");

            output.Clear();
            host.Handle(Wing, Stroke(new[] { 0, 0 }), 0f, output);
            TestAssert.That(Refused(output), "a one-point stroke is refused");

            output.Clear();
            host.Rules.AllowDrawing = false;
            host.Handle(Wing, Stroke(new[] { 0, 0, 10, 10 }), 0f, output);
            TestAssert.That(Refused(output), "drawing off refuses strokes");
            host.Rules.AllowDrawing = true;

            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.Place, Kind = 77, Points = new[] { 0, 0 } }, 0f, output);
            TestAssert.That(Refused(output), "an unknown item kind is refused");

            output.Clear();
            CommsIntent stroke = Stroke(new[] { 0, 0, 10, 10 });
            host.Handle(Wing, stroke, 0f, output);
            stroke.Points[0] = 999;
            TestAssert.That(host.Board.Items[host.Board.Count - 1].Points[0] == 0, "the host keeps its own copy of the geometry");
        }

        private static void AuthorBudgetRetiresOldest()
        {
            var host = new CommsAuthority(7);
            var output = new List<CommsOutbound>();
            int budget = CommsBoard.AuthorBudget(CommsItemKind.Ping);
            for (int i = 0; i < budget; i++) host.Handle(Wing, Ping(0, CommsChannel.Team), i * 5f, output);
            uint first = output[0].Envelope.Id;
            output.Clear();
            host.Handle(Wing, Ping(0, CommsChannel.Team), 100f, output);
            TestAssert.That(host.Board.CountBy(Wing.Id, CommsItemKind.Ping) == budget, "the budget holds");
            TestAssert.That(output.Count == 2 && output[1].Envelope.Event == CommsEvent.Remove &&
                            output[1].Envelope.Ids[0] == first, "the oldest ping is retired and announced");
            TestAssert.That(host.Board.Find(first) == null, "and really gone");
        }

        private static void OnlyAuthorOrHostErases()
        {
            var host = new CommsAuthority(7);
            var output = new List<CommsOutbound>();
            host.Handle(Wing, Ping(0, CommsChannel.Team), 0f, output);
            uint id = output[0].Envelope.Id;

            output.Clear();
            host.Handle(Third, new CommsIntent { Op = CommsOp.Erase, Target = id }, 1f, output);
            TestAssert.That(Refused(output) && host.Board.Find(id) != null, "a teammate cannot erase your ping");

            output.Clear();
            host.Handle(Host, new CommsIntent { Op = CommsOp.Erase, Target = id }, 2f, output);
            TestAssert.That(host.Board.Find(id) == null && output[0].Envelope.Event == CommsEvent.Remove, "the host can");

            output.Clear();
            host.Handle(Wing, Stroke(new[] { 0, 0, 5, 5 }), 3f, output);
            host.Handle(Wing, Ping(1, CommsChannel.All), 3f, output);
            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.ClearMine }, 4f, output);
            TestAssert.That(host.Board.Count == 0 && output[0].Envelope.Ids.Length == 2, "clear mine takes all of mine");

            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.ClearAll }, 5f, output);
            TestAssert.That(Refused(output), "a guest cannot clear the map");
            host.Handle(Third, Ping(0, CommsChannel.Team), 6f, output);
            output.Clear();
            host.Handle(Host, new CommsIntent { Op = CommsOp.ClearAll }, 7f, output);
            TestAssert.That(host.Board.Count == 0 && output[0].Envelope.Event == CommsEvent.Reset &&
                            (output[0].Envelope.Flags & CommsFlags.Snapshot) == 0, "the host wipes the map only");
        }

        private static void RateLimitStopsSpam()
        {
            var host = new CommsAuthority(7);
            var output = new List<CommsOutbound>();
            int refused = 0;
            for (int i = 0; i < 40; i++)
            {
                output.Clear();
                host.Handle(Wing, Ping(0, CommsChannel.Team), 0f, output);
                if (Refused(output)) refused++;
            }
            TestAssert.That(refused > 20, "a burst past the bucket is refused");
            output.Clear();
            host.Handle(Wing, Ping(0, CommsChannel.Team), 30f, output);
            TestAssert.That(!Refused(output), "the bucket refills");
            output.Clear();
            host.Handle(Third, Ping(0, CommsChannel.Team), 0f, output);
            TestAssert.That(!Refused(output), "one spammer does not starve another player");
        }

        private static void CallsDropAPingAtTheCaller()
        {
            var host = new CommsAuthority(7);
            var output = new List<CommsOutbound>();
            int needSupport = Array.FindIndex(CommsCatalog.Calls, c => c.Code == "NEED SUPPORT");
            host.Handle(Wing, new CommsIntent { Op = CommsOp.Call, Style = (byte)needSupport, Points = new[] { 50, 60 } }, 0f, output);
            TestAssert.That(output.Count == 2 && output[0].Envelope.Event == CommsEvent.Feed &&
                            output[1].Envelope.Event == CommsEvent.Item, "a located call is a feed line plus a ping");
            TestAssert.That(output[1].Envelope.Style == CommsCatalog.PingHelp, "the ping is the call's own kind");

            output.Clear();
            int copy = Array.FindIndex(CommsCatalog.Calls, c => c.Code == "COPY");
            host.Handle(Third, new CommsIntent { Op = CommsOp.Call, Style = (byte)copy, Points = new[] { 50, 60 } }, 0f, output);
            TestAssert.That(output.Count == 1 && output[0].Envelope.Points == null, "an unlocated call never leaks a position");
        }

        private static void PollsCountOneBallotPerVoter()
        {
            var host = new CommsAuthority(7);
            var output = new List<CommsOutbound>();
            host.Handle(Wing, new CommsIntent
            {
                Op = CommsOp.PollCreate, Text = "push now?", Items = new[] { "push", "hold", "PUSH" }, Size = 1,
            }, 0f, output);
            CommsEnvelope created = output[0].Envelope;
            TestAssert.That(created.Event == CommsEvent.Poll && created.Items.Length == 2 && created.Text == "PUSH NOW?",
                "the poll is cleaned and deduplicated");
            TestAssert.That(Math.Abs(created.Ttl - CommsCatalog.PollDurations[1]) < 0.01f, "duration comes from the table");

            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.PollCreate, Text = "again?", Items = new[] { "a", "b" } }, 1f, output);
            TestAssert.That(Refused(output), "one open poll per author");

            output.Clear();
            host.Handle(Wing, Vote(created.Id, 0), 1f, output);
            host.Handle(Third, Vote(created.Id, 0), 1f, output);
            host.Handle(Third, Vote(created.Id, 1), 2f, output);
            host.Handle(Third, Vote(created.Id, 1), 3f, output);
            CommsEnvelope last = output[output.Count - 1].Envelope;
            TestAssert.That(output.Count == 3, "an unchanged ballot sends nothing");
            TestAssert.That(last.Values[0] == 1 && last.Values[1] == 1, "a changed mind moves one vote");

            output.Clear();
            host.Handle(Bandit, Vote(created.Id, 0), 4f, output);
            TestAssert.That(Refused(output), "the other side cannot vote in a team poll");
            host.Handle(Wing, Vote(created.Id, 9), 4f, output);

            output.Clear();
            host.Handle(Third, new CommsIntent { Op = CommsOp.PollClose, Target = created.Id }, 5f, output);
            TestAssert.That(Refused(output), "only the asker or host closes a poll");
            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.PollClose, Target = created.Id }, 5f, output);
            TestAssert.That((output[0].Envelope.Flags & CommsFlags.Closed) != 0 && host.Polls.Count == 0, "the asker closes it");

            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.PollCreate, Text = "?", Items = new[] { "only" } }, 30f, output);
            TestAssert.That(Refused(output), "one option is not a poll");
        }

        private static void PollsCloseOnTime()
        {
            var host = new CommsAuthority(7);
            var output = new List<CommsOutbound>();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.PollCreate, Text = "rtb?", Items = new[] { "rtb", "stay" }, Size = 0 }, 0f, output);
            output.Clear();
            host.Tick(10f, output);
            TestAssert.That(output.Count == 0, "an open poll stays open");
            host.Tick(CommsCatalog.PollDurations[0] + 0.1f, output);
            TestAssert.That(output.Count == 1 && (output[0].Envelope.Flags & CommsFlags.Closed) != 0, "it closes on time with a verdict");
            TestAssert.That(!output[0].Reaches(Bandit.Id, Bandit.Faction), "and the verdict stays on its side");

            var poll = new CommsPoll { Options = new[] { "A", "B" }, Tally = new[] { 2, 2 } };
            TestAssert.That(poll.Leader == -1 && poll.Verdict().StartsWith("TIE"), "a tie has no leader");
            poll.Tally = new[] { 0, 0 };
            TestAssert.That(poll.Verdict() == "NO VOTES", "no votes says so");
            poll.Tally = new[] { 1, 3 };
            TestAssert.That(poll.Leader == 1 && poll.Verdict() == "B 3/4", "a winner is named with its share");
        }

        private static void DuelThrowStaysSecretAndResolves()
        {
            TestAssert.That(RpsChallenge.Resolve(0, 2) == 1 && RpsChallenge.Resolve(1, 0) == 1 && RpsChallenge.Resolve(2, 1) == 1,
                "rock blunts scissors, paper wraps rock, scissors cut paper");
            TestAssert.That(RpsChallenge.Resolve(0, 1) == -1 && RpsChallenge.Resolve(2, 2) == 0, "losses and draws");

            var host = new CommsAuthority(7);
            var output = new List<CommsOutbound>();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.RpsChallenge, Style = 0 }, 0f, output);
            CommsEnvelope open = output[0].Envelope;
            TestAssert.That(open.Values == null && open.Style == 0 && open.Items == null, "the challenge carries no throw");

            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.RpsAccept, Target = open.Id, Style = 1 }, 1f, output);
            TestAssert.That(Refused(output), "you cannot duel yourself");
            output.Clear();
            host.Handle(Bandit, new CommsIntent { Op = CommsOp.RpsAccept, Target = open.Id, Style = 1 }, 1f, output);
            TestAssert.That(Refused(output), "the other side cannot take a team challenge");

            output.Clear();
            host.Handle(Third, new CommsIntent { Op = CommsOp.RpsAccept, Target = open.Id, Style = 1 }, 2f, output);
            CommsEnvelope result = output[0].Envelope;
            TestAssert.That((result.Flags & CommsFlags.Closed) != 0 && result.Values[2] == -1, "paper beats rock");
            TestAssert.That(output[1].Envelope.Event == CommsEvent.Scores && host.Scores.PointsOf(Third.Id) == 2 &&
                            host.Scores.RankOf(Third.Id) == 1, "the winner scores");

            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.RpsChallenge, Style = 2 }, 3f, output);
            output.Clear();
            host.Tick(3f + CommsAuthority.DuelSeconds + 1f, output);
            TestAssert.That(output.Count == 1 && (output[0].Envelope.Flags & CommsFlags.Lapsed) != 0, "an untaken challenge lapses");
        }

        private static void HuntRanksAndRevealsOnlyAtTheEnd()
        {
            var host = new CommsAuthority(7);
            var output = new List<CommsOutbound>();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.HuntStart, Points = StrokeCodec.Point(10000f, 10000f), Size = 0 }, 0f, output);
            CommsEnvelope open = output[0].Envelope;
            TestAssert.That(open.Points == null, "the hidden point never leaves the host early");

            output.Clear();
            host.Handle(Wing, Guess(open.Id, 10000f, 10000f), 1f, output);
            TestAssert.That(Refused(output), "the hider cannot guess");
            output.Clear();
            host.Handle(Third, Guess(open.Id, 12000f, 10000f), 1f, output);
            TestAssert.That(output.Count == 2 && output[0].Envelope.Values[0] == 1 && output[1].Route == CommsRoute.One,
                "a guess bumps the count and is confirmed privately");
            output.Clear();
            host.Handle(Third, Guess(open.Id, 10000f, 10000f), 2f, output);
            TestAssert.That(Refused(output), "one guess per hunt");
            output.Clear();
            host.Handle(Host, Guess(open.Id, 10500f, 10000f), 2f, output);
            output.Clear();
            host.Handle(Bandit, Guess(open.Id, 10000f, 10000f), 2f, output);
            TestAssert.That(Refused(output), "the other side cannot join a team hunt");

            output.Clear();
            host.Tick(CommsCatalog.HuntDurations[0] + 1f, output);
            CommsEnvelope reveal = output[0].Envelope;
            TestAssert.That((reveal.Flags & CommsFlags.Revealed) != 0 && reveal.Points.Length == 6, "the reveal carries point and guesses");
            TestAssert.That(reveal.Items[0] == "HOST" && reveal.Values[0] == 500 && reveal.Values[1] == 4,
                "closest first, with a bullseye bonus");
            TestAssert.That(reveal.Values[3] == 2, "second place scores two");
            TestAssert.That(host.Scores.RankOf(Host.Id) == 1 && host.Hunts.Count == 0, "scores land and the round ends");
        }

        private static void SnapshotShowsOnlyWhatTheViewerMaySee()
        {
            var host = new CommsAuthority(7);
            var output = new List<CommsOutbound>();
            host.Handle(Wing, Ping(0, CommsChannel.Team), 0f, output);
            host.Handle(Wing, Ping(1, CommsChannel.All), 0f, output);
            host.Handle(Bandit, Ping(2, CommsChannel.Team), 0f, output);
            host.Handle(Wing, new CommsIntent { Op = CommsOp.PollCreate, Text = "q", Items = new[] { "a", "b" } }, 0f, output);
            uint pollId = output[output.Count - 1].Envelope.Id;
            host.Handle(Third, Vote(pollId, 1), 0f, output);

            output.Clear();
            host.Snapshot(Bandit, 1f, output);
            int items = 0, polls = 0;
            foreach (CommsOutbound o in output)
            {
                TestAssert.That(o.Route == CommsRoute.One && o.Player == Bandit.Id, "a snapshot goes to its viewer alone");
                if (o.Envelope.Event == CommsEvent.Item) items++;
                if (o.Envelope.Event == CommsEvent.Poll) polls++;
            }
            TestAssert.That(output[0].Envelope.Event == CommsEvent.Reset && (output[0].Envelope.Flags & CommsFlags.Snapshot) != 0,
                "it starts with a full reset");
            TestAssert.That(items == 2 && polls == 0, "the enemy sees their own post and the ALL post, not ours");

            output.Clear();
            host.Snapshot(Third, 1f, output);
            foreach (CommsOutbound o in output)
                if (o.Envelope.Event == CommsEvent.Poll)
                    TestAssert.That(o.Envelope.Style == 2, "a rejoining voter gets their own ballot back");

            var crowded = new CommsAuthority(7);
            var senders = new CommsSender[40];
            for (int i = 0; i < senders.Length; i++)
                senders[i] = new CommsSender { Id = (ulong)(100 + i), Name = "P" + i, Faction = Blue };
            for (int round = 0; round < 8; round++)
                for (int i = 0; i < senders.Length; i++)
                    crowded.Handle(senders[i], Stroke(new[] { round, i, round + 5, i + 5 }), round * 100f, output);
            output.Clear();
            crowded.Snapshot(Third, 900f, output);
            int snapshotItems = 0;
            uint previous = 0;
            foreach (CommsOutbound o in output)
            {
                if (o.Envelope.Event != CommsEvent.Item) continue;
                snapshotItems++;
                TestAssert.That(o.Envelope.Id > previous, "a snapshot sends items oldest first");
                previous = o.Envelope.Id;
            }
            TestAssert.That(crowded.Board.Count > CommsAuthority.SnapshotItems && snapshotItems == CommsAuthority.SnapshotItems,
                "a crowded board is capped to the newest items in one snapshot");
            TestAssert.That(previous == crowded.Board.Items[crowded.Board.Count - 1].Id, "and the newest item is always in it");
        }

        private static void ClientAppliesTheWholeLifecycle()
        {
            var host = new CommsAuthority(7);
            var client = new CommsClientState { LocalId = Third.Id };
            var output = new List<CommsOutbound>();
            host.Handle(Wing, Ping(CommsCatalog.PingSam, CommsChannel.Team), 0f, output);
            host.Handle(Wing, Stroke(new[] { 0, 0, 100, 100, 200, 0 }), 0f, output);
            host.Handle(Wing, new CommsIntent { Op = CommsOp.Roll, Style = 2 }, 0f, output);
            Deliver(output, client, Third, 0f);

            TestAssert.That(client.Board.Count == 2, "the peer holds both items");
            TestAssert.That(client.Feed.Count == 2 && client.Feed[0].Text.StartsWith("SAM THREAT"), "the ping and the roll are logged");
            var arrivals = new List<CommsArrival>();
            client.DrainArrivals(arrivals);
            TestAssert.That(arrivals.Count == 1 && arrivals[0].Ping && arrivals[0].Tone == CommsTone.Danger, "a SAM ping is a danger notice");

            client.Tick(CommsRules.Default.PingSeconds + 1f);
            TestAssert.That(client.Board.CountOf(CommsItemKind.Ping) == 0 && client.Board.CountOf(CommsItemKind.Stroke) == 1,
                "the peer expires the ping on its own clock and keeps the drawing");

            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.PollCreate, Text = "rtb?", Items = new[] { "rtb", "stay" } }, 70f, output);
            Deliver(output, client, Third, 70f);
            TestAssert.That(client.Polls.Count == 1 && !client.Polls[0].Closed, "the poll arrives open");
            client.NoteLocalVote(client.Polls[0].Id, 1);
            output.Clear();
            host.Handle(Third, Vote(client.Polls[0].Id, 1), 71f, output);
            host.Handle(Wing, new CommsIntent { Op = CommsOp.PollClose, Target = client.Polls[0].Id }, 72f, output);
            Deliver(output, client, Third, 72f);
            TestAssert.That(client.Polls[0].Closed && client.Polls[0].LocalVote == 1 && client.Polls[0].Tally[1] == 1,
                "the tally, the close and this peer's own ballot all land");
            client.Tick(72f + CommsClientState.ClosedPollSeconds + 1f);
            TestAssert.That(client.Polls.Count == 0, "a closed poll leaves the panel after a while");

            output.Clear();
            client.NoteLocalHide(0f, 0f);
            host.Handle(Third, new CommsIntent { Op = CommsOp.HuntStart, Points = StrokeCodec.Point(500f, 500f) }, 200f, output);
            Deliver(output, client, Third, 200f);
            TestAssert.That(client.Hunts.Count == 1 && client.Hunts[0].HasLocalGuess, "the hider sees their own hidden point");
            output.Clear();
            host.Handle(Wing, Guess(client.Hunts[0].Id, 600f, 500f), 201f, output);
            host.Tick(200f + CommsCatalog.HuntDurations[0] + 1f, output);
            Deliver(output, client, Third, 240f);
            TestAssert.That(client.Hunts[0].Revealed && client.Hunts[0].Placings.Count == 1 &&
                            client.Hunts[0].Placings[0].Name == "WING", "the reveal lands with its placings");
            TestAssert.That(client.Scores.RankOf(Wing.Id) == 1, "and the leaderboard follows");
            client.Tick(240f + CommsClientState.RevealSeconds + 1f);
            TestAssert.That(client.Hunts.Count == 0, "the reveal clears itself");

            output.Clear();
            host.Snapshot(Third, 300f, output);
            Deliver(output, client, Third, 300f);
            TestAssert.That(client.Board.Count == host.Board.Count, "a snapshot rebuilds the board exactly");
        }

        private static void ClientMutesAndSkipsSelfNoise()
        {
            var host = new CommsAuthority(7);
            var client = new CommsClientState { LocalId = Wing.Id };
            var output = new List<CommsOutbound>();
            host.Handle(Wing, Ping(0, CommsChannel.Team), 0f, output);
            Deliver(output, client, Wing, 0f);
            var arrivals = new List<CommsArrival>();
            client.DrainArrivals(arrivals);
            TestAssert.That(arrivals.Count == 0, "your own ping makes no HUD noise");

            client.ToggleMute(Third.Id);
            output.Clear();
            host.Handle(Third, Ping(1, CommsChannel.Team), 1f, output);
            Deliver(output, client, Wing, 1f);
            client.DrainArrivals(arrivals);
            TestAssert.That(arrivals.Count == 0 && client.IsMuted(Third.Id), "a muted player's ping is silent");
            TestAssert.That(client.Board.Count == 2, "but still held, so unmuting shows it");
            client.ToggleMute(Wing.Id);
            TestAssert.That(!client.IsMuted(Wing.Id), "you cannot mute yourself");
            TestAssert.That(client.Seen.ContainsKey(Third.Id) && !client.Seen.ContainsKey(Wing.Id), "the mute list knows who spoke");

            output.Clear();
            host.Handle(Third, Ping(0, CommsChannel.Team), 2f, output);
            client.Apply(new CommsEnvelope { Event = CommsEvent.Notice, Text = "slow down" }, 2f);
            TestAssert.That(client.Notice == "SLOW DOWN" && client.NoticeIsError, "a refusal reaches the status strip");
        }

        private static void GlyphsStayInTheUnitBox()
        {
            foreach (string key in CommsGlyphs.Keys)
            {
                float[][] strokes = CommsGlyphs.Get(key);
                TestAssert.That(strokes != null && strokes.Length > 0, "glyph draws something: " + key);
                foreach (float[] stroke in strokes)
                {
                    TestAssert.That(stroke.Length >= 4 && stroke.Length % 2 == 0, "glyph strokes are polylines: " + key);
                    foreach (float v in stroke)
                        TestAssert.That(!float.IsNaN(v) && v >= -1.001f && v <= 1.001f, "glyph stays in the unit box: " + key);
                }
            }
            TestAssert.That(CommsGlyphs.Get("no-such-glyph").Length == 1, "an unknown glyph falls back to a ring");
        }

        // ---- helpers -----------------------------------------------------------------------

        private static CommsIntent Ping(int kind, CommsChannel channel) => new CommsIntent
        {
            Op = CommsOp.Place, Kind = (byte)CommsItemKind.Ping, Style = (byte)kind, Channel = channel,
            Points = StrokeCodec.Point(1200f, -800f),
        };

        private static CommsIntent Stroke(int[] points) => new CommsIntent
        {
            Op = CommsOp.Place, Kind = (byte)CommsItemKind.Stroke, Style = 1, Size = 1, Points = points,
        };

        private static CommsIntent Vote(uint poll, int option) =>
            new CommsIntent { Op = CommsOp.PollVote, Target = poll, Style = (byte)option };

        private static CommsIntent Guess(uint hunt, float x, float z) =>
            new CommsIntent { Op = CommsOp.HuntGuess, Target = hunt, Points = StrokeCodec.Point(x, z) };

        private static bool Refused(List<CommsOutbound> output) =>
            output.Count > 0 && output[output.Count - 1].Envelope.Event == CommsEvent.Notice &&
            (output[output.Count - 1].Envelope.Flags & CommsFlags.Self) == 0;

        /// <summary>What the transport does: hand each message to a peer if the route reaches it.</summary>
        private static void Deliver(List<CommsOutbound> output, CommsClientState client, CommsSender viewer, float now)
        {
            foreach (CommsOutbound o in output)
                if (o.Reaches(viewer.Id, viewer.Faction)) client.Apply(o.Envelope, now);
        }
    }
}
