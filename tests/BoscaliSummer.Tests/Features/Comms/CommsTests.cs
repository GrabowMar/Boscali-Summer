using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Comms.Domain;
using BoscaliSummer.Modules.Comms.Presentation;

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
            RemovedSocialActionsAreRejected();
            AuthorBudgetRetiresOldest();
            OnlyAuthorOrHostErases();
            RateLimitStopsSpam();
            CallsDropAPingAtTheCaller();
            SnapshotShowsOnlyWhatTheViewerMaySee();
            ClientAppliesTheWholeLifecycle();
            ClientMutesAndSkipsSelfNoise();
            GlyphsStayInTheUnitBox();
            SnapshotReplaysSilently();
            SyncSkipsTheBucketButIsGated();
            CallPingsHaveTheirOwnSlotAndWords();
            RemovalsStayWithTheirAudience();
            OtherSideIsQuietAndLabelled();
            RefusalsReachTheHud();
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
            TestAssert.That(CommsText.BearingRange(0f, 0f, 3000f, 0f, true) == "BRG 090 · 3.0 KM",
                "a HUD notice says where it is from the reader's aircraft");
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
            var host = new CommsAuthority();
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
            var host = new CommsAuthority();
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
            var host = new CommsAuthority();
            var output = new List<CommsOutbound>();
            int budget = CommsBoard.AuthorBudget(CommsItemKind.Ping);
            for (int i = 0; i < budget; i++) host.Handle(Wing, Ping(0, CommsChannel.Team), i * 5f, output);
            uint first = output[0].Envelope.Id;
            output.Clear();
            host.Handle(Wing, Ping(0, CommsChannel.Team), 100f, output);
            TestAssert.That(host.Board.CountOf(CommsItemKind.Ping) == budget, "the budget holds");
            TestAssert.That(output.Count == 2 && output[1].Envelope.Event == CommsEvent.Remove &&
                            output[1].Envelope.Ids[0] == first, "the oldest ping is retired and announced");
            TestAssert.That(host.Board.Find(first) == null, "and really gone");
        }

        private static void OnlyAuthorOrHostErases()
        {
            var host = new CommsAuthority();
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
            host.Handle(Wing, Stroke(new[] { 0, 0, 9, 9 }), 3f, output);
            host.Handle(Wing, Ping(1, CommsChannel.All), 3f, output);
            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.ClearMine }, 4f, output);
            TestAssert.That(host.Board.Count == 0 && output.Count == 2 &&
                            output[0].Envelope.Ids.Length + output[1].Envelope.Ids.Length == 3, "clear mine takes all of mine");

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
            var host = new CommsAuthority();
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
            var host = new CommsAuthority();
            var output = new List<CommsOutbound>();
            int needSupport = Array.FindIndex(CommsCatalog.Calls, c => c.Code == "NEED SUPPORT");
            host.Handle(Wing, new CommsIntent { Op = CommsOp.Call, Style = (byte)needSupport, Points = new[] { 50, 60 } }, 0f, output);
            TestAssert.That(output.Count == 2 && output[0].Envelope.Event == CommsEvent.Feed &&
                            output[1].Envelope.Event == CommsEvent.Item, "a located call is a feed line plus a ping");
            TestAssert.That(output[1].Envelope.Style == CommsCatalog.PingHelp, "the ping is the call's own kind");

            output.Clear();
            int spike = Array.FindIndex(CommsCatalog.Calls, c => c.Code == "SPIKE");
            host.Handle(Third, new CommsIntent { Op = CommsOp.Call, Style = (byte)spike, Points = new[] { 50, 60 } }, 0f, output);
            TestAssert.That(output[1].Envelope.Style == CommsCatalog.PingSpike, "SPIKE marks the caller, not a SAM site");

            output.Clear();
            int copy = Array.FindIndex(CommsCatalog.Calls, c => c.Code == "COPY");
            host.Handle(Third, new CommsIntent { Op = CommsOp.Call, Style = (byte)copy, Points = new[] { 50, 60 } }, 0f, output);
            TestAssert.That(output.Count == 1 && output[0].Envelope.Points == null, "an unlocated call never leaks a position");
        }

        private static void SnapshotShowsOnlyWhatTheViewerMaySee()
        {
            var host = new CommsAuthority();
            var output = new List<CommsOutbound>();
            host.Handle(Wing, Ping(0, CommsChannel.Team), 0f, output);
            host.Handle(Wing, Ping(1, CommsChannel.All), 0f, output);
            host.Handle(Bandit, Ping(2, CommsChannel.Team), 0f, output);
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

            var crowded = new CommsAuthority();
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
            var host = new CommsAuthority();
            var client = new CommsClientState { LocalId = Third.Id };
            var output = new List<CommsOutbound>();
            host.Handle(Wing, Ping(CommsCatalog.PingSam, CommsChannel.Team), 0f, output);
            host.Handle(Wing, Stroke(new[] { 0, 0, 100, 100, 200, 0 }), 0f, output);
            Deliver(output, client, Third, 0f);

            TestAssert.That(client.Board.Count == 2, "the peer holds both items");
            TestAssert.That(client.Feed.Count == 1 && client.Feed[0].Text.StartsWith("SAM THREAT"), "the ping is logged");
            var arrivals = new List<CommsArrival>();
            client.DrainArrivals(arrivals);
            TestAssert.That(arrivals.Count == 1 && arrivals[0].Sound && arrivals[0].Tone == CommsTone.Danger &&
                            arrivals[0].HasPosition, "a SAM ping is a danger notice with a position");

            client.Tick(CommsRules.Default.PingSeconds + 1f);
            TestAssert.That(client.Board.CountOf(CommsItemKind.Ping) == 0 && client.Board.CountOf(CommsItemKind.Stroke) == 1,
                "the peer expires the ping on its own clock and keeps the drawing");

            output.Clear();
            host.Snapshot(Third, 300f, output);
            Deliver(output, client, Third, 300f);
            TestAssert.That(client.Board.Count == host.Board.Count, "a snapshot rebuilds the board exactly");
        }

        private static void ClientMutesAndSkipsSelfNoise()
        {
            var host = new CommsAuthority();
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

        private static void SnapshotReplaysSilently()
        {
            var host = new CommsAuthority();
            var client = new CommsClientState { LocalId = Third.Id, LocalFaction = Blue };
            var output = new List<CommsOutbound>();
            host.Handle(Wing, Ping(CommsCatalog.PingSam, CommsChannel.Team), 0f, output);
            output.Clear();
            host.Snapshot(Third, 10f, output);
            foreach (CommsOutbound o in output)
                if (o.Envelope.Event == CommsEvent.Item || o.Envelope.Event == CommsEvent.Poll)
                    TestAssert.That((o.Envelope.Flags & CommsFlags.Replay) != 0, "snapshot items and polls are flagged as replays");

            Deliver(output, client, Third, 10f);
            var arrivals = new List<CommsArrival>();
            client.DrainArrivals(arrivals);
            TestAssert.That(client.Board.Count == 1 && client.Polls.Count == 0, "a replay still lands");
            TestAssert.That(client.Feed.Count == 0 && arrivals.Count == 0, "but is neither logged nor announced again");
            TestAssert.That(10f - client.Board.Items[0].Created > 8f, "and does not pulse as if it were new");
        }

        private static void SyncSkipsTheBucketButIsGated()
        {
            var host = new CommsAuthority();
            var output = new List<CommsOutbound>();
            for (int i = 0; i < 40; i++) host.Handle(Wing, Ping(0, CommsChannel.Team), 0f, output);
            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.Sync }, 0f, output);
            TestAssert.That(output.Count > 0 && output[0].Envelope.Event == CommsEvent.Reset, "a busy drawer still gets a snapshot");

            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.Sync }, 1f, output);
            TestAssert.That(output.Count == 1 && output[0].Envelope.Event == CommsEvent.Notice &&
                            (output[0].Envelope.Flags & CommsFlags.Retry) != 0 && (output[0].Envelope.Flags & CommsFlags.Self) != 0,
                "a second snapshot within the gap is a quiet retry, not a burst");
            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.Sync }, 1f + CommsAuthority.SyncGapSeconds, output);
            TestAssert.That(output[0].Envelope.Event == CommsEvent.Reset, "after the gap it is served again");

            var client = new CommsClientState { LocalId = Wing.Id };
            client.Apply(new CommsEnvelope { Event = CommsEvent.Notice, Flags = CommsFlags.Self | CommsFlags.Retry }, 2f);
            TestAssert.That(client.Notice == null, "a retry is for the manager, not the status strip");
        }

        private static void CallPingsHaveTheirOwnSlotAndWords()
        {
            var host = new CommsAuthority();
            var output = new List<CommsOutbound>();
            for (int i = 0; i < CommsBoard.AuthorBudget(CommsItemKind.Ping); i++)
                host.Handle(Wing, Ping(CommsCatalog.PingSam, CommsChannel.Team), i, output);
            int needSupport = Array.FindIndex(CommsCatalog.Calls, c => c.Code == "NEED SUPPORT");
            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.Call, Style = (byte)needSupport, Points = new[] { 50, 60 }, Target = 3200 }, 10f, output);
            TestAssert.That(output.Count == 2, "a call ping retires none of the caller's own pings");
            CommsEnvelope mark = output[1].Envelope;
            TestAssert.That(mark.Size == CommsItem.CallMark && mark.Text == "NEED SUPPORT" && mark.Values[0] == 3200,
                "a call ping names its call and carries the caller's altitude");
            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.Call, Style = (byte)needSupport, Points = new[] { 70, 80 } }, 20f, output);
            TestAssert.That(output.Count == 3 && output[2].Envelope.Event == CommsEvent.Remove &&
                            output[2].Envelope.Ids[0] == mark.Id, "a new call moves the caller's one call mark");

            var client = new CommsClientState { LocalId = Third.Id, LocalFaction = Blue };
            Deliver(output, client, Third, 20f);
            var arrivals = new List<CommsArrival>();
            client.DrainArrivals(arrivals);
            TestAssert.That(client.Feed.Count == 1 && client.Feed[0].Text.StartsWith("NEED SUPPORT"), "a call is logged once, by its own name");
            TestAssert.That(arrivals.Count == 1 && arrivals[0].Text == "WING · NEED SUPPORT" && arrivals[0].HasPosition && arrivals[0].Sound,
                "and announced once, with a position for bearing and range");
            TestAssert.That(client.Board.Items[client.Board.Count - 1].IsCall, "its mark is on the map");

            output.Clear();
            int gg = Array.FindIndex(CommsCatalog.Calls, c => c.Code == "GG");
            host.Handle(Wing, new CommsIntent { Op = CommsOp.Call, Style = (byte)gg }, 30f, output);
            Deliver(output, client, Third, 30f);
            arrivals.Clear();
            client.DrainArrivals(arrivals);
            TestAssert.That(arrivals.Count == 1 && !arrivals[0].Sound, "a GG is shown but never ticks");
        }

        private static void RemovalsStayWithTheirAudience()
        {
            var host = new CommsAuthority();
            var output = new List<CommsOutbound>();
            host.Handle(Wing, Ping(0, CommsChannel.Team), 0f, output);
            uint id = output[0].Envelope.Id;
            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.Erase, Target = id }, 1f, output);
            TestAssert.That(output.Count == 1 && output[0].Envelope.Event == CommsEvent.Remove &&
                            !output[0].Reaches(Bandit.Id, Bandit.Faction), "a team mark's removal never reaches the other side");

            var crowded = new CommsAuthority();
            crowded.Handle(Bandit, Ping(0, CommsChannel.Team), 0f, output);
            uint enemyMark = output[output.Count - 1].Envelope.Id;
            for (int i = 0; i < CommsBoard.MaxPerAudience + 5; i++)
                crowded.Handle(new CommsSender { Id = (ulong)(500 + i), Name = "P", Faction = Blue }, Stroke(new[] { 0, 0, i, i }), i * 10f, output);
            TestAssert.That(crowded.Board.Find(enemyMark) != null, "a crowded side never evicts the other side's marks");
            TestAssert.That(crowded.Board.Count == CommsBoard.MaxPerAudience + 1, "each audience keeps its own ceiling");
        }

        private static void OtherSideIsQuietAndLabelled()
        {
            var host = new CommsAuthority();
            var client = new CommsClientState { LocalId = Wing.Id, LocalFaction = Blue };
            var output = new List<CommsOutbound>();
            host.Handle(Bandit, Ping(CommsCatalog.PingSam, CommsChannel.All), 0f, output);
            Deliver(output, client, Wing, 0f);
            var arrivals = new List<CommsArrival>();
            client.DrainArrivals(arrivals);
            TestAssert.That(arrivals.Count == 1 && arrivals[0].Text.StartsWith("[ALL] ") &&
                            arrivals[0].Tone == CommsTone.Info && !arrivals[0].Sound,
                "an ALL ping from the other side is labelled, calm and silent");
        }

        private static void RefusalsReachTheHud()
        {
            var host = new CommsAuthority();
            var client = new CommsClientState { LocalId = Wing.Id, LocalFaction = Blue };
            var output = new List<CommsOutbound>();
            var arrivals = new List<CommsArrival>();

            client.WatchRefusal(0f);
            host.Rules.AllowAllChannel = false;
            host.Handle(Wing, Ping(0, CommsChannel.All), 1f, output);
            Deliver(output, client, Wing, 1f);
            client.DrainArrivals(arrivals);
            TestAssert.That(arrivals.Count == 1 && arrivals[0].Text.StartsWith("COMMS · "), "a refused ping is told on the HUD");
            output.Clear();
            arrivals.Clear();
            host.Handle(Wing, Ping(0, CommsChannel.All), 20f, output);
            Deliver(output, client, Wing, 20f);
            client.DrainArrivals(arrivals);
            TestAssert.That(arrivals.Count == 0 && client.NoticeIsError, "later refusals stay on the status strip");

        }

        private static void RemovedSocialActionsAreRejected()
        {
            var host = new CommsAuthority();
            var output = new List<CommsOutbound>();
            foreach (CommsOp op in new[] { CommsOp.PollCreate, CommsOp.PollVote, CommsOp.PollClose,
                CommsOp.Roll, CommsOp.RpsChallenge, CommsOp.RpsAccept, CommsOp.RpsCancel,
                CommsOp.HuntStart, CommsOp.HuntGuess })
            {
                output.Clear();
                host.Handle(Wing, new CommsIntent { Op = op, Text = "TEST", Items = new[] { "A", "B" },
                    Points = new[] { 0, 0 } }, (int)op * 100f, output);
                TestAssert.That(output.Count == 1 && Refused(output) && output[0].Player == Wing.Id,
                    "removed action is refused privately: " + op);
            }
            output.Clear();
            host.Handle(Wing, new CommsIntent { Op = CommsOp.Place, Kind = (byte)CommsItemKind.Sticker,
                Points = new[] { 0, 0 } }, 2000f, output);
            TestAssert.That(Refused(output) && host.Board.Count == 0, "stickers cannot reach the board");
            output.Clear();
            host.Snapshot(Wing, 2001f, output);
            TestAssert.That(output.Count == 1 && output[0].Envelope.Event == CommsEvent.Reset,
                "removed actions leave no replay state");
            host.Tick(3000f, output);
            TestAssert.That(output.Count == 1, "removed actions produce no delayed results");

            var client = new CommsClientState { LocalId = Third.Id, LocalFaction = Blue };
            foreach (CommsEvent kind in new[] { CommsEvent.Poll, CommsEvent.Rps, CommsEvent.Hunt, CommsEvent.Scores })
                client.Apply(new CommsEnvelope { Event = kind, Id = 1, Author = Wing.Id,
                    Items = new[] { "A", "B" }, Values = new[] { 1, 2 } }, 0f);
            client.Apply(new CommsEnvelope { Event = CommsEvent.Item, Kind = (byte)CommsItemKind.Sticker,
                Id = 2, Author = Wing.Id, Points = new[] { 0, 0 }, Ttl = 60 }, 0f);
            client.Apply(new CommsEnvelope { Event = CommsEvent.Feed, Kind = (byte)CommsFeedKind.Roll,
                Author = Wing.Id, Text = "DICE" }, 0f);
            TestAssert.That(client.Board.Count == 0 && client.Feed.Count == 0 && client.Polls.Count == 0 &&
                client.Duels.Count == 0 && client.Hunts.Count == 0 && client.Scores.Rows.Count == 0,
                "legacy social envelopes cannot restore removed content");
            var arrivals = new List<CommsArrival>();
            client.DrainArrivals(arrivals);
            TestAssert.That(arrivals.Count == 0, "legacy social content is silent on the HUD");
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
