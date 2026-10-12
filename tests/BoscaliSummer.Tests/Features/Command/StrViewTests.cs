using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The Station Board builder (annex 05 P2): feeds in, words, numbers and gates out. Every test builds from wire-legal feeds.</summary>
    internal static class StrViewTests
    {
        private static readonly Regex ZeroPercent = new Regex(@"\b0\s*%");
        private const string Dash = "\u2014";

        public static void Run()
        {
            EveryFixtureModeIsChosenAndSpeaks();
            ModeRulesFollowTheSpec();
            AnUnknownIsADashEverywhere();
            FrontsAreNumberedOrderedAndCapped();
            TheOperationReadsItsRouteForcesAndGroups();
            TheLockCountsDownFromTheReportAge();
            TheWaveIsPricedUpFrontAndHeldNeverDropped();
            ProposalsCarryTheirComposedAimAndTheSameQuote();
            SortiesAreTiedOrderedAndCapped();
            PostsAreRichestFirstAndNeverFresherThanTheirFeed();
            TilesAndSitrepAgreeAndRefuseToGuess();
            TheAlertLevelIsTheCensusThreat();
            TheFooterSaysTheMostUrgentThing();
            TheNoOpeningChecklistFailsExactlyOneRow();
            CountdownsReadTheEnginesClocks();
            ASelectionLivesOnlyWhileItsRowDoes();
            TheRevisionBumpsOnlyOnAChange();
            ASteadyBuildAllocatesNothing();
            AMalformedFeedNeverThrowsOrExceedsACap();
            AStaleFeedDisablesOnlyItsOwnVerbs();
            TheClientIsToldWhoCanSendTheWing();
            TheCensusFollowsTheMode();
        }

        // ---- helpers ----------------------------------------------------------------------------------------------------------

        private static StrViewModel Build(StrFeeds f, StrUiState ui = default, float now = StrFixtures.Now)
        {
            f = StrFixtures.Stamped(f);
            var vm = new StrViewModel();
            StrViewBuilder.Build(vm, in f, in ui, new StrOrdinals("P"), new StrOrdinals("S"), now);
            return vm;
        }

        private static StrViewModel Fixture(string name) => Build(StrFixtures.Feeds(name));

        private static IEnumerable<string> Texts(StrViewModel vm)
        {
            yield return vm.AgeText; yield return vm.RadarsText; yield return vm.KnownAdText; yield return vm.FrontText; yield return vm.ContestedBasesText;
            yield return vm.AlertWord; yield return vm.AlertRule; yield return vm.FooterStatus; yield return vm.FooterNote;
            yield return vm.Banner.Chip; yield return vm.Banner.Head; yield return vm.Banner.Why; yield return vm.Banner.Do; yield return vm.Banner.CountLabel;
            for (int i = 0; i < vm.FrontCount; i++) { StrFrontRow r = vm.Fronts[i]; yield return r.PressureText; yield return r.TrendText; yield return r.StatusText; yield return r.OfferText; }
            for (int i = 0; i < vm.ProposalCount; i++) { StrProposalRow r = vm.Proposals[i]; yield return r.Aim; yield return r.WaveText; yield return r.RiskText; }
            StrEffortView e = vm.Effort;
            yield return e.PressureText; yield return e.TrendText; yield return e.VehiclesText; yield return e.GroupsText; yield return e.MovementText; yield return e.EtaText; yield return e.PhaseText;
            for (int i = 0; i < e.MarkCount; i++) yield return e.Marks[i].DistanceText;
            for (int i = 0; i < vm.SortieCount; i++) { StrSortieRow r = vm.Sorties[i]; yield return r.DistanceText; yield return r.RewardText; yield return r.TimeText; yield return r.WingText; }
            for (int i = 0; i < vm.PostCount; i++) { StrPostRow r = vm.Posts[i]; yield return r.BountyText; yield return r.SeenText; }
            yield return vm.Wave.StateText; yield return vm.Wave.PoolText; yield return vm.Wave.LastText;
            foreach (StrTile t in vm.Tiles) { yield return t.Value; yield return t.Caption; }
            yield return vm.Sectors.Text; yield return vm.Air.Text; yield return vm.Ground.Text; yield return vm.Airbases.Text;
        }

        private static StrFrontRow FrontNamed(StrViewModel vm, string name)
        {
            for (int i = 0; i < vm.FrontCount; i++) if (vm.Fronts[i].Name == name) return vm.Fronts[i];
            return null;
        }

        // ---- rule 1 ----------------------------------------------------------------------------------------------------------

        private static void EveryFixtureModeIsChosenAndSpeaks()
        {
            var expected = new Dictionary<string, StrBoardMode>
            {
                ["rich"] = StrBoardMode.Operation, ["proposals"] = StrBoardMode.Proposals, ["assessing"] = StrBoardMode.Assessing, ["noopening"] = StrBoardMode.NoOpening,
                ["reduced"] = StrBoardMode.Reduced, ["staffoff"] = StrBoardMode.StaffOff, ["hostlacks"] = StrBoardMode.HostLacksStaff, ["nofaction"] = StrBoardMode.NoFaction,
                ["waiting"] = StrBoardMode.Waiting, ["noanswer"] = StrBoardMode.NoAnswer, ["stale"] = StrBoardMode.Operation, ["unknown"] = StrBoardMode.Operation,
                ["locked-by-other"] = StrBoardMode.Operation, ["wave-held"] = StrBoardMode.Operation, ["wing-tasked"] = StrBoardMode.Operation, ["client"] = StrBoardMode.Operation,
            };
            var notes = new HashSet<string>();
            foreach (string name in StrFixtures.Names)
            {
                StrViewModel vm = Fixture(name);
                TestAssert.That(vm.Mode == expected[name], name + " is " + expected[name] + " not " + vm.Mode);
                TestAssert.That(vm.FooterStatus.Length > 8 && vm.FooterNote.Length > 8, name + " has both footer lines");
                bool showsBanner = vm.Mode != StrBoardMode.Operation || vm.Stale;
                TestAssert.That(vm.Banner.Show == showsBanner, name + " shows its banner exactly when it has something to say");
                if (showsBanner) TestAssert.That(vm.Banner.Chip.Length >= 4 && vm.Banner.Head.Length >= 8, name + " banner has a chip and a headline");
                if (vm.Mode != StrBoardMode.Operation) TestAssert.That(notes.Add(vm.FooterNote), name + " has a flavour line no other mode shares");
            }
        }

        private static void ModeRulesFollowTheSpec()
        {
            StrFeeds waiting = StrFixtures.Feeds("waiting");
            waiting.WaitSeconds = 29f;
            TestAssert.That(Build(waiting).Mode == StrBoardMode.Waiting, "29 s of waiting is still waiting");
            waiting.WaitSeconds = 30f;
            TestAssert.That(Build(waiting).Mode == StrBoardMode.NoAnswer, "30 s of waiting is no answer");

            StrFeeds noSnap = StrFixtures.Base();
            TestAssert.That(Build(noSnap).Mode == StrBoardMode.Waiting, "a live channel with no snapshot yet is waiting");
            noSnap.WarState = TheaterViewState.Stale;
            TestAssert.That(Build(noSnap).Mode == StrBoardMode.Waiting, "so is a stale one: there is nothing to keep showing");

            StrViewModel staffOff = Fixture("staffoff");
            TestAssert.That(staffOff.Banner.Do.Contains("restart"), "StaffOff tells the pilot to restart");
            StrViewModel reduced = Fixture("reduced");
            TestAssert.That(reduced.Banner.Do.Contains("sorties") && reduced.Banner.Head == "NO GROUND WAR HERE", "Reduced (no ground war) points at the sortie desk");
            StrViewModel noAnswer = Fixture("noanswer");
            TestAssert.That(noAnswer.Banner.Why == StrWords.NoAnswerWhy() && noAnswer.Banner.Do == StrWords.NoAnswerDo(), "NoAnswer uses its own why and do");
            TestAssert.That(Fixture("hostlacks").Banner.Do.Contains("different build") || Fixture("hostlacks").Banner.Do.Contains("ask the host"), "HostLacksStaff says what to do");

            // The reduced reasons by title.
            foreach (var pair in new[] { (TheaterEmptyReason.TacticsOff, "GROUND STEERING OFF"), (TheaterEmptyReason.RtsOwnsOrders, "RTS HAS THE GROUND"), (TheaterEmptyReason.NoGroundWar, "NO GROUND WAR HERE") })
            {
                StrFeeds f = StrFixtures.Live(StrFixtures.Reason(StrFixtures.Snapshot(), pair.Item1, 0f));
                StrViewModel vm = Build(f);
                TestAssert.That(vm.Mode == StrBoardMode.Reduced && vm.Banner.Head == pair.Item2 && vm.Banner.Do.Length > 8, pair.Item1 + " reads " + pair.Item2);
            }

            // N-16: a deck or an operation wins over a Reduced reason.
            TheaterWarSnapshot withDeck = StrFixtures.Deck(StrFixtures.Reason(StrFixtures.Snapshot(), TheaterEmptyReason.NoGroundWar, 0f));
            TestAssert.That(Build(StrFixtures.Live(withDeck)).Mode == StrBoardMode.Proposals, "a deck wins over Reduced");
            TheaterWarSnapshot withOp = StrFixtures.Reason(StrFixtures.Snapshot(), TheaterEmptyReason.NoGroundWar, 0f);
            withOp.Operation = StrFixtures.Operation(21, TheaterOpKind.Defend, "#9", "PORT AURELIA", 3000f, 5200f, TheaterPhase.Holding);
            TestAssert.That(Build(StrFixtures.Live(withOp)).Mode == StrBoardMode.Operation, "an operation wins over Reduced");

            StrViewModel stale = Fixture("stale");
            TestAssert.That(stale.Stale && stale.Mode == StrBoardMode.Operation && stale.FrontCount > 0, "a stale report keeps showing the last board");
            TestAssert.That(stale.Banner.Show && stale.Banner.Head == StrWords.StaleReport(22f) && stale.AgeText == "22s", "and says how old it is");
            TestAssert.That(stale.FooterStatus.Contains("BOARD STALE"), "the strip says it too");
            TheaterWarSnapshot deck = StrFixtures.Deck(StrFixtures.Snapshot());
            StrFeeds staleDeck = StrFixtures.Live(deck); staleDeck.WarState = TheaterViewState.Stale; staleDeck.WarAgeSeconds = 30f;
            StrViewModel sd = Build(staleDeck);
            TestAssert.That(sd.Mode == StrBoardMode.Proposals && sd.Banner.Do.Contains("ORDERS WAIT FOR THE HOST"), "a stale deck keeps its banner and adds the stale line");
        }

        // ---- rule 3 ----------------------------------------------------------------------------------------------------------

        private static void AnUnknownIsADashEverywhere()
        {
            // A front that was never observed prints a dash whatever number the wire carried.
            TheaterWarSnapshot s = StrFixtures.Snapshot();
            s.Fronts[4].Pressure = 0.4f; s.Fronts[4].Trend = 0.3f;
            StrViewModel loud = Build(StrFixtures.Live(s));
            StrFrontRow flats = FrontNamed(loud, "EAST FLATS");
            TestAssert.That(flats != null && flats.PressureText == Dash && float.IsNaN(flats.Pressure) && float.IsNaN(flats.Trend) && flats.TrendText == "", "an unobserved front is a dash and no trend");
            TestAssert.That(flats.StatusText == "NOT OBSERVED" + StrWords.Dot + "6 MIN OLD" && flats.OfferText == "NO REPORT", "and says how old the last report is");

            // A real zero, observed, is a zero.
            TheaterWarSnapshot calm = StrFixtures.Snapshot();
            calm.Fronts[3].Pressure = 0f;
            TestAssert.That(FrontNamed(Build(StrFixtures.Live(calm)), "NORTH RIDGE").PressureText == "0%", "an observed zero is printed as one");

            StrViewModel u = Fixture("unknown");
            foreach (string text in Texts(u)) TestAssert.That(text == null || !ZeroPercent.IsMatch(text), "no unknown prints a zero percent: " + text);
            TestAssert.That(u.AgeText == Dash && float.IsNaN(u.AgeSeconds), "an unknown age is a dash");
            foreach (StrTile t in u.Tiles) TestAssert.That(t.Value == Dash && t.State == NOAvionics.AvState.Inert && float.IsNaN(t.Bar), t.Key + " tile is a dash and inert");
            TestAssert.That(u.Sectors.Text == Dash && u.Air.Text == Dash && u.Ground.Text == Dash && u.Airbases.Text == Dash, "every SITREP balance is a dash");
            TestAssert.That(u.RadarsText == Dash && u.KnownAdText == "NOT AVAILABLE" && u.FrontText.StartsWith(Dash + StrWords.Dot) && u.ContestedBasesText == Dash, "sensors and the front length are dashes, the platform count is a fact");
            TestAssert.That(u.AlertLevel == 0 && u.AlertWord == "NOT MEASURED" && u.AlertRule == "", "an unmeasured census lights no alert");
            foreach (int count in u.SortieCounts) TestAssert.That(count == -1, "sortie counts stay unknown");
            foreach (float share in u.SortieShares) TestAssert.That(float.IsNaN(share), "sortie shares stay unknown");
            TestAssert.That(u.Wave.Pool == -1 && u.Wave.Cost == -1 && u.Wave.Affordable == StrTri.Unknown, "no price and no pool is not a free wave");
            StrEffortView e = u.Effort;
            TestAssert.That(e.VehiclesText == Dash && e.GroupsText == Dash && e.EtaText == "ETA " + Dash && e.MarkCount == 0, "unknown forces are dashes");
            TestAssert.That(e.MovementText == "MOVING " + Dash + StrWords.Dot + "ON SLOT " + Dash + StrWords.Dot + "STALLED " + Dash, "each force number is its own dash");
            TestAssert.That(float.IsNaN(e.Pressure) && e.PressureText == Dash && e.LockSeconds == -1 && e.PickedText == "SET BY STAFF", "the lane has no pressure and no lock");
            for (int i = 0; i < u.ProposalCount; i++)
                TestAssert.That(u.Proposals[i].RiskText == Dash && u.Proposals[i].Aim.Contains(Dash + " FRIENDLY NEARBY, " + Dash + " TRACKED HOSTILE"), "an unknown proposal count is a dash");
            TestAssert.That(u.SortieCount == 1 && u.Sorties[0].DistanceText == Dash && u.Sorties[0].RewardText == Dash && u.Sorties[0].TimeText == Dash, "a sortie with no own position, reward or clock is dashes");
            TestAssert.That(u.PostCount == 0, "no post feed, no posts");
        }

        // ---- rule 4 ----------------------------------------------------------------------------------------------------------

        private static void FrontsAreNumberedOrderedAndCapped()
        {
            StrViewModel rich = Fixture("rich");
            TestAssert.That(rich.FrontCount == 4 && FrontNamed(rich, "KESTREL CROSSING") == null, "the operation's front is the hero lane, not a compact lane");
            string order = rich.Fronts[0].Name + "|" + rich.Fronts[1].Name + "|" + rich.Fronts[2].Name + "|" + rich.Fronts[3].Name;
            TestAssert.That(order == "HARBOR|PORT AURELIA|NORTH RIDGE|EAST FLATS", "pressure descending, the unobserved one last: " + order);
            TestAssert.That(rich.Fronts[0].Tag == "P2" && rich.Fronts[1].Tag == "P3" && rich.Fronts[3].Tag == "P5" && rich.Effort.FrontTag == "P1", "a platform keeps its first-sighting number");
            TestAssert.That(rich.Fronts[0].StatusText == "UNDER PRESSURE" && rich.Fronts[1].StatusText == "HOLDING" && rich.Fronts[2].StatusText == "QUIET", "status words");
            TestAssert.That(rich.Fronts[0].PressureText == "80%" && rich.Fronts[0].TrendText == StrWords.Up + "RISING" && rich.Fronts[1].TrendText == StrWords.Down + "FALLING" &&
                            rich.Fronts[2].TrendText == StrWords.Level + "STEADY", "percent and trend words");
            TestAssert.That(rich.Fronts[0].Ink == StrInkRole.Caution && rich.Fronts[1].Ink == StrInkRole.Own && rich.Fronts[3].Ink == StrInkRole.Muted, "lane inks");
            TestAssert.That(rich.Fronts[0].OfferText == "O2 ASSAULT" + StrWords.Dot + "HIGH" && rich.Fronts[1].OfferText == "O1 DEFEND" + StrWords.Dot + "MODERATE" &&
                            rich.Fronts[2].OfferText == "NO OFFER" && rich.Fronts[3].OfferText == "O3 RECON" + StrWords.Dot + Dash, "each lane names its offer, or says there is none");
            TestAssert.That(rich.Fronts[0].Offer == new StrItemId(StrItemKind.Proposal, 23) && rich.Fronts[2].Offer.IsNone, "the offer is the proposal id");

            // 12 in, 8 out; equal pressure by platform number; unknown last; the same twice.
            TheaterWarSnapshot big = StrFixtures.Snapshot();
            var list = new List<TheaterFrontView>();
            for (int i = 0; i < 12; i++)
                list.Add(StrFixtures.Front("@" + i, "PLACE " + i, i * 100f, 0f, i % 3 == 0 ? 0.5f : 0.2f + i * 0.01f, 0f, TheaterFrontStatus.Holding, observed: i != 5));
            big.Fronts = list.ToArray();
            StrFeeds f = StrFixtures.Live(big);
            StrViewModel a = Build(f), b = Build(f);
            TestAssert.That(a.FrontCount == 8, "twelve fronts in, eight out");
            for (int i = 0; i < 8; i++) TestAssert.That(a.Fronts[i].Name == b.Fronts[i].Name, "the same build twice orders alike");
            TestAssert.That(a.Fronts[0].Name == "PLACE 0" && a.Fronts[1].Name == "PLACE 3" && a.Fronts[2].Name == "PLACE 6", "equal pressure is ordered by platform number");
            for (int i = 1; i < 8; i++)
                TestAssert.That(float.IsNaN(a.Fronts[i].Pressure) ? float.IsNaN(a.Fronts[i].Pressure) : (float.IsNaN(a.Fronts[i - 1].Pressure) ? false : a.Fronts[i - 1].Pressure >= a.Fronts[i].Pressure), "pressure never rises down the list");

            // Reduced: one air picture lane from the census, hostile share of tracked air.
            StrViewModel red = Fixture("reduced");
            TestAssert.That(red.FrontCount == 1 && red.Fronts[0].Name == "AIR PICTURE" && red.Fronts[0].PressureText == "33%", "a mission without a ground war shows the air picture");
            StrFeeds noAir = StrFixtures.Feeds("reduced"); noAir.War.Census = TheaterCensus.Unmeasured; noAir.Census = TheaterCensus.Unmeasured;
            StrViewModel un = Build(noAir);
            TestAssert.That(un.Fronts[0].PressureText == Dash && un.Fronts[0].StatusText == "NOT MEASURED", "and a dash when the air is not measured");
        }

        // ---- rule 6 ----------------------------------------------------------------------------------------------------------

        private static void TheOperationReadsItsRouteForcesAndGroups()
        {
            StrEffortView e = Fixture("rich").Effort;
            TestAssert.That(e.Live && e.Tag == "01" && e.KindText == "MAIN EFFORT" && e.Name == "KESTREL CROSSING" && e.Id == new StrItemId(StrItemKind.Effort, 21), "the effort names itself");
            TestAssert.That(e.PhaseText == "ADVANCING" && e.RouteSteps == 3 && e.RouteStep == 2 && e.Advancing && !e.Alarm, "an advancing assault is on step 2 of 3");
            TestAssert.That(e.PickedText == "SET BY VIPER 1-1" && e.PickedBy == "Viper 1-1" && e.PickedByLocal && !e.PickedByStaff, "the picker is named");
            TestAssert.That(e.PressureText == "62%" && e.TrendText == StrWords.Up + "RISING", "the hero lane reads its front");
            TestAssert.That(e.VehiclesText == "11 VEH" && e.GroupsText == "3 GRP" + StrWords.Dot + "1 LINE" + StrWords.Dot + "2 ASSAULT", "vehicles and groups with the stages that are above zero");
            TestAssert.That(e.MovementText == "MOVING 5" + StrWords.Dot + "ON SLOT 2" + StrWords.Dot + "STALLED 1" && e.EtaText == "ETA 04:10", "moving, on slot and stalled are separate numbers");
            TestAssert.That(e.MarkCount == 3 && e.Marks[0].Tag == "G1" && e.Marks[2].Tag == "G3" && e.Marks[0].DistanceM <= e.Marks[1].DistanceM && e.Marks[1].DistanceM <= e.Marks[2].DistanceM,
                "groups are ranked nearest the fix first");
            TestAssert.That(e.Marks[2].DistanceText.EndsWith(" KM") && e.Marks[0].Stage == 3, "marks carry distance words and the engine stage");

            // A malformed feed with the marks the wrong way round is still ranked nearest first.
            StrFeeds f = StrFixtures.Feeds("rich");
            Array.Reverse(f.War.Operation.Forces.Marks);
            StrEffortView ranked = Build(f).Effort;
            TestAssert.That(ranked.Marks[0].DistanceM <= ranked.Marks[2].DistanceM, "the builder ranks them itself");

            // Phase to step; Withdrawing raises the alarm; notes are appended.
            var cases = new[]
            {
                (TheaterOpKind.Assault, TheaterPhase.Awaiting, 0), (TheaterOpKind.Assault, TheaterPhase.Forming, 1), (TheaterOpKind.Assault, TheaterPhase.InContact, 3),
                (TheaterOpKind.Defend, TheaterPhase.Holding, 2), (TheaterOpKind.Recon, TheaterPhase.Scouting, 2), (TheaterOpKind.Defend, TheaterPhase.Advancing, 2),
            };
            foreach (var c in cases)
            {
                StrFeeds g = StrFixtures.Feeds("rich");
                g.War.Operation.Kind = c.Item1; g.War.Operation.Phase = c.Item2;
                StrEffortView ev = Build(g).Effort;
                TestAssert.That(ev.RouteStep == c.Item3 && ev.RouteSteps == (c.Item1 == TheaterOpKind.Assault ? 3 : 2), c.Item1 + " in " + c.Item2 + " is on step " + c.Item3);
            }
            StrFeeds w = StrFixtures.Feeds("rich"); w.War.Operation.Phase = TheaterPhase.Withdrawing;
            StrEffortView wd = Build(w).Effort;
            TestAssert.That(wd.Alarm && wd.RouteStep == wd.RouteSteps && !wd.Advancing, "withdrawing raises the alarm and stays on the last step");
            StrFeeds n = StrFixtures.Feeds("rich"); n.War.Operation.Phase = TheaterPhase.Awaiting; n.War.Operation.PhaseNote = TheaterPhaseNote.NoSteering;
            TestAssert.That(Build(n).Effort.PhaseText == TheaterWords.Phase(TheaterPhase.Awaiting, TheaterPhaseNote.NoSteering), "an awaiting operation says why");
            StrFeeds nf = StrFixtures.Feeds("rich"); nf.War.Operation.PhaseNote = TheaterPhaseNote.NoForces;
            TestAssert.That(Build(nf).Effort.PhaseText == "ADVANCING" + StrWords.Dot + "NO FORCES", "a note on a later phase is appended");

            // Picked by the staff, and a missing local identity is never mistaken for the picker.
            StrFeeds staff = StrFixtures.Feeds("rich"); staff.War.Operation.PickedByStaff = true; staff.War.Operation.PickedBy = ""; staff.War.Operation.PickedById = 0UL; staff.LocalId = 0UL;
            StrEffortView st = Build(staff).Effort;
            TestAssert.That(st.PickedText == "SET BY STAFF" && st.PickedByStaff && !st.PickedByLocal, "a staff pick is the staff's, and identity 0 is nobody");
            StrEffortView other = Fixture("locked-by-other").Effort;
            TestAssert.That(!other.PickedByLocal && other.PickedText == "SET BY RAPTOR 2-3" && other.LockSeconds == 71, "someone else's pick is theirs");
            TestAssert.That(Fixture("proposals").Effort.Live == false && Fixture("proposals").Effort.LockSeconds == -1 && Fixture("proposals").Effort.Name == "", "no operation, an empty effort");
        }

        // ---- rule 6, lock ----------------------------------------------------------------------------------------------------

        private static void TheLockCountsDownFromTheReportAge()
        {
            StrFeeds f = StrFixtures.Feeds("rich");
            f.War.Operation.LockSeconds = 120f; f.WarSincePublished = 30f;
            StrEffortView e = Build(f).Effort;
            TestAssert.That(e.LockSeconds == 90 && e.LockText == "01:30", "120 s of lock, 30 s old, reads 01:30");
            f.WarSincePublished = 121f;
            e = Build(f).Effort;
            TestAssert.That(e.LockSeconds == 0 && e.LockText == "", "past the lock it reads zero and the text hides");
            f.War.Operation.LockSeconds = 0f; f.WarSincePublished = 1f;
            TestAssert.That(Build(f).Effort.LockSeconds == -1, "no lock is -1");
            f.War.Operation.LockSeconds = 60f; f.WarSincePublished = -1f;
            TestAssert.That(Build(f).Effort.LockSeconds == 60, "an unknown report age takes the lock as sent");
            f.WarSincePublished = 0.5f;
            TestAssert.That(Build(f).Effort.LockSeconds == 59, "the countdown floors");
        }

        // ---- rule 8 ----------------------------------------------------------------------------------------------------------

        private static void TheWaveIsPricedUpFrontAndHeldNeverDropped()
        {
            StrWaveView earning = Fixture("rich").Wave;
            TestAssert.That(earning.Present && earning.StateText == "EARNING 2/3" && earning.Credits == 2 && earning.PerWave == 3, "two of three credits");
            TestAssert.That(earning.Cost == 1850 && earning.Pool == 7400 && earning.Affordable == StrTri.Yes && !earning.Held, "the price and the pool are up front");
            TestAssert.That(earning.PoolText == "POOL 7,400 " + StrWords.Arrow + " 5,550" && earning.GroupText == "SUPPLY CONVOY ALPHA" && earning.LastText == "LAST WAVE 2 MIN AGO", "pool before and after, group, last wave");
            TestAssert.That(earning.DepotKnown && earning.DepotX == 700f && earning.DepotZ == -250f, "the depot for the route line");

            StrFeeds exact = StrFixtures.Feeds("rich"); exact.War.Wave = StrFixtures.Wave(WaveState.Earning, WaveBlock.None, 2, 1850, 1850);
            TestAssert.That(Build(exact).Wave.Affordable == StrTri.Yes, "a pool exactly the price covers it");

            StrWaveView held = Fixture("wave-held").Wave;
            TestAssert.That(held.StateText == "HELD" + StrWords.Dot + "POOL SHORT" && held.Held && held.Affordable == StrTri.No, "a short pool holds the wave");
            TestAssert.That(held.PoolText.Contains("1,200") && held.PoolText.Contains("1,850"), "with both numbers on the strip");
            TestAssert.That(Fixture("wave-held").Proposals[0].WaveText == "WAVE 1,850 " + StrWords.No, "every ticket shows the same quote, and a cross");

            // Unknown pool: the price still prints, affordability is unknown.
            StrFeeds f = StrFixtures.Feeds("rich");
            f.War.Wave = StrFixtures.Wave(WaveState.Earning, WaveBlock.None, 3, 1850, -1);
            StrViewModel vm = Build(f);
            TestAssert.That(vm.Wave.Affordable == StrTri.Unknown && vm.Wave.Pool == -1 && vm.Wave.PoolText == "POOL " + Dash + StrWords.Dot + "COST 1,850", "an unknown pool is a dash with the price");
            TestAssert.That(vm.Wave.StateText == "READY" && vm.Proposals[0].WaveText == "WAVE 1,850 ?", "a full bank with nothing in the way is ready");
            TestAssert.That(Fixture("rich").Proposals[0].WaveText == "WAVE 1,850 " + StrWords.Yes, "an affordable wave shows a check");

            f.War.Wave = StrFixtures.Wave(WaveState.Earning, WaveBlock.None, 4, -1, -1);
            vm = Build(f);
            TestAssert.That(vm.Wave.StateText == "READY +1" && vm.Wave.PoolText == "POOL " + Dash + StrWords.Dot + "COST " + Dash && vm.Proposals[0].WaveText == "WAVE " + Dash + " ?", "extra credits show as +k, an unknown price as a dash");

            f.War.Wave = StrFixtures.Wave(WaveState.Blocked, WaveBlock.NoDepot, 3, 1850, 7400);
            vm = Build(f);
            TestAssert.That(vm.Wave.Held && vm.Wave.StateText == "HELD" + StrWords.Dot + "NO DEPOT" && vm.Wave.PoolText == TheaterWords.Wave(WaveBlock.NoDepot), "another block holds it and says why in the engine's words");

            f.War.Wave = StrFixtures.Wave(WaveState.Off, WaveBlock.SwitchedOff, 0, 1850, 7400);
            vm = Build(f);
            TestAssert.That(!vm.Wave.Held && vm.Wave.StateText == TheaterWords.Wave(WaveBlock.SwitchedOff), "a desk that is off says so");

            f.War.Wave = null;
            vm = Build(f);
            TestAssert.That(!vm.Wave.Present && vm.Wave.Cost == -1 && vm.Wave.StateText.Length > 8 && vm.Wave.PoolText == "", "no desk is not a free wave");
        }

        // ---- rule 5 ----------------------------------------------------------------------------------------------------------

        private static void ProposalsCarryTheirComposedAimAndTheSameQuote()
        {
            StrViewModel vm = Fixture("proposals");
            TestAssert.That(vm.ProposalCount == 3 && vm.Proposals[0].Tag == "O1" && vm.Proposals[1].Tag == "O2" && vm.Proposals[2].Tag == "O3", "three proposals in engine order");
            StrProposalRow p = vm.Proposals[1];
            TestAssert.That(p.Id == new StrItemId(StrItemKind.Proposal, 23) && p.ProposalId == 23 && p.Revision == 1 && p.KindText == "ASSAULT" && p.RiskText == "HIGH" && p.Recommended, "the engine ids and words");
            TestAssert.That(vm.Proposals[0].RiskText == "MODERATE" && vm.Proposals[2].RiskText == Dash, "risk words come from TheaterWords, the unknown as a dash");
            TestAssert.That(p.Aim == "P2" + StrWords.Dot + "4 FRIENDLY NEARBY, 5 TRACKED HOSTILE." && vm.Proposals[2].Aim == "P5" + StrWords.Dot + Dash + " FRIENDLY NEARBY, " + Dash + " TRACKED HOSTILE.", "the aim line is composed, with dashes for 255");
            TestAssert.That(p.Front == new StrItemId(StrItemKind.Front, 2) && float.Equals(p.Pressure, 0.80f) && p.Radius == 1200f, "a proposal knows its platform, pressure and ring");
            TestAssert.That(float.IsNaN(vm.Proposals[2].Pressure) && vm.Proposals[2].Front.Key == 5, "the unobserved front's proposal has a platform and no pressure");
            TestAssert.That(vm.Proposals[0].WaveText == vm.Proposals[1].WaveText && vm.Proposals[1].WaveText == vm.Proposals[2].WaveText, "one quote on all three tickets");
            TestAssert.That(vm.Proposals[2].WaveAffordable == StrTri.Unknown && vm.Proposals[2].WaveText == "WAVE " + Dash + " ?", "no desk in the deck-only fixture: no quote, no claim");
            for (int i = 0; i < 3; i++) TestAssert.That(vm.Proposals[i].Pick.Allowed, "PICK is allowed on a healthy deck: " + vm.Proposals[i].Pick.Text);

            // A proposal on a target that is not a listed front has no platform and no pressure.
            StrFeeds f = StrFixtures.Feeds("proposals");
            f.War.Proposals[0].TargetKey = "#unlisted";
            StrProposalRow lone = Build(f).Proposals[0];
            TestAssert.That(lone.Front.IsNone && float.IsNaN(lone.Pressure) && !lone.Aim.StartsWith("P"), "a target no front lists is not given a platform");
        }

        // ---- rule 7 ----------------------------------------------------------------------------------------------------------

        private static void SortiesAreTiedOrderedAndCapped()
        {
            StrViewModel rich = Fixture("rich");
            TestAssert.That(rich.SortieCount == 3 && rich.SortieMore == 0 && rich.SortiesLive, "the three tied sorties are on the board");
            TestAssert.That(rich.Sorties[0].Active && rich.Sorties[0].Title == "SCOUT THE RIDGE" && rich.Sorties[1].Title == "STRIKE HARBOR SAM" && rich.Sorties[2].Title == "COVER THE ADVANCE", "accepted first, then least time left");
            TestAssert.That(rich.Sorties[0].Tag == "S1" && rich.Sorties[1].Tag == "S2" && rich.Sorties[2].Tag == "S3", "tags in board order, the first sighting of each");
            StrSortieRow strike = rich.Sorties[1];
            TestAssert.That(strike.ChipText == "STRIKE" && strike.StateText == "OFFERED" && strike.Offered && strike.SiteKnown && strike.Radius == 800f && strike.RewardText == "4,200 +120 XP", "chip, state, site and reward");
            TestAssert.That(Regex.IsMatch(strike.DistanceText, "^\\d+ KM \\d{3}\u00B0$") && strike.TimeText == "09:00", "distance and bearing from the aircraft, time left");
            TestAssert.That(rich.Sorties[0].ByText == "BY VIPER 1-1" && rich.Sorties[0].StateText == "ACCEPTED" && rich.Sorties[0].Progress == 0.4f && float.IsNaN(strike.Progress), "who holds it and how far along");
            TestAssert.That(rich.SortieActiveCount == 1 && rich.SortieActiveLimit == 3, "the active count is of every accepted contract");

            // Only tied, live cards; untied and closed ones are the desk's.
            StrFeeds f = StrFixtures.Feeds("rich");
            f.Sorties = new[]
            {
                StrFixtures.Card(41, "TIED", ObjectiveFamily.Strike, true, false, 100f),
                StrFixtures.Card(42, "UNTIED", ObjectiveFamily.Strike, true, false, 50f, tied: false),
                StrFixtures.Card(43, "CLOSED", ObjectiveFamily.Strike, true, false, 40f, operationLive: false),
            };
            StrViewModel only = Build(f);
            TestAssert.That(only.SortieCount == 1 && only.Sorties[0].ContractId == 41 && only.SortieMore == 0, "untied and closed cards stay off the board");

            // Seven tied cards: five kept, two more; the five are the best five.
            var seven = new List<SecondaryObjectiveView>();
            for (int i = 0; i < 7; i++) seven.Add(StrFixtures.Card(50 + i, "CARD " + i, ObjectiveFamily.Strike, true, false, 600f - i * 10f, acceptedBy: null));
            f.Sorties = seven.ToArray();
            StrViewModel many = Build(f);
            TestAssert.That(many.SortieCount == 5 && many.SortieMore == 2, "seven tied cards give five rows and two more");
            TestAssert.That(many.Sorties[0].ContractId == 56 && many.Sorties[4].ContractId == 52, "the five with the least time left");
            TestAssert.That(StrViewBuilder.HeroTickets == 3, "the hero lane draws three tickets");

            // No own position: a dash, not a distance from the origin.
            f = StrFixtures.Feeds("rich"); f.HasOwn = false;
            foreach (string t in new[] { Build(f).Sorties[0].DistanceText, Build(f).Sorties[1].DistanceText }) TestAssert.That(t == Dash, "no aircraft, no distance");

            // A proposals board has no tied sorties.
            StrFeeds p = StrFixtures.Feeds("proposals"); p.Sorties = StrFixtures.Feeds("rich").Sorties;
            TestAssert.That(Build(p).SortieCount == 0, "with no operation there is nothing to tie a sortie to");

            // Wing text from the status words, with COUNTS when the host counts the wing.
            StrViewModel wing = Fixture("wing-tasked");
            StrSortieRow cover = null;
            for (int i = 0; i < wing.SortieCount; i++) if (wing.Sorties[i].ContractId == 32) cover = wing.Sorties[i];
            TestAssert.That(cover != null && cover.WingLabel == "RECALL" && cover.WingText == "WING TASKED" && cover.HasWing && cover.WingState == WingSortieState.Tasked, "a tasked wing shows RECALL and its status words");
            TestAssert.That(strike.WingLabel == "SEND WING" && rich.Sorties[2].WingLabel == "COVER" && rich.Sorties[0].WingLabel == "SCOUT", "the wing button is labelled by the job");
            foreach (var pair in new[] { (WingSortieState.Done, "WING DONE"), (WingSortieState.Failed, "WING LOST"), (WingSortieState.Recalled, "WING RECALLED") })
            {
                StrFeeds g = StrFixtures.Feeds("rich");
                g.Wing[0].State = pair.Item1; g.Wing[0].Words = "ignored";
                StrViewModel done = Build(g);
                StrSortieRow row = null;
                for (int i = 0; i < done.SortieCount; i++) if (done.Sorties[i].ContractId == 31) row = done.Sorties[i];
                TestAssert.That(row.WingText == pair.Item2 && row.WingLabel == "SEND WING", pair.Item1 + " reads " + pair.Item2 + " and the button returns to its send label");
            }
        }

        // ---- rule 9 ----------------------------------------------------------------------------------------------------------

        private static void PostsAreRichestFirstAndNeverFresherThanTheirFeed()
        {
            StrFeeds f = StrFixtures.Feeds("rich");
            var posts = new StrPostIn[9];
            for (int i = 0; i < 9; i++) posts[i] = StrFixtures.Post(100 + i, "OFFICER " + i, 500 + (i * 7 % 9) * 100, 30f * i, 1000f * i, 0f, capped: i == 3);
            f.Posts = posts; f.PostCount = 9;
            StrViewModel vm = Build(f);
            TestAssert.That(vm.PostCount == 6, "nine posts in, six rows out");
            for (int i = 1; i < vm.PostCount; i++) TestAssert.That(Bounty(vm.Posts[i - 1]) >= Bounty(vm.Posts[i]), "bounty descending");

            f = StrFixtures.Feeds("rich");
            f.Posts = new[] { StrFixtures.Post(12, "COL. IMRE DANKOV", 3000, 360f, 3100f, 5000f), StrFixtures.Post(5, "MAJ. VERA STOLL", 1800, 45f, 6400f, 1100f, capped: true), StrFixtures.Post(6, "OLD FILE", 900, 600f, 0f, 0f) };
            f.PostCount = 3; f.PostsAgeSeconds = 20f;
            vm = Build(f);
            TestAssert.That(vm.Posts[0].SeenText == "SEEN 6 MIN AGO" && vm.Posts[1].SeenText == "SEEN 1 MIN AGO" && vm.Posts[2].SeenText == "SEEN 10+ MIN AGO", "a row's age is its own plus the feed's");
            TestAssert.That(vm.Posts[0].BountyText == "BOUNTY 3,000" && vm.Posts[1].BountyText == "BOUNTY 1,800" + StrWords.Dot + "CAPPED" && vm.Posts[1].Capped && !vm.Posts[0].Capped, "the bounty, and CAPPED only when the cap bit");
            TestAssert.That(vm.Posts[0].Name == "COL. IMRE DANKOV" && vm.Posts[0].Role == "GROUND COMPONENT CMDR" && vm.Posts[0].TraitText == "VETERAN +20% BOUNTY" && vm.Posts[0].X == 3100f, "the dossier lines as the feed sent them");
            TestAssert.That(vm.Posts[0].Id == new StrItemId(StrItemKind.Post, 12) && vm.Posts[0].Ticket.IsNone, "a post with no STRIKE ticket has none");

            f.PostsAvailable = false;
            TestAssert.That(Build(f).PostCount == 0, "without a post feed there are no markers");

            // A STRIKE ticket for a post is linked by the post id the card carries.
            f = StrFixtures.Feeds("rich");
            var tasking = new ObjectiveTasking(ObjectiveFamily.Strike, ObjectiveAsset.Personnel, ObjectiveContact.Known, 0f, "", "", "", new ObjectivePhase[0], ObjectiveLifecycle.Offered, "", ObjectiveAllegiance.Hostile, 12);
            f.Sorties = new[]
            {
                new SecondaryObjectiveView(61, "STRIKE COMMAND POST", "", "", "OFFERED", "", 0f, 500f, 3000, 100, false, true, false, true, 3100f, 5000f, 400f, null, tasking,
                    new SortieLink(21, FocusRole.Assault, "KESTREL CROSSING", true, true, 3100f, 5000f, 400f, false)),
            };
            vm = Build(f);
            TestAssert.That(vm.Sorties[0].PostId == 12 && vm.Posts[0].Id.Key == 12 && vm.Posts[0].Ticket == vm.Sorties[0].Id, "the post and its ticket point at each other");
            TestAssert.That(Fixture("rich").Sorties[0].PostId == -1, "an ordinary card has no post");
        }

        private static int Bounty(StrPostRow r) => int.Parse(r.BountyText.Substring(7).Split(' ')[0].Replace(",", ""));

        // ---- rules 10 and 11 -------------------------------------------------------------------------------------------------

        private static void TilesAndSitrepAgreeAndRefuseToGuess()
        {
            StrViewModel vm = Fixture("rich");
            TestAssert.That(vm.Tiles[0].Key == "SECTORS" && vm.Tiles[1].Key == "AIR" && vm.Tiles[2].Key == "GROUND" && vm.Tiles[3].Key == "POOL", "four tiles in order");
            TestAssert.That(vm.Tiles[0].Value == "53%" && vm.Tiles[0].Value == TheaterReadout.Percent(vm.Sectors.Own) && vm.Sectors.Text.StartsWith("53% OWN"), "the SECTORS tile and the SITREP bar agree on 53");
            TestAssert.That(vm.Tiles[0].Caption == "LEAD" && vm.Tiles[0].State == NOAvionics.AvState.Ready, "ahead of the hostile share by more than two points is a lead");
            TestAssert.That(vm.Sectors.Text == "53% OWN" + StrWords.Dot + "12% CONTESTED" + StrWords.Dot + "29% HOSTILE" + StrWords.Dot + "6% UNCLAIMED" && vm.Sectors.Caption == "17 SECTORS", "all four classes, unclaimed only when above zero");
            TestAssert.That(Math.Abs(vm.Sectors.Own + vm.Sectors.Middle + vm.Sectors.Hostile - (1f - 1f / 17f)) < 0.001f, "the bar parts leave the unclaimed share out");
            TestAssert.That(vm.Tiles[1].Value == "6 : 3" && vm.Tiles[1].Caption == "TRK" && vm.Tiles[2].Value == "14 : 9" && vm.Tiles[2].Caption == "TRK", "air and ground are own : tracked hostile, both TRK");
            TestAssert.That(vm.Air.Text == "6 : 3" && vm.Ground.Text == "14 : 9" && !vm.Ground.Text.Contains("2") && Math.Abs(vm.Ground.Own - 14f / 23f) < 0.001f, "ground counts vehicles only, not the two ships");
            TestAssert.That(vm.Tiles[3].Value == "7,400" && vm.Tiles[3].Caption == "BDF", "the pool tile");
            foreach (StrTile t in vm.Tiles) TestAssert.That(t.Help.Length > 30 && t.Help.EndsWith("."), t.Key + " tile says what it verified");
            TestAssert.That(vm.Airbases.Text == "3 OWN" + StrWords.Dot + "1 NEUTRAL" + StrWords.Dot + "2 HOSTILE" && vm.ContestedBasesText == "1" && vm.RadarsText == "4" && vm.KnownAdText == "6 SITES", "airbases, radars, air defence");
            TestAssert.That(vm.FrontText == "14.2 KM" + StrWords.Dot + "5 PLATFORMS" + StrWords.Dot + "1 NOT OBSERVED", "the front in kilometres and platforms");

            StrFeeds f = StrFixtures.Feeds("rich");
            f.War.Census.SectorsFriendly = 4; f.War.Census.SectorsHostile = 12; f.Census = f.War.Census;
            StrViewModel lag = Build(f);
            TestAssert.That(lag.Tiles[0].Caption == "LAG" && lag.Tiles[0].State == NOAvionics.AvState.Caution, "behind by more than two points is a lag");
            f.War.Census.SectorsFriendly = 8; f.War.Census.SectorsHostile = 8; f.War.Census.SectorsContested = 0; f.War.Census.SectorsNeutral = 0; f.Census = f.War.Census;
            TestAssert.That(Build(f).Tiles[0].Caption == "EVEN", "level is even");
            f.War.Census.AirFriendly = 0; f.War.Census.AirHostileTracked = 0; f.Census = f.War.Census;
            StrViewModel none = Build(f);
            TestAssert.That(none.Tiles[1].Value == "0 : 0" && float.IsNaN(none.Tiles[1].Bar) && float.IsNaN(none.Air.Own), "a measured nothing has no share");
            f.War.Census.SectorsFriendly = 0; f.War.Census.SectorsHostile = 0; f.Census = f.War.Census;
            TestAssert.That(Build(f).Tiles[0].Value == Dash, "an empty board never reads as a confident number");

            StrFeeds noPool = StrFixtures.Feeds("rich"); noPool.War.Wave = StrFixtures.Wave(WaveState.Earning, WaveBlock.None, 1, 1850, -1);
            TestAssert.That(Build(noPool).Tiles[3].Value == Dash, "an unknown pool is a dash on the tile");

            // The AI sortie board: shares of the total, every count needed.
            StrViewModel board = Fixture("rich");
            TestAssert.That(board.SortieCounts[2] == 3 && Math.Abs(board.SortieShares[2] - 3f / 9f) < 0.001f && board.SortieBoardCaption == "FRIENDLY AI ONLY" + StrWords.Dot + "NOT YOU OR YOUR WING", "the AI sortie board");
            float sum = 0f; foreach (float s in board.SortieShares) sum += s;
            TestAssert.That(Math.Abs(sum - 1f) < 0.001f, "shares sum to one");
            f = StrFixtures.Feeds("rich"); f.War.Census.SortieSead = -1; f.Census = f.War.Census;
            StrViewModel one = Build(f);
            TestAssert.That(one.SortieCounts[1] == -1 && float.IsNaN(one.SortieShares[0]) && one.SortieCounts[0] == 2, "one unknown count makes every share unknown, and keeps the known counts");
        }

        private static void TheAlertLevelIsTheCensusThreat()
        {
            StrViewModel vm = Fixture("rich");
            TestAssert.That(vm.AlertLevel == 3 && vm.AlertWord == "HIGH" && vm.AlertRule == TheaterWords.ThreatTitle(TheaterThreatWhy.AirContact), "threat 3 lights level 3 and reads HIGH with its rule");
            StrFeeds f = StrFixtures.Feeds("rich");
            f.War.Census.Threat = 0; f.War.Census.ThreatWhy = TheaterThreatWhy.None; f.Census = f.War.Census;
            vm = Build(f);
            TestAssert.That(vm.AlertLevel == 0 && vm.AlertWord == "NOT MEASURED" && vm.AlertRule == "", "threat 0 is not measured and lights nothing");
            foreach (var pair in new[] { (1, "MAXIMUM"), (2, "SEVERE"), (4, "ELEVATED"), (5, "NORMAL") })
            {
                f.War.Census.Threat = (byte)pair.Item1; f.War.Census.ThreatWhy = TheaterThreatWhy.Nominal; f.Census = f.War.Census;
                vm = Build(f);
                TestAssert.That(vm.AlertLevel == pair.Item1 && vm.AlertWord == pair.Item2, "level " + pair.Item1 + " is " + pair.Item2);
            }
        }

        // ---- rule 13 ---------------------------------------------------------------------------------------------------------

        private static void TheFooterSaysTheMostUrgentThing()
        {
            StrViewModel rich = Fixture("rich");
            TestAssert.That(rich.FooterStatus == "BOARD CURRENT" + StrWords.Dot + "REPORT 4S OLD" + StrWords.Dot + "FACTION BDF" + StrWords.Dot + "5 PLATFORMS", "the live strip: " + rich.FooterStatus);
            TestAssert.That(rich.FooterNote == StrWords.Flavour(StrBoardMode.Operation), "with nothing to say the note is the flavour line");

            StrFeeds f = StrFixtures.Feeds("rich");
            f.PickerPrompt = "CLICK A PLACE ON THE MAP";
            f.WarLast = new TheaterCommandResult { Code = TheaterCommandCode.Accepted, Detail = "", LockSeconds = 0f, SecondsAgo = 1f };
            var ui = new StrUiState { Notice = "ACCEPTED STRIKE HARBOR SAM", NoticeUntil = StrFixtures.Now + 6f };
            TestAssert.That(Build(f, ui).FooterNote == "CLICK A PLACE ON THE MAP", "the picker prompt beats everything");
            f.PickerPrompt = "";
            TestAssert.That(Build(f, ui).FooterNote == "STAFF UPDATED", "then the engine's command result, in the engine's words");
            f.WarLast = new TheaterCommandResult { Code = TheaterCommandCode.Locked, Detail = "Raptor 2-3", LockSeconds = 30f };
            TestAssert.That(Build(f, ui).FooterNote == TheaterWords.Command(TheaterCommandCode.Locked, "Raptor 2-3", 30f), "a locked result names who and for how long");
            f.WarLast = new TheaterCommandResult { Code = TheaterCommandCode.None };
            TestAssert.That(Build(f, ui).FooterNote == "ACCEPTED STRIKE HARBOR SAM", "a result with no words falls through to the local notice");
            f.WarLast = null;
            TestAssert.That(Build(f, ui).FooterNote == "ACCEPTED STRIKE HARBOR SAM", "then the notice");
            TestAssert.That(Build(f, ui, StrFixtures.Now + 5.9f).FooterNote == "ACCEPTED STRIKE HARBOR SAM", "while it lasts");
            TestAssert.That(Build(f, ui, StrFixtures.Now + 6f).FooterNote == StrWords.Flavour(StrBoardMode.Operation), "and it expires on the presentation clock");

            // The same model rebuilt across the expiry changes, with nothing else different.
            f.Posts = new StrPostIn[0]; f.PostCount = 0;
            var vm = new StrViewModel();
            f = StrFixtures.Stamped(f);
            var plat = new StrOrdinals("P"); var sor = new StrOrdinals("S");
            StrViewBuilder.Build(vm, in f, in ui, plat, sor, StrFixtures.Now + 5.5f);
            int rev = vm.Revision;
            StrViewBuilder.Build(vm, in f, in ui, plat, sor, StrFixtures.Now + 5.6f);
            TestAssert.That(vm.Revision == rev, "no change inside the notice");
            StrViewBuilder.Build(vm, in f, in ui, plat, sor, StrFixtures.Now + 6.1f);
            TestAssert.That(vm.Revision == rev + 1 && vm.FooterNote == StrWords.Flavour(StrBoardMode.Operation), "the notice expiring is a change");

            // The strip carries the worst age of the feeds present and flags a stale sortie list.
            f = StrFixtures.Feeds("rich"); f.PostsAgeSeconds = 25f; f.SortiesFresh = false;
            vm = Build(f);
            TestAssert.That(vm.FooterStatus.Contains("REPORT 25S OLD") && vm.FooterStatus.EndsWith(StrWords.Dot + "SORTIES STALE"), "the oldest feed sets the age: " + vm.FooterStatus);

            TestAssert.That(Fixture("proposals").FooterStatus.StartsWith("NO OPERATION RUNNING" + StrWords.Dot + "STAFF PICKS O2 IN 00:34 IF NOBODY DOES"), "the proposals strip counts down to the staff pick: " + Fixture("proposals").FooterStatus);
            TestAssert.That(Fixture("assessing").FooterStatus.StartsWith("FIRST REVIEW IN 00:08"), "the assessing strip counts down to the first review: " + Fixture("assessing").FooterStatus);
            TestAssert.That(Fixture("reduced").FooterStatus.Contains("REDUCED MODE" + StrWords.Dot + "NO GROUND WAR HERE"), "the reduced strip names its reason");
            TestAssert.That(Fixture("waiting").FooterStatus == StrWords.Status(StrBoardMode.Waiting) && !Fixture("waiting").FooterStatus.Contains(" OLD"), "a board with no report claims no age");
        }

        // ---- rule 14 ---------------------------------------------------------------------------------------------------------

        private static void TheNoOpeningChecklistFailsExactlyOneRow()
        {
            var reasons = new[]
            {
                TheaterEmptyReason.StaffFault, TheaterEmptyReason.NoTerritoryField, TheaterEmptyReason.NoFronts, TheaterEmptyReason.NoFriendlyGroundNearFront,
                TheaterEmptyReason.FrontsQuiet, TheaterEmptyReason.PostureTooCautious, TheaterEmptyReason.CalledOff,
            };
            var rows = new[] { 0, 1, 2, 3, 4, 5, 5 };
            for (int r = 0; r < reasons.Length; r++)
            {
                TheaterWarSnapshot s = StrFixtures.Reason(StrFixtures.Snapshot(), reasons[r], 40f);
                StrFeeds f = StrFixtures.Live(s); f.WarSincePublished = 0f;
                StrViewModel vm = Build(f);
                TestAssert.That(vm.Mode == StrBoardMode.NoOpening && vm.Banner.CheckCount == 6, reasons[r] + " is a no-opening board with six rows");
                int fails = 0;
                for (int i = 0; i < 6; i++)
                {
                    StrCheck c = vm.Banner.Checks[i];
                    if (c.Pass == StrTri.No) { fails++; TestAssert.That(i == rows[r], reasons[r] + " fails row " + rows[r] + " not " + i); }
                    else if (i < rows[r]) TestAssert.That(c.Pass == StrTri.Yes && c.Do == "", reasons[r] + " row " + i + " passes before the blocking row");
                    else TestAssert.That(c.Pass == StrTri.Unknown && c.Do == "NOT CHECKED YET", reasons[r] + " row " + i + " is not checked yet");
                    TestAssert.That(c.Label.Length > 8, "every row has a label");
                }
                TestAssert.That(fails == 1, reasons[r] + " fails exactly one row");
                string title, detail;
                string engine = TheaterWords.Empty(reasons[r], 40f);
                int cut = engine.IndexOf(" - ", StringComparison.Ordinal);
                title = engine.Substring(0, cut); detail = engine.Substring(cut + 3);
                TestAssert.That(vm.Banner.Checks[rows[r]].Do == detail && vm.Banner.Why == title && vm.Banner.Do == detail, reasons[r] + " carries the engine's own sentence");
            }

            StrViewModel odd = Build(StrFixtures.Live(StrFixtures.Reason(StrFixtures.Snapshot(), TheaterEmptyReason.OperationLive, 0f)));
            int unknown = 0; for (int i = 0; i < odd.Banner.CheckCount; i++) if (odd.Banner.Checks[i].Pass == StrTri.Unknown) unknown++;
            TestAssert.That(odd.Mode == StrBoardMode.NoOpening && unknown == 6, "a reason that is not on the list checks nothing and claims nothing");
            TestAssert.That(Fixture("proposals").Banner.CheckCount == 0 && Fixture("reduced").Banner.CheckCount == 0, "only a no-opening board has a checklist");
        }

        private static void CountdownsReadTheEnginesClocks()
        {
            StrViewModel p = Fixture("proposals");
            TestAssert.That(p.Banner.CountSeconds == 34 && p.Banner.CountLabel == "AUTO-PICK O2 IN", "the staff pick counts down from the deal");
            StrViewModel a = Fixture("assessing");
            TestAssert.That(a.Banner.CountSeconds == 8 && a.Banner.CountLabel == "FIRST REVIEW IN", "the first review counts down");
            StrFeeds f = StrFixtures.Live(StrFixtures.Reason(StrFixtures.Snapshot(), TheaterEmptyReason.CalledOff, 40f));
            StrViewModel c = Build(f);
            TestAssert.That(c.Banner.CountSeconds == 36 && c.Banner.CountLabel == "BACK IN THE DECK IN", "a called-off front counts down to its return");
            f = StrFixtures.Live(StrFixtures.Reason(StrFixtures.Snapshot(), TheaterEmptyReason.FrontsQuiet, 6f));
            StrViewModel q = Build(f);
            TestAssert.That(q.Banner.CountSeconds == 2 && q.Banner.CountLabel == "NEXT REVIEW IN", "a quiet board counts down to the next review");
            f.War.ReasonSeconds = 0f;
            TestAssert.That(Build(f).Banner.CountSeconds == -1, "no clock, no countdown");
            f.WarSincePublished = 100f; f.War.ReasonSeconds = 6f;
            TestAssert.That(Build(f).Banner.CountSeconds == 0, "an expired countdown reads zero, never negative");
            TestAssert.That(Fixture("waiting").Banner.CountSeconds == 22 && Fixture("waiting").Banner.CountLabel == "NO ANSWER IN", "waiting counts down to no answer");
            TestAssert.That(Fixture("noanswer").Banner.CountSeconds == -1, "no answer has no clock");
        }

        // ---- rule 15 ---------------------------------------------------------------------------------------------------------

        private static void ASelectionLivesOnlyWhileItsRowDoes()
        {
            StrFeeds f = StrFixtures.Feeds("rich");
            foreach (var id in new[]
            {
                new StrItemId(StrItemKind.Proposal, 23), new StrItemId(StrItemKind.Front, 2), new StrItemId(StrItemKind.Sortie, 31),
                new StrItemId(StrItemKind.Post, 12), new StrItemId(StrItemKind.Effort, 21),
            })
            {
                StrViewModel kept = Build(f, new StrUiState { Selected = id, Hover = id });
                TestAssert.That(kept.SelectedId == id && kept.HoverId == id && !kept.SelectionVanished, id.Kind + " " + id.Key + " is kept while its row exists");
            }
            foreach (var id in new[]
            {
                new StrItemId(StrItemKind.Proposal, 99), new StrItemId(StrItemKind.Front, 1), new StrItemId(StrItemKind.Sortie, 77),
                new StrItemId(StrItemKind.Post, 9), new StrItemId(StrItemKind.Effort, 22),
            })
            {
                StrViewModel gone = Build(f, new StrUiState { Selected = id });
                TestAssert.That(gone.SelectedId.IsNone && gone.SelectionVanished, id.Kind + " " + id.Key + " is cleared and flagged");
            }
            StrViewModel hover = Build(f, new StrUiState { Hover = new StrItemId(StrItemKind.Proposal, 99) });
            TestAssert.That(hover.HoverId.IsNone && hover.SelectionVanished, "a hover on a vanished row is cleared and flagged too");
            StrViewModel nothing = Build(f, default);
            TestAssert.That(nothing.SelectedId.IsNone && !nothing.SelectionVanished, "no selection asks for nothing");
            StrViewModel noOp = Build(StrFixtures.Feeds("proposals"), new StrUiState { Selected = new StrItemId(StrItemKind.Effort, 21) });
            TestAssert.That(noOp.SelectionVanished, "the effort is not selectable while there is none");
        }

        // ---- rule 16 and the allocation rule ------------------------------------------------------------------------------------

        private static void TheRevisionBumpsOnlyOnAChange()
        {
            StrFeeds f = StrFixtures.Stamped(StrFixtures.Feeds("rich"));
            var ui = default(StrUiState);
            var vm = new StrViewModel(); var plat = new StrOrdinals("P"); var sor = new StrOrdinals("S");
            TestAssert.That(StrViewBuilder.Build(vm, in f, in ui, plat, sor, StrFixtures.Now) && vm.Revision == 1, "the first build is a change");
            TestAssert.That(!StrViewBuilder.Build(vm, in f, in ui, plat, sor, StrFixtures.Now) && vm.Revision == 1, "the same input twice leaves the revision alone");

            StrFeeds same = f; same.Stamp++;
            TestAssert.That(!StrViewBuilder.Build(vm, in same, in ui, plat, sor, StrFixtures.Now) && vm.Revision == 1, "a new stamp with nothing different displayed is not a change");

            StrFeeds changed = f; changed.Stamp += 2; changed.War = StrFixtures.Feeds("rich").War; changed.War.Fronts[1].Pressure = 0.7f;
            TestAssert.That(StrViewBuilder.Build(vm, in changed, in ui, plat, sor, StrFixtures.Now) && vm.Revision == 2, "a displayed change bumps it once");

            // A running lock bumps it once per second as the report ages, not on every tick.
            StrFeeds tick = changed; tick.Stamp++;
            tick.WarSincePublished = 10.1f;
            StrViewBuilder.Build(vm, in tick, in ui, plat, sor, StrFixtures.Now);
            int r = vm.Revision;
            tick.WarSincePublished = 10.7f;
            StrViewBuilder.Build(vm, in tick, in ui, plat, sor, StrFixtures.Now + 0.4f);
            TestAssert.That(vm.Revision == r, "inside one second of report age nothing is rebuilt");
            tick.WarSincePublished = 11.2f;
            StrViewBuilder.Build(vm, in tick, in ui, plat, sor, StrFixtures.Now + 0.9f);
            TestAssert.That(vm.Revision == r + 1 && vm.Effort.LockSeconds == 78, "the next second of age is one bump and the lock reads one less");

            // A selection change is a change; the ui equal twice is not.
            var picked = new StrUiState { Selected = new StrItemId(StrItemKind.Proposal, 23) };
            int before = vm.Revision;
            StrViewBuilder.Build(vm, in tick, in picked, plat, sor, StrFixtures.Now + 0.9f);
            TestAssert.That(vm.Revision == before + 1 && vm.SelectedId == picked.Selected, "a selection changes the model");
            StrViewBuilder.Build(vm, in tick, in picked, plat, sor, StrFixtures.Now + 0.9f);
            TestAssert.That(vm.Revision == before + 1, "and an equal ui builds nothing");

            // A replan armed for four seconds shows and then lapses.
            var armed = new StrUiState { ReplanArmedUntil = StrFixtures.Now + 4f };
            StrViewBuilder.Build(vm, in tick, in armed, plat, sor, StrFixtures.Now + 1f);
            TestAssert.That(vm.Effort.ReplanArmed, "an armed replan is armed");
            StrViewBuilder.Build(vm, in tick, in armed, plat, sor, StrFixtures.Now + 4.5f);
            TestAssert.That(!vm.Effort.ReplanArmed, "and it lapses on the presentation clock with no input");

            TestAssert.That(!StrViewBuilder.Build(null, in f, in ui, plat, sor, 0f) && !StrViewBuilder.Build(vm, in f, in ui, null, sor, 0f), "a missing model or table builds nothing");
        }

        private static void ASteadyBuildAllocatesNothing()
        {
            StrFeeds f = StrFixtures.Stamped(StrFixtures.Feeds("rich"));
            var ui = new StrUiState { Selected = new StrItemId(StrItemKind.Proposal, 23), Notice = "x", NoticeUntil = StrFixtures.Now + 60f };
            var vm = new StrViewModel(); var plat = new StrOrdinals("P"); var sor = new StrOrdinals("S");
            StrViewBuilder.Build(vm, in f, in ui, plat, sor, StrFixtures.Now);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool any = false;
            for (int i = 0; i < 100; i++) any |= StrViewBuilder.Build(vm, in f, in ui, plat, sor, StrFixtures.Now + 0.01f * i);
            long after = GC.GetAllocatedBytesForCurrentThread();
            TestAssert.That(!any && after - before == 0, "a hundred steady builds allocate " + (after - before) + " bytes");

            // The first build of every fixture fills the model without growing past its caps.
            foreach (string name in StrFixtures.Names) TestAssert.That(Fixture(name).Revision == 1, name + " builds once");
        }

        // ---- rule 17 ---------------------------------------------------------------------------------------------------------

        private static void AMalformedFeedNeverThrowsOrExceedsACap()
        {
            StrFeeds f = StrFixtures.Feeds("rich");
            var fronts = new List<TheaterFrontView>();
            for (int i = 0; i < 14; i++) fronts.Add(StrFixtures.Front("@" + i, "N" + i, float.NaN, float.PositiveInfinity, float.NaN, float.NaN, TheaterFrontStatus.UnderPressure));
            fronts.Add(null);
            f.War.Fronts = fronts.ToArray();
            var props = new List<TheaterProposalView>();
            for (int i = 0; i < 6; i++) props.Add(StrFixtures.Proposal(100 + i, TheaterOpKind.Assault, "@" + i, "P" + i, float.NaN, 0f, TheaterRisk.High, 255, 255, false));
            props.Add(null);
            f.War.Proposals = props.ToArray();
            f.War.Log = new string[20]; for (int i = 0; i < 20; i++) f.War.Log[i] = i == 3 ? null : new string('x', 90);
            var marks = new List<TheaterForceMark>();
            for (int i = 0; i < 12; i++) marks.Add(new TheaterForceMark { X = float.NaN, Z = i, Stage = 200, Alive = 200 });
            marks.Add(null);
            f.War.Operation.Forces.Marks = marks.ToArray();
            f.War.Operation.Radius = float.NaN; f.War.Operation.X = float.NaN; f.War.Operation.LockSeconds = float.NaN;
            var cards = new List<SecondaryObjectiveView> { null };
            for (int i = 0; i < 40; i++) cards.Add(StrFixtures.Card(200 + i, null, ObjectiveFamily.Unknown, true, false, -5f, x: float.NaN, z: float.NaN));
            cards.Add(null);
            f.Sorties = cards.ToArray();
            f.Posts = new StrPostIn[40]; f.PostCount = 40;
            for (int i = 0; i < 40; i++) f.Posts[i] = new StrPostIn { Id = i, Bounty = -5, X = float.NaN, Z = float.NaN, IntelAge = float.NaN };
            f.Wing = null; f.WingCount = 5;
            f.WarAgeSeconds = -5f; f.WarSincePublished = float.NaN; f.PostsAgeSeconds = float.NegativeInfinity; f.FactionCode = null; f.PickerPrompt = null;
            f.OwnX = float.NaN;

            StrViewModel vm = null;
            try { vm = Build(f); } catch (Exception e) { TestAssert.That(false, "a malformed feed threw " + e.GetType().Name + ": " + e.Message); }
            TestAssert.That(vm.FrontCount <= StrViewModel.MaxFronts && vm.ProposalCount <= StrViewModel.MaxProposals && vm.SortieCount <= StrViewModel.MaxSorties &&
                            vm.PostCount <= StrViewModel.MaxPosts && vm.Effort.MarkCount <= StrViewModel.MaxMarks && vm.LogCount <= StrViewModel.MaxLog, "no count exceeds its cap");
            TestAssert.That(vm.FrontCount == 8 && vm.ProposalCount == 3 && vm.SortieCount == 5 && vm.PostCount == 6 && vm.Effort.MarkCount == 8 && vm.LogCount == 8, "and each is at its cap");
            TestAssert.That(float.IsNaN(vm.Fronts[0].X) && float.IsNaN(vm.Fronts[0].Pressure) && vm.Fronts[0].PressureText == Dash, "a front at a non-finite place is NaN and prints a dash");
            TestAssert.That(vm.Log[0].Length == 64, "a log line is cut to 64");
            TestAssert.That(float.IsNaN(vm.Effort.X) && vm.Effort.Radius == 0f && vm.Effort.LockSeconds == -1 && float.IsNaN(vm.AgeSeconds), "non-finite operation numbers and a negative age are unknown");
            TestAssert.That(!vm.Sorties[0].SiteKnown && vm.Sorties[0].DistanceText == Dash && vm.Sorties[0].TimeText == Dash && vm.Sorties[0].Title == "", "a sortie with no finite place is not located");
            TestAssert.That(vm.Posts[0].BountyText.StartsWith("BOUNTY " + Dash) && vm.Posts[0].SeenText == "SEEN " + Dash && vm.FooterStatus.Length > 0, "a post with a negative bounty or an unknown age prints a dash for it");
            TestAssert.That(vm.Fronts[0].Tag.Length > 0 && vm.FrontCount == 8, "a null front past the cap is ignored");

            // Null pieces of the snapshot itself.
            StrFeeds bare = StrFixtures.Live(new TheaterWarSnapshot { Fronts = null, Proposals = null, Log = null, Wave = null });
            bare.Sorties = null; bare.Posts = null; bare.Wing = null;
            vm = Build(bare);
            TestAssert.That(vm.Mode == StrBoardMode.NoOpening && vm.FrontCount == 0 && vm.LogCount == 0 && vm.SortieCount == 0, "a snapshot of nulls is an empty board, not a crash");
            StrFeeds noForces = StrFixtures.Feeds("rich"); noForces.War.Operation.Forces = null;
            TestAssert.That(Build(noForces).Effort.VehiclesText == Dash, "an operation with no forces block reads unknown");
        }

        // ---- the gates, per feed ----------------------------------------------------------------------------------------------

        private static void AStaleFeedDisablesOnlyItsOwnVerbs()
        {
            StrViewModel warStale = Fixture("stale");
            TestAssert.That(warStale.Proposals[0].Pick.Why == StrWhy.Stale && warStale.Effort.Replan.Why == StrWhy.Stale && warStale.Proposals[0].Pick.Text.Contains("22 S OLD"), "a stale war report disables pick and replan, with its age");
            TestAssert.That(warStale.Sorties[1].Accept.Allowed, "but not accept, which is the sortie list's verb");
            TestAssert.That(warStale.Fronts[0].Redirect.Why == StrWhy.Stale, "nor is a redirect spared");

            StrFeeds f = StrFixtures.Feeds("rich"); f.SortiesFresh = false;
            StrViewModel sortieStale = Build(f);
            TestAssert.That(!sortieStale.SortiesLive && sortieStale.Sorties[1].Accept.Why == StrWhy.Stale, "a stale sortie list disables accept");
            TestAssert.That(sortieStale.Proposals[0].Pick.Allowed && sortieStale.Effort.Replan.Allowed, "but not pick or replan");
            TestAssert.That(sortieStale.PostCount == 2 && sortieStale.Posts[0].X == 3100f, "and the post markers keep their place");

            StrViewModel rich = Fixture("rich");
            TestAssert.That(rich.Effort.Replan.Allowed && rich.Proposals[1].Pick.Allowed && rich.Fronts[0].Redirect.Allowed, "a healthy board allows its war verbs");
            TestAssert.That(rich.Sorties[1].Accept.Allowed && !rich.Sorties[0].Accept.Allowed && rich.Sorties[0].Accept.Why == StrWhy.NotOffered, "accept only on an offered sortie");
            TestAssert.That(rich.Sorties[1].Wing.Why == StrWhy.WingRefused && rich.Sorties[1].Wing.Text.StartsWith("ACCEPT THIS SORTIE FIRST"), "an offered card sends no wing until it is accepted");
            TestAssert.That(rich.Sorties[0].Wing.Allowed, "an accepted card with a wing order can send it");

            StrFeeds pending = StrFixtures.Feeds("rich"); pending.WarPending = true; pending.SortieActionPending = true;
            StrViewModel pend = Build(pending);
            TestAssert.That(pend.Proposals[0].Pick.Why == StrWhy.Pending && pend.Sorties[1].Accept.Why == StrWhy.Pending, "an order in flight disables the next one");

            StrViewModel locked = Fixture("locked-by-other");
            TestAssert.That(locked.Proposals[0].Pick.Why == StrWhy.Locked && locked.Proposals[0].Pick.Text.Contains("RAPTOR 2-3") && locked.Proposals[0].Pick.Text.Contains("01:11"), "someone else's lock names them and the clock");
            TestAssert.That(rich.Proposals[0].Pick.Allowed, "the picker may redirect");

            StrFeeds noOp = StrFixtures.Feeds("proposals");
            StrViewModel prop = Build(noOp);
            TestAssert.That(prop.Effort.Replan.Why == StrWhy.NoOperation && prop.Fronts[0].Redirect.Why == StrWhy.NoOperation, "with no operation there is nothing to call off or redirect");
            TestAssert.That(rich.Fronts[2].Redirect.Why == StrWhy.Gone, "a lane with no offer cannot be redirected to");

            foreach (string name in new[] { "waiting", "noanswer", "staffoff", "hostlacks", "nofaction" })
            {
                StrViewModel m = Fixture(name);
                TestAssert.That(!m.CanCommand && StrGate.Check(m, StrActionKind.Posture, StrItemId.None, 2).Why == StrWhy.NoBoard, name + " has no board to command");
            }
        }

        private static void TheClientIsToldWhoCanSendTheWing()
        {
            StrViewModel client = Fixture("client");
            TestAssert.That(!client.IsHost && client.Mode == StrBoardMode.Operation, "the client fixture is a client on a live board");
            for (int i = 0; i < client.SortieCount; i++)
            {
                StrSortieRow r = client.Sorties[i];
                TestAssert.That(r.Wing.Why == StrWhy.HostOnly && r.Wing.Text.StartsWith("ONLY THE HOST CAN SEND A WING") && !r.HasWing && r.WingText == "", "sortie " + r.Tag + " tells a client who can send the wing");
                TestAssert.That(StrGate.Check(client, StrActionKind.SendWing, r.Id).Why == StrWhy.HostOnly && StrGate.Check(client, StrActionKind.Recall, r.Id).Why == StrWhy.HostOnly, "and the gate agrees");
            }
            TestAssert.That(client.Sorties[1].Accept.Allowed, "a client may still accept");
            TestAssert.That(client.Proposals[0].Pick.Allowed, "and pick: the host decides");

            StrViewModel waiting = Fixture("waiting");
            TestAssert.That(!waiting.IsHost && waiting.Banner.Show && waiting.ProposalCount == 0 && waiting.FrontCount == 0 && waiting.Effort.Live == false, "a waiting client builds without a snapshot");
            StrViewModel stale = Fixture("stale");
            TestAssert.That(stale.Stale && stale.FrontCount == 4, "a stale board builds from the last report");
        }

        // ---- rule 19 ---------------------------------------------------------------------------------------------------------

        private static void TheCensusFollowsTheMode()
        {
            StrViewModel off = Fixture("staffoff");
            TestAssert.That(off.Mode == StrBoardMode.StaffOff && off.Sectors.Text.StartsWith("53% OWN") && off.AlertLevel == 3 && off.Tiles[0].Value == "53%", "a host with the staff off still reads its own census");
            TestAssert.That(off.Tiles[3].Value == Dash && off.LogCount == 0 && off.FrontCount == 0 && off.ProposalCount == 0, "but has no pool, no log and no fronts");

            foreach (string name in new[] { "hostlacks", "nofaction", "waiting", "noanswer" })
            {
                StrViewModel vm = Fixture(name);
                TestAssert.That(vm.Sectors.Text == Dash && vm.Air.Text == Dash && vm.Ground.Text == Dash && vm.Airbases.Text == Dash, name + " prints only dashes on SITREP");
                TestAssert.That(vm.AlertLevel == 0 && vm.AlertWord == "NOT MEASURED" && vm.RadarsText == Dash && vm.FrontText == Dash, name + " is NOT MEASURED");
                foreach (StrTile t in vm.Tiles) TestAssert.That(t.Value == Dash && t.State == NOAvionics.AvState.Inert, name + " tile " + t.Key + " is a dash");
                string said = vm.Banner.Head + " " + vm.Banner.Why + " " + vm.Banner.Do + " " + vm.FooterNote;
                TestAssert.That(!said.Contains("still read the live theater") && !said.Contains("SITREP still"), name + " claims nothing works that does not");
            }
            TestAssert.That(Fixture("hostlacks").FooterNote.Contains("dashes") && Fixture("noanswer").Banner.Do.Contains("dashes"), "the banners that print dashes say so");
            TestAssert.That(Fixture("staffoff").FooterNote != Fixture("hostlacks").FooterNote, "staff off here and on the host read differently");
            TestAssert.That(Fixture("reduced").Sectors.Text.StartsWith("53% OWN") && Fixture("reduced").AlertLevel == 3, "a reduced board prints the snapshot's counts");
        }
    }
}
