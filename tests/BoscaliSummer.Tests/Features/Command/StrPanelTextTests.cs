using System;
using System.Text.RegularExpressions;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The panel's own words and page plan (annex 05 P9): which parts a mode shows, and the composed captions the parts only copy.</summary>
    internal static class StrPanelTextTests
    {
        private const string Dash = "—";
        private static readonly Regex ZeroPercent = new Regex(@"\b0\s*%");

        public static void Run()
        {
            EveryModeHasOneShape();
            AProposalsBoardDropsTheLanesItsTicketsCarry();
            TheFlapShowsMinutesAndSecondsOrNothing();
            TheSortiesCaptionNamesThePlatformAndTheWaveStep();
            TheWaveLinesAreHonest();
            TheReplanLabelNamesWhatItCalls();
            TheTicketReasonIsTheOneThatMatters();
            TheProposalPlateSplitsItsPrice();
            TheLogNoteNamesItsCause();
            ThePlatformCaptionCarriesTheMeasuredFrontOnly();
            ControlIdsFollowTheGrammar();
            TheBannerInkSeparatesAPickFromAnOutage();
            ItemIdsResolveAgainstTheModel();
        }

        private static void EveryModeHasOneShape()
        {
            foreach (StrBoardMode mode in (StrBoardMode[])Enum.GetValues(typeof(StrBoardMode)))
            {
                StrPagePlan p = StrPagePlan.For(mode);
                int bodies = (p.Hero ? 1 : 0) + (p.ProposalTickets ? 1 : 0) + (p.Placeholders ? 1 : 0);
                TestAssert.That(bodies <= 1, mode + " shows at most one of hero, proposal tickets and placeholders");
                TestAssert.That(!(p.PostureTop && p.PostureBottom), mode + " has one posture row, not two");
                TestAssert.That(p.Lanes != p.Placeholders, mode + " draws lanes or placeholders, never both and never neither");
                TestAssert.That(p.LaneTitle.Length > 0, mode + " titles its lane section");
            }
            StrPagePlan op = StrPagePlan.For(StrBoardMode.Operation);
            TestAssert.That(op.Hero && op.PostureBottom && !op.PostureTop && op.LaneTitle == "OTHER PLATFORMS", "an operation puts its lane on top and the posture row below the platforms");
            StrPagePlan pr = StrPagePlan.For(StrBoardMode.Proposals);
            TestAssert.That(pr.ProposalTickets && pr.PostureTop && !pr.Hero, "a deck puts the posture row above the tickets");
            TestAssert.That(StrPagePlan.For(StrBoardMode.NoOpening).Checklist && !StrPagePlan.For(StrBoardMode.Assessing).Checklist, "only the no-opening board carries the checklist");
            TestAssert.That(!StrPagePlan.For(StrBoardMode.Reduced).PostureTop && !StrPagePlan.For(StrBoardMode.Reduced).PostureBottom, "a mission with no ground war has no posture to set");
            foreach (StrBoardMode m in new[] { StrBoardMode.StaffOff, StrBoardMode.HostLacksStaff, StrBoardMode.NoFaction, StrBoardMode.Waiting, StrBoardMode.NoAnswer })
            {
                TestAssert.That(StrPagePlan.For(m).Placeholders, m + " shows the hatched placeholder lanes");
                TestAssert.That(StrPagePlan.PlaceholderText(m).Length >= 12, m + " says what the placeholders are");
            }
            TestAssert.That(StrPagePlan.PlaceholderText(StrBoardMode.Waiting) != StrPagePlan.PlaceholderText(StrBoardMode.StaffOff), "waiting and staff off do not claim the same thing");
        }

        private static void AProposalsBoardDropsTheLanesItsTicketsCarry()
        {
            StrViewModel vm = StrFixtures.Model("proposals");
            int shown = 0, offered = 0;
            for (int i = 0; i < vm.FrontCount; i++)
            {
                if (!vm.Fronts[i].Offer.IsNone) offered++;
                if (StrPagePlan.ShowsLane(StrBoardMode.Proposals, vm.Fronts[i])) shown++;
            }
            TestAssert.That(offered >= 1 && shown == vm.FrontCount - offered, "a lane with a proposal ticket is not drawn twice");
            for (int i = 0; i < vm.FrontCount; i++)
                TestAssert.That(StrPagePlan.ShowsLane(StrBoardMode.Operation, vm.Fronts[i]), "an operation board keeps every other platform, offers and all (REDIRECT)");
            TestAssert.That(!StrPagePlan.ShowsLane(StrBoardMode.Operation, null), "a missing row is never drawn");
        }

        private static void TheFlapShowsMinutesAndSecondsOrNothing()
        {
            TestAssert.That(StrPanelText.Flap(92) == "0132", "92 s reads 01:32");
            TestAssert.That(StrPanelText.Flap(0) == "0000", "zero is a real zero when the engine says so");
            TestAssert.That(StrPanelText.Flap(38) == "0038", "seconds pad to two places");
            TestAssert.That(StrPanelText.Flap(99 * 60 + 59) == "9959" && StrPanelText.Flap(100000) == "9959", "the flap saturates at 99:59");
            TestAssert.That(StrPanelText.Flap(-1) == "" && StrPanelText.Flap(int.MinValue) == "", "an unknown time shows no digits, never 00:00");
        }

        private static void TheSortiesCaptionNamesThePlatformAndTheWaveStep()
        {
            StrViewModel rich = StrFixtures.Model("rich");
            string c = StrPanelText.SortiesCaption(rich);
            TestAssert.That(c.StartsWith("TIED TO " + rich.Effort.FrontTag) && c.Contains("3 SORTIES"), "the caption names the platform and the count: " + c);
            TestAssert.That(c.Contains("1 MORE DONE LAUNCHES THE WAVE"), "two of three finished sorties leaves one: " + c);
            StrViewModel held = StrFixtures.Model("wave-held");
            TestAssert.That(!StrPanelText.SortiesCaption(held).Contains("LAUNCH"), "a full credit bank launches nothing more by finishing sorties");
            StrViewModel unknown = StrFixtures.Model("unknown");
            TestAssert.That(StrPanelText.SortiesCaption(unknown).Length > 0 && !ZeroPercent.IsMatch(StrPanelText.SortiesCaption(unknown)), "an unknown board still captions itself");
            TestAssert.That(StrPanelText.SortiesCaption(null) == "", "no model, no caption");
        }

        private static void TheWaveLinesAreHonest()
        {
            StrViewModel rich = StrFixtures.Model("rich");
            string line = StrPanelText.WaveLine(rich);
            TestAssert.That(line.Contains(rich.Wave.GroupText) && line.Contains("COST 1,850") && line.Contains("POOL 7,400"), "the wave line carries group, price and pool: " + line);
            TestAssert.That(StrPanelText.WaveCheck(rich).Contains("AFFORDABLE") && !StrPanelText.WaveCheck(rich).Contains("NOT"), "a covered price says so");
            StrViewModel held = StrFixtures.Model("wave-held");
            TestAssert.That(StrPanelText.WaveCheck(held).Contains("OVER THE POOL") && StrPanelText.WaveCheck(held).StartsWith(StrWords.No), "an uncovered price says it is over the pool: " + StrPanelText.WaveCheck(held));
            StrViewModel unknown = StrFixtures.Model("unknown");
            TestAssert.That(StrPanelText.WaveCheck(unknown) == "", "an unknown price claims neither a check nor a cross");
            TestAssert.That(StrPanelText.WaveLine(unknown).Contains("COST " + Dash), "an unknown price prints a dash: " + StrPanelText.WaveLine(unknown));
            TestAssert.That(StrPanelText.WaveLine(StrFixtures.Model("staffoff")) == "", "no wave desk, no line");
        }

        private static void TheReplanLabelNamesWhatItCalls()
        {
            StrViewModel vm = StrFixtures.Model("rich");
            TestAssert.That(StrPanelText.ReplanLabel(vm) == "REPLAN", "an unarmed button says REPLAN");
            StrUiState ui = default;
            ui.ReplanArmedUntil = StrFixtures.Now + 3f;
            var armed = new StrViewModel();
            StrFeeds feeds = StrFixtures.Feeds("rich");
            StrViewBuilder.Build(armed, in feeds, in ui, new StrOrdinals("P"), new StrOrdinals("S"), StrFixtures.Now);
            string label = StrPanelText.ReplanLabel(armed);
            TestAssert.That(label.StartsWith("CALLS OFF ") && label.Contains(armed.Effort.FrontTag), "an armed button names the platform it calls off: " + label);
        }

        private static void TheTicketReasonIsTheOneThatMatters()
        {
            StrViewModel rich = StrFixtures.Model("rich");
            for (int i = 0; i < rich.SortieCount; i++)
                TestAssert.That(StrPanelText.TicketReason(rich.Sorties[i]) == "", "an allowed offer prints no reason line (" + rich.Sorties[i].Tag + ")");
            StrViewModel client = StrFixtures.Model("client");
            bool sawHostOnly = false;
            for (int i = 0; i < client.SortieCount; i++)
                if (client.Sorties[i].Active) { string r = StrPanelText.TicketReason(client.Sorties[i]); sawHostOnly = r.Contains("HOST"); }
            TestAssert.That(sawHostOnly, "an accepted ticket on a client says the wing is the host's");
            StrViewModel stale = StrFixtures.Model("stale");
            TestAssert.That(StrPanelText.TicketReason(null) == "", "a missing row prints nothing");
            TestAssert.That(stale.SortieCount > 0, "the stale fixture still lists its tickets");
        }

        private static void TheProposalPlateSplitsItsPrice()
        {
            StrViewModel vm = StrFixtures.Model("proposals");
            StrProposalRow rec = null, other = null;
            for (int i = 0; i < vm.ProposalCount; i++) { if (vm.Proposals[i].Recommended) rec = vm.Proposals[i]; else other = vm.Proposals[i]; }
            TestAssert.That(rec != null && other != null, "the deck has a default and an open card");
            TestAssert.That(StrPanelText.PlateState(rec) == "DEFAULT" && StrPanelText.PlateState(other) == "OPEN", "the staff's choice is the default card");
            StrPanelText.PlateWave(rec, out string word, out string price);
            TestAssert.That(word == "WAVE" && price.Length > 0, "the plate prints WAVE over the price: " + word + "/" + price);
            TestAssert.That(StrPanelText.RiskChip(vm.Proposals[0]).StartsWith("RISK "), "the risk chip names its subject");
            StrViewModel unknown = StrFixtures.Model("unknown");
            TestAssert.That(StrPanelText.RiskChip(unknown.Proposals[0]) == "RISK " + Dash, "an unknown risk is a dash, not a level");
        }

        private static void TheLogNoteNamesItsCause()
        {
            StrViewModel rich = StrFixtures.Model("rich");
            TestAssert.That(rich.LogCount > 0, "the rich board has log lines to show");
            foreach (string name in new[] { "staffoff", "hostlacks", "nofaction", "waiting", "noanswer" })
            {
                StrViewModel vm = StrFixtures.Model(name);
                StrPanelText.LogNote(vm, out string title, out string detail);
                TestAssert.That(title.Length >= 8 && detail.Length >= 12, name + " explains why there is no log");
                TestAssert.That(vm.Banner.Head.Length >= 8 && detail.Contains(vm.Banner.Head), name + " names the cause: " + detail);
            }
            StrPanelText.LogNote(StrFixtures.Model("assessing"), out string t2, out string d2);
            TestAssert.That(t2.Length >= 8 && d2.Length >= 12, "an assessing board with an empty log says the staff has not written yet");
        }

        private static void ThePlatformCaptionCarriesTheMeasuredFrontOnly()
        {
            StrViewModel rich = StrFixtures.Model("rich");
            string length = StrPanelText.FrontLength(rich.FrontText), caption = StrPanelText.LaneCaption(rich);
            TestAssert.That(length != Dash && length.EndsWith("KM"), "the rich census measured a front: " + rich.FrontText);
            TestAssert.That(caption == StrPanelText.NotchCaption + StrWords.Dot + "FRONT " + length, "a measured front rides on the platform header (spec 7 row 8): " + caption);
            foreach (string name in new[] { "unknown", "waiting", "hostlacks", "noanswer", "nofaction" })
                TestAssert.That(StrPanelText.LaneCaption(StrFixtures.Model(name)) == StrPanelText.NotchCaption, name + " measured no front, so its header names none");
            TestAssert.That(StrPanelText.FrontLength("14.2 KM" + StrWords.Dot + "5 PLATFORMS") == "14.2 KM" && StrPanelText.FrontLength("14.2 KM") == "14.2 KM", "the length is the first part of the sensors text");
            TestAssert.That(StrPanelText.FrontLength(null) == Dash && StrPanelText.FrontLength("") == Dash && StrPanelText.LaneCaption(null) == StrPanelText.NotchCaption, "nothing to read is a dash and no caption claim");
        }

        private static void ControlIdsFollowTheGrammar()
        {
            string[] good =
            {
                "tab.board", "tab.sitrep", "tab.info", "posture.cautious", "posture.steady", "posture.bold", "replan", "hero.locate",
                "prop.1.pick", "prop.3.locate", "front.1.redirect", "front.8.locate", "sortie.1.accept", "sortie.3.wing", "sortie.2.locate",
                "layer.fronts", "layer.operation", "layer.sorties", "map.card.primary", "map.card.secondary", "map.card.close", "map.tag.prop.1", "map.tag.effort.1", "map.tag.post.2",
            };
            foreach (string id in good) TestAssert.That(StrControlIds.IsValid(id), id + " is a legal id");
            string[] bad = { "", null, "prop.0.pick", "prop.4.pick", "front.9.redirect", "sortie.4.accept", "prop.1.accept", "Prop.1.pick", "posture.reckless", "tab.map", "hero.replan", "wave.locate" };
            foreach (string id in bad) TestAssert.That(!StrControlIds.IsValid(id), (id ?? "null") + " is not a legal id");
            TestAssert.That(StrControlIds.Prop(2, "pick") == "prop.2.pick" && StrControlIds.Front(8, "redirect") == "front.8.redirect" && StrControlIds.Sortie(3, "wing") == "sortie.3.wing", "the builders spell the grammar");
            TestAssert.That(StrControlIds.Posture(TheaterWarPosture.Bold) == "posture.bold" && StrControlIds.Tab(1) == "tab.sitrep", "posture and tab ids");
        }

        private static void TheBannerInkSeparatesAPickFromAnOutage()
        {
            TestAssert.That(StrPanelText.BannerInk(StrBoardMode.Proposals, false) == StrInkRole.Effort, "a deck to pick from is the effort amber");
            TestAssert.That(StrPanelText.BannerInk(StrBoardMode.Operation, true) == StrInkRole.Caution && StrPanelText.BannerInk(StrBoardMode.Operation, false) == StrInkRole.Muted, "a stale operation board is a caution, a current one is quiet");
            TestAssert.That(StrPanelText.BannerInk(StrBoardMode.Waiting, false) == StrPanelText.BannerInk(StrBoardMode.Assessing, false), "the two modes that wait share the cyan");
            TestAssert.That(StrPanelText.BannerInk(StrBoardMode.NoAnswer, false) != StrPanelText.BannerInk(StrBoardMode.Waiting, false), "no answer reads differently from still waiting");
        }

        private static void ItemIdsResolveAgainstTheModel()
        {
            StrViewModel rich = StrFixtures.Model("rich");
            TestAssert.That(StrControlIds.TryItem(rich, "effort", out StrItemId e) && e == rich.Effort.Id && e.Kind == StrItemKind.Effort, "the effort tag names the live operation");
            TestAssert.That(StrControlIds.TryItem(rich, "map.tag.effort.1", out e) && e == rich.Effort.Id, "the map tag spelling resolves too");
            TestAssert.That(StrControlIds.TryItem(rich, "map.tag.prop.2", out StrItemId p) && p == rich.Proposals[1].Id && p.Kind == StrItemKind.Proposal, "prop.2 is the second proposal");
            TestAssert.That(StrControlIds.TryItem(rich, "front.1", out StrItemId f) && f == rich.Fronts[0].Id, "front.1 is the first front row");
            TestAssert.That(StrControlIds.TryItem(rich, "sortie.3", out StrItemId s) && s == rich.Sorties[2].Id, "sortie.3 is the third ticket");
            TestAssert.That(StrControlIds.TryItem(rich, "post.2", out StrItemId po) && po == rich.Posts[1].Id && po.Kind == StrItemKind.Post, "post.2 is the second office");
            foreach (string bad in new[] { "", null, "prop", "prop.0", "prop.9", "prop.x", "sortie.99", "front.1.pick", "banana.1", "post.3", "effort.2" })
                TestAssert.That(!StrControlIds.TryItem(rich, bad, out StrItemId none) && none.IsNone, (bad ?? "null") + " names nothing");
            TestAssert.That(!StrControlIds.TryItem(StrFixtures.Model("proposals"), "effort", out _), "no operation, no effort tag");
            TestAssert.That(!StrControlIds.TryItem(null, "prop.1", out _), "no model, no item");

            int lane1 = StrControlIds.RedirectLane(rich, 1), lane2 = StrControlIds.RedirectLane(rich, 2);
            TestAssert.That(lane1 >= 1 && !rich.Fronts[lane1 - 1].Offer.IsNone, "the first redirect is a lane that has an offer");
            TestAssert.That(lane2 > lane1 || lane2 == 0, "the second redirect comes after the first");
            TestAssert.That(StrControlIds.RedirectLane(rich, 0) == 0 && StrControlIds.RedirectLane(rich, 99) == 0 && StrControlIds.RedirectLane(null, 1) == 0, "no such redirect, no lane");
        }
    }
}
