using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The fixtures the offline harness renders (annex 05 P12 item 3, P2 test 17): legal on the wire, honest, and enough to measure the fold.</summary>
    internal static class StrFixtureTests
    {
        private const string Dash = "—";

        public static void Run()
        {
            SixteenNamedFixturesExist();
            EveryFixtureIsWireLegal();
            TheValidatorCanFail();
            TheUnknownFixtureUsesOnlyTheWireSentinels();
            TheCensusFollowsTheFixture();
            TheEmptyStatesAreDistinct();
            AFixtureModelIsStable();
            TheFoldContentFitsThe596Bay();
            TheNamedVariantsDifferWhereTheyShould();
        }

        private static void SixteenNamedFixturesExist()
        {
            var seen = new HashSet<string>(StrFixtures.Names);
            TestAssert.That(StrFixtures.Names.Length == 16 && seen.Count == 16, "sixteen distinct fixture names");
            foreach (string expected in new[] { "rich", "proposals", "assessing", "noopening", "reduced", "staffoff", "hostlacks", "nofaction", "waiting", "noanswer",
                                                "stale", "unknown", "locked-by-other", "wave-held", "wing-tasked", "client" })
                TestAssert.That(seen.Contains(expected), expected + " is a fixture");
            TestAssert.Throws<ArgumentException>(() => StrFixtures.Feeds("nope"), "an unknown fixture name is an error, not an empty board");
            var stamps = new HashSet<int>();
            foreach (string name in StrFixtures.Names) TestAssert.That(stamps.Add(StrFixtures.Feeds(name).Stamp), name + " has a stamp of its own");
        }

        private static void EveryFixtureIsWireLegal()
        {
            foreach (string name in StrFixtures.Names)
            {
                StrFeeds f = StrFixtures.Feeds(name);
                string problem = StrFixtures.Illegal(in f);
                TestAssert.That(problem == null, name + " is not wire-legal: " + problem);
            }
        }

        private static void TheValidatorCanFail()
        {
            var cases = new Dictionary<string, Action<StrFeeds>>
            {
                ["a NaN own position"] = f => { f.OwnX = float.NaN; Check(f); },
                ["an infinite age"] = f => { f.WarAgeSeconds = float.PositiveInfinity; Check(f); },
                ["nine fronts"] = f => { var l = new List<TheaterFrontView>(f.War.Fronts); while (l.Count < 9) l.Add(l[0]); f.War.Fronts = l.ToArray(); Check(f); },
                ["a 33 character name"] = f => { f.War.Fronts[0].Name = new string('N', 33); Check(f); },
                ["an unobserved front with a hostile count"] = f => { f.War.Fronts[4].NearHostile = 3; Check(f); },
                ["an unobserved front with a pressure"] = f => { f.War.Fronts[4].Pressure = 0.4f; Check(f); },
                ["a pressure above one"] = f => { f.War.Fronts[0].Pressure = 1.5f; Check(f); },
                ["four proposals"] = f => { var l = new List<TheaterProposalView>(f.War.Proposals); l.Add(l[0]); f.War.Proposals = l.ToArray(); Check(f); },
                ["nine log lines"] = f => { f.War.Log = new string[9]; for (int i = 0; i < 9; i++) f.War.Log[i] = "x"; Check(f); },
                ["a 65 character log line"] = f => { f.War.Log[0] = new string('x', 65); Check(f); },
                ["a ninth mark"] = f => { var m = new List<TheaterForceMark>(f.War.Operation.Forces.Marks); while (m.Count < 9) m.Add(m[0]); f.War.Operation.Forces.Marks = m.ToArray(); Check(f); },
                ["a mark at stage six"] = f => { f.War.Operation.Forces.Marks[0].Stage = 6; Check(f); },
                ["33 groups"] = f => { f.War.Operation.Forces.Groups = 33; Check(f); },
                ["a lock of 121 seconds"] = f => { f.War.Operation.LockSeconds = 121f; Check(f); },
                ["a wave that is off with no block"] = f => { f.War.Wave = new TheaterWaveView(WaveState.Off, WaveBlock.PoolShort, 0, 3, "", 1, 1, 0, 0, -1f, false, 0f, 0f); Check(f); },
                ["a wave that is blocked with no block"] = f => { f.War.Wave = new TheaterWaveView(WaveState.Blocked, WaveBlock.None, 3, 3, "", 1, 1, 0, 0, -1f, false, 0f, 0f); Check(f); },
                ["seven credits"] = f => { f.War.Wave = new TheaterWaveView(WaveState.Earning, WaveBlock.None, 7, 3, "", 1, 1, 0, 0, -1f, false, 0f, 0f); Check(f); },
                ["an unmeasured census with a count"] = f => { f.War.Census = TheaterCensus.Unmeasured; f.War.Census.Radars = 3; f.Census = f.War.Census; Check(f); },
                ["an unmeasured census with a threat"] = f => { f.Census = TheaterCensus.Unmeasured; f.Census.Threat = 2; Check(f); },
                ["a census count of -2"] = f => { f.War.Census.AirFriendly = -2; Check(f); },
                ["a threat of six"] = f => { f.War.Census.Threat = 6; Check(f); },
                ["an old post"] = f => { f.Posts[0].IntelAge = 601f; Check(f); },
                ["seventeen officers"] = f => { f.Officers = new StrOfficerIn[17]; f.OfficerCount = 17; Check(f); },
                ["an officer at tier three"] = f => { f.Officers[0].Tier = 3; Check(f); },
            };
            foreach (var pair in cases)
            {
                StrFeeds f = StrFixtures.Feeds("rich");
                TestAssert.That(StrFixtures.Illegal(in f) == null, "the control fixture is legal");
                pair.Value(f);
            }
        }

        private static void Check(StrFeeds f) => TestAssert.That(StrFixtures.Illegal(in f) != null, "the validator missed a feed that is not wire-legal");

        private static void TheUnknownFixtureUsesOnlyTheWireSentinels()
        {
            StrFeeds f = StrFixtures.Feeds("unknown");
            TheaterWarSnapshot s = f.War;
            foreach (TheaterFrontView fv in s.Fronts)
                TestAssert.That(!fv.Observed && fv.NearHostile == 255 && fv.NearFriendly == 255 && fv.ObservedAgeSeconds == -1f && fv.Pressure == 0f, "every front is unobserved with the unknown encodings");
            foreach (TheaterProposalView p in s.Proposals)
                TestAssert.That(p.FriendlyGround == 255 && p.FriendlyOther == 255 && p.HostileTracked == 255 && p.Risk == TheaterRisk.Unknown, "every proposal count is unknown");
            TheaterForcesView fo = s.Operation.Forces;
            TestAssert.That(fo.Groups == 255 && fo.Vehicles == 255 && fo.Formed == 255 && fo.Moving == 255 && fo.Steered == 255 && fo.OnSlot == 255 && fo.Stalled == 255 &&
                            fo.Rally == 255 && fo.Line == 255 && fo.Assault == 255 && fo.Contact == 255 && fo.Withdraw == 255, "every force count is 255");
            TestAssert.That(fo.EtaSeconds == 65535 && fo.NearestToFixMeters == -1f && fo.Marks.Length == 0 && s.Operation.Progress == -1f, "no eta, no distance, no progress");
            TheaterWaveView w = s.Wave;
            TestAssert.That(w.Cost == -1 && w.Pool == -1 && w.CooldownSeconds == -1 && w.LastBoughtAgo == -1f && !w.DepotKnown, "the wave knows nothing");
            TestAssert.That(!f.Census.Known && !s.Census.Known && f.WarAgeSeconds == -1f && !f.HasOwn && f.KnownAirDefence == -1, "no census, no age, no position, no air defence");
            string problem = StrFixtures.Illegal(in f);
            TestAssert.That(problem == null, "and all of it is wire-legal: " + problem);
        }

        private static void TheCensusFollowsTheFixture()
        {
            StrFeeds off = StrFixtures.Feeds("staffoff");
            TestAssert.That(off.Census.Known && off.CensusAgeSeconds >= 0f, "staffoff carries the census the host measured itself");
            foreach (string name in new[] { "hostlacks", "nofaction", "waiting", "noanswer" })
            {
                StrFeeds f = StrFixtures.Feeds(name);
                TestAssert.That(!f.Census.Known && f.Census.Threat == 0 && f.Census.AirFriendly == -1 && f.Census.SectorsFriendly == -1 && f.Census.FrontMetres == -1f, name + " carries TheaterCensus.Unmeasured");
                TestAssert.That(f.War == null, name + " has no snapshot");
            }
            TestAssert.That(StrFixtures.Feeds("rich").Census.Known && StrFixtures.Feeds("reduced").Census.Known, "a snapshot brings its census");
        }

        private static void TheEmptyStatesAreDistinct()
        {
            var seen = new HashSet<string>();
            foreach (string name in new[] { "assessing", "noopening", "reduced", "staffoff", "hostlacks", "nofaction", "waiting", "noanswer" })
            {
                StrViewModel vm = StrFixtures.Model(name);
                TestAssert.That(vm.Banner.Show && vm.Banner.Chip.Length >= 4 && vm.Banner.Head.Length >= 8, name + " shows a banner with a chip and a headline");
                TestAssert.That(seen.Add(vm.Banner.Chip + "|" + vm.Banner.Head + "|" + vm.FooterNote), name + " has a chip, headline and flavour of its own");
                TestAssert.That(vm.FooterStatus.Length > 8 && vm.Banner.Why.Length + vm.Banner.Do.Length > 12, name + " says why and what to do");
            }
        }

        private static void AFixtureModelIsStable()
        {
            foreach (string name in StrFixtures.Names)
            {
                StrViewModel a = StrFixtures.Model(name), b = StrFixtures.Model(name);
                TestAssert.That(a.Revision == 1 && b.Revision == 1 && a.Mode == b.Mode && a.FooterStatus == b.FooterStatus && a.FooterNote == b.FooterNote, name + " builds the same model twice");
            }
        }

        // The fold rule at 596 px: the footer overlays the last 76 px, so the first ACCEPT and the wave strip must end above 520. The geometry is the
        // Unity harness's; what decides it is how many tickets the hero lane draws and what the first of them is.
        private static void TheFoldContentFitsThe596Bay()
        {
            TestAssert.That(StrViewBuilder.BayHeight == 596f && StrViewBuilder.FoldLine == 520f && StrViewBuilder.HeroTickets == 3, "the bay, the fold and the ticket cap");
            StrViewModel rich = StrFixtures.Model("rich");
            TestAssert.That(rich.Mode == StrBoardMode.Operation && rich.Effort.Live && rich.Wave.Present, "rich is a live board with a wave strip");
            TestAssert.That(StrViewBuilder.TicketsDrawn(rich) == 3 && StrViewBuilder.MoreOnTheMap(rich) == 0, "three tickets under the hero lane");
            int firstOffered = -1;
            for (int i = 0; i < rich.SortieCount && firstOffered < 0; i++) if (rich.Sorties[i].Offered) firstOffered = i;
            TestAssert.That(firstOffered >= 0 && firstOffered < StrViewBuilder.TicketsDrawn(rich) && rich.Sorties[firstOffered].Accept.Allowed, "the first ACCEPT is on a drawn ticket and is not disabled");
            TestAssert.That(rich.Sorties[firstOffered].WingLabel.Length > 0 && rich.Wave.StateText.Length > 0, "the wing button and the wave strip both have words to lay out");

            StrViewModel proposals = StrFixtures.Model("proposals");
            TestAssert.That(proposals.ProposalCount == 3 && proposals.Proposals[0].Pick.Allowed && !proposals.Effort.Live, "the first PICK exists and is allowed");

            // A crowded board still draws three tickets and leaves the rest to the map.
            StrFeeds f = StrFixtures.Feeds("rich");
            var seven = new List<SecondaryObjectiveView>();
            for (int i = 0; i < 7; i++) seven.Add(StrFixtures.Card(70 + i, "CARD " + i, ObjectiveFamily.Strike, true, false, 300f + i));
            f.Sorties = seven.ToArray();
            f = StrFixtures.Stamped(f);
            var vm = new StrViewModel(); var ui = default(StrUiState);
            StrViewBuilder.Build(vm, in f, in ui, new StrOrdinals("P"), new StrOrdinals("S"), StrFixtures.Now);
            TestAssert.That(vm.SortieCount == 5 && vm.SortieMore == 2 && StrViewBuilder.TicketsDrawn(vm) == 3 && StrViewBuilder.MoreOnTheMap(vm) == 4, "seven tied cards: five kept, three drawn, four left to the map");
            TestAssert.That(StrViewBuilder.TicketsDrawn(null) == 0 && StrViewBuilder.MoreOnTheMap(null) == 0, "no model, no tickets");
        }

        private static void TheNamedVariantsDifferWhereTheyShould()
        {
            StrViewModel locked = StrFixtures.Model("locked-by-other");
            TestAssert.That(locked.Proposals[0].Pick.Why == StrWhy.Locked && !locked.Effort.PickedByLocal && locked.Effort.LockSeconds > 0, "locked-by-other: another pilot holds the effort");
            StrViewModel held = StrFixtures.Model("wave-held");
            TestAssert.That(held.Wave.Held && held.Wave.Affordable == StrTri.No && held.Wave.Credits == 3, "wave-held: three credits, a pool that is short");
            StrViewModel tasked = StrFixtures.Model("wing-tasked");
            bool found = false;
            for (int i = 0; i < tasked.SortieCount; i++) if (tasked.Sorties[i].WingLabel == "RECALL" && tasked.Sorties[i].Wing.Allowed) found = true;
            TestAssert.That(found, "wing-tasked: a ticket offers RECALL");
            StrViewModel client = StrFixtures.Model("client");
            TestAssert.That(!client.IsHost && client.Sorties[0].Wing.Why == StrWhy.HostOnly, "client: not the host");
            StrViewModel stale = StrFixtures.Model("stale");
            TestAssert.That(stale.Stale && stale.Proposals[0].Pick.Why == StrWhy.Stale, "stale: the report is old and orders wait");
            StrViewModel waiting = StrFixtures.Model("waiting");
            TestAssert.That(waiting.Mode == StrBoardMode.Waiting && waiting.AgeText == Dash && !waiting.IsHost, "waiting: a client with nothing yet");
        }
    }
}
