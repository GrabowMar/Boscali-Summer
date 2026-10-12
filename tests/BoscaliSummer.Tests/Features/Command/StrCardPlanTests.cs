using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The click card of the map overlay (annex 05 P10): what it says and which one button commits, as pure data over the model.</summary>
    internal static class StrCardPlanTests
    {
        private const string Dash = "—";

        public static void Run()
        {
            AProposalCardCommitsThePickAndSaysRedirectWhileAnOperationRuns();
            ARefusedPickShowsItsReasonInTheFooter();
            TheEffortAndFrontCardsOnlyFocus();
            AnOfferedSortieAcceptsAndShowsTheWingReasonAndAnActiveOneSendsTheWing();
            ATaskedSortieOffersRecall();
            APostCardAcceptsItsTicketWhenOneIsOffered();
            AnItemThatLeftTheBoardBuildsNothing();
            UnknownNumbersPrintAsDashes();
            EveryFixtureAndEveryItemBuildsInsideTheCaps();
            TheLayerLineSaysWhatTheBoardIs();
        }

        private static StrCardPlan Card(StrViewModel vm, StrItemId id)
        {
            var plan = new StrCardPlan();
            StrCardPlan.Build(plan, vm, id);
            return plan;
        }

        private static StrSortieRow Sortie(StrViewModel vm, bool active)
        {
            for (int i = 0; i < vm.SortieCount; i++) if (vm.Sorties[i].Active == active && vm.Sorties[i].Offered == !active) return vm.Sorties[i];
            throw new InvalidOperationException("no " + (active ? "accepted" : "offered") + " sortie on the board");
        }

        private static string All(StrCardPlan c)
        {
            string s = c.Tag + "|" + c.Chip + "|" + c.Title;
            for (int i = 0; i < c.LineCount; i++) s += "|" + c.Lines[i];
            return s;
        }

        private static void AProposalCardCommitsThePickAndSaysRedirectWhileAnOperationRuns()
        {
            StrViewModel live = StrFixtures.Model("rich");
            StrProposalRow p = live.Proposals[0];
            StrCardPlan c = Card(live, p.Id);
            TestAssert.That(c.Valid && c.Id == p.Id, "a proposal on the board builds a card");
            TestAssert.That(c.Tag == p.Tag && c.Chip == p.KindText && c.Title == p.Name, "the card carries the model's tag, kind and place words");
            TestAssert.That(c.Primary.Act == StrCardAct.Pick && c.Primary.Target == p.Id, "the primary button picks this proposal");
            TestAssert.That(c.Primary.Label == "REDIRECT HERE", "while an operation runs the pick is a redirect");
            TestAssert.That(c.Primary.Gate.Allowed, "a seated pilot on a live board may redirect");
            TestAssert.That(c.Secondary.Act == StrCardAct.Focus && c.Secondary.Target == p.Id && c.Secondary.Label == "FOCUS", "the secondary button flies the map to it");
            TestAssert.That(c.Reason.Length == 0, "nothing is refused, so the footer is empty");
            TestAssert.That(All(c).Contains(p.Aim) && All(c).Contains(p.RiskText), "the card says who is near and how risky it is, in the model's words");

            StrViewModel deck = StrFixtures.Model("proposals");
            StrCardPlan d = Card(deck, deck.Proposals[1].Id);
            TestAssert.That(d.Primary.Label == "MAKE MAIN EFFORT", "with no operation the pick makes the main effort");
        }

        private static void ARefusedPickShowsItsReasonInTheFooter()
        {
            StrViewModel locked = StrFixtures.Model("locked-by-other");
            StrCardPlan c = Card(locked, locked.Proposals[0].Id);
            TestAssert.That(!c.Primary.Gate.Allowed && c.Primary.Gate.Why == StrWhy.Locked, "another pilot's lock refuses the pick");
            TestAssert.That(c.Reason == c.Primary.Gate.Text && c.Reason.Contains("LOCKED"), "the footer carries the refusal words");
            TestAssert.That(c.Secondary.Gate.Allowed, "locating is never refused for a placed item");

            StrViewModel stale = StrFixtures.Model("stale");
            StrCardPlan s = Card(stale, stale.Proposals[0].Id);
            TestAssert.That(s.Primary.Gate.Why == StrWhy.Stale && s.Reason.Length >= 8, "a stale report refuses the pick and says so");
        }

        private static void TheEffortAndFrontCardsOnlyFocus()
        {
            StrViewModel vm = StrFixtures.Model("rich");
            StrCardPlan e = Card(vm, vm.Effort.Id);
            TestAssert.That(e.Valid && e.Primary.Act == StrCardAct.Focus && e.Secondary.Act == StrCardAct.None, "the effort card has one button, FOCUS");
            TestAssert.That(All(e).Contains(vm.Effort.PhaseText) && All(e).Contains(vm.Effort.Name), "it names the operation and its phase");
            StrCardPlan f = Card(vm, vm.Fronts[0].Id);
            TestAssert.That(f.Valid && f.Primary.Act == StrCardAct.Focus && f.Secondary.Act == StrCardAct.None, "a front card has one button, FOCUS");
            TestAssert.That(f.Tag == vm.Fronts[0].Tag && f.Title == vm.Fronts[0].Name, "it carries the platform number and the place");
            TestAssert.That(All(f).Contains(vm.Fronts[0].PressureText), "it carries the lane's pressure words");
        }

        private static void AnOfferedSortieAcceptsAndShowsTheWingReasonAndAnActiveOneSendsTheWing()
        {
            StrViewModel vm = StrFixtures.Model("rich");
            StrSortieRow offered = Sortie(vm, false), active = Sortie(vm, true);
            TestAssert.That(offered.Offered && active.Active, "the rich board has an offered and an accepted sortie");
            StrCardPlan o = Card(vm, offered.Id);
            TestAssert.That(o.Primary.Act == StrCardAct.Accept && o.Primary.Target == offered.Id && o.Primary.Label == "ACCEPT", "an offered sortie's primary button accepts it");
            TestAssert.That(o.Secondary.Act == StrCardAct.SendWing && o.Secondary.Label == offered.WingLabel, "the wing button sits beside it with the model's label");
            TestAssert.That(!o.Secondary.Gate.Allowed && o.Secondary.Gate.Why == StrWhy.WingRefused && o.Reason == o.Secondary.Gate.Text, "an offered sortie cannot be sent a wing yet, and the footer says accept it first (N-07)");
            TestAssert.That(All(o).Contains(offered.DistanceText) && All(o).Contains(offered.RewardText), "distance and reward are on the card");

            StrCardPlan a = Card(vm, active.Id);
            TestAssert.That(a.Primary.Act == StrCardAct.SendWing && a.Primary.Label == active.WingLabel && a.Primary.Gate.Allowed == active.Wing.Allowed, "an accepted sortie's primary button is the wing button");
            TestAssert.That(a.Secondary.Act == StrCardAct.Focus, "and the secondary one flies the map to it");
            TestAssert.That(All(a).Contains(active.StateText), "its state is on the card");

            StrViewModel client = StrFixtures.Model("client");
            StrCardPlan cl = Card(client, Sortie(client, true).Id);
            TestAssert.That(!cl.Primary.Gate.Allowed && cl.Primary.Gate.Why == StrWhy.HostOnly && cl.Reason.Length >= 20, "a client's wing button says only the host can send a wing");
        }

        private static void ATaskedSortieOffersRecall()
        {
            StrViewModel vm = StrFixtures.Model("rich");
            StrSortieRow row = Sortie(vm, true);
            row.HasWing = true; row.WingState = WingSortieState.Tasked; row.WingLabel = "RECALL";
            row.Wing = StrGate.Check(vm, StrActionKind.Recall, row.Id);
            StrCardPlan c = Card(vm, row.Id);
            TestAssert.That(StrPanelText.WingTasked(row), "a tasked wing reads as tasked");
            TestAssert.That(c.Primary.Act == StrCardAct.Recall && c.Primary.Label == "RECALL" && c.Primary.Gate.Allowed, "a tasked sortie's wing button recalls");
            row.WingState = WingSortieState.OnStation;
            TestAssert.That(StrPanelText.WingTasked(row), "a wing on station is tasked too");
            row.WingState = WingSortieState.Done;
            TestAssert.That(!StrPanelText.WingTasked(row), "a finished tasking is not");
            row.HasWing = false;
            TestAssert.That(!StrPanelText.WingTasked(row) && !StrPanelText.WingTasked(null), "no wing entry, no tasking");
        }

        private static void APostCardAcceptsItsTicketWhenOneIsOffered()
        {
            StrViewModel vm = StrFixtures.Model("rich");
            StrPostRow post = vm.Posts[0];
            StrCardPlan bare = Card(vm, post.Id);
            TestAssert.That(bare.Valid && bare.Title == post.Name && bare.Chip.Length > 0, "a post card names the officer");
            TestAssert.That(bare.Primary.Act == StrCardAct.Focus && bare.Secondary.Act == StrCardAct.None, "with no ticket the post card only focuses");
            TestAssert.That(All(bare).Contains(post.BountyText) && All(bare).Contains(post.Role), "bounty and role are on the dossier");

            post.Ticket = Sortie(vm, false).Id;
            StrCardPlan ticketed = Card(vm, post.Id);
            TestAssert.That(ticketed.Primary.Act == StrCardAct.Accept && ticketed.Primary.Target == Sortie(vm, false).Id, "an offered strike ticket makes ACCEPT the primary button, aimed at the ticket");
            TestAssert.That(ticketed.Secondary.Act == StrCardAct.Focus && ticketed.Secondary.Target == post.Id, "FOCUS stays on the post");
            post.Ticket = Sortie(vm, true).Id;
            TestAssert.That(Card(vm, post.Id).Primary.Act == StrCardAct.Focus, "a ticket that is already accepted is not offered again");
        }

        private static void AnItemThatLeftTheBoardBuildsNothing()
        {
            StrViewModel vm = StrFixtures.Model("rich");
            StrCardPlan c = Card(vm, new StrItemId(StrItemKind.Proposal, 999));
            TestAssert.That(!c.Valid && c.LineCount == 0 && c.Primary.Act == StrCardAct.None && c.Secondary.Act == StrCardAct.None && c.Reason.Length == 0, "an id that is not on the board builds an empty card");
            TestAssert.That(!Card(vm, StrItemId.None).Valid && !Card(null, vm.Effort.Id).Valid, "no id and no model build nothing");
            StrCardPlan reuse = Card(vm, vm.Proposals[0].Id);
            StrCardPlan.Build(reuse, vm, new StrItemId(StrItemKind.Sortie, 4242));
            TestAssert.That(!reuse.Valid && reuse.LineCount == 0 && reuse.Title.Length == 0, "rebuilding into a used plan clears it first");
        }

        private static void UnknownNumbersPrintAsDashes()
        {
            StrViewModel vm = StrFixtures.Model("unknown");
            StrCardPlan p = Card(vm, vm.Proposals[0].Id);
            TestAssert.That(p.Valid && All(p).Contains(Dash), "an unknown count reads as a dash on the card");
            TestAssert.That(!System.Text.RegularExpressions.Regex.IsMatch(All(p), @"\b0\s*%"), "and never as a zero percent");
            StrCardPlan f = Card(vm, vm.Fronts[0].Id);
            TestAssert.That(f.Valid && All(f).Contains(Dash), "an unobserved front reads as a dash");
            StrCardPlan s = Card(vm, vm.Sorties[0].Id);
            TestAssert.That(s.Valid && All(s).Contains(Dash), "a sortie with no known distance reads as a dash");
        }

        private static void EveryFixtureAndEveryItemBuildsInsideTheCaps()
        {
            var plan = new StrCardPlan();
            foreach (string name in StrFixtures.Names)
            {
                StrViewModel vm = StrFixtures.Model(name);
                var ids = new System.Collections.Generic.List<StrItemId>();
                if (vm.Effort.Live) ids.Add(vm.Effort.Id);
                for (int i = 0; i < vm.ProposalCount; i++) ids.Add(vm.Proposals[i].Id);
                for (int i = 0; i < vm.FrontCount; i++) ids.Add(vm.Fronts[i].Id);
                for (int i = 0; i < vm.SortieCount; i++) ids.Add(vm.Sorties[i].Id);
                for (int i = 0; i < vm.PostCount; i++) ids.Add(vm.Posts[i].Id);
                foreach (StrItemId id in ids)
                {
                    StrCardPlan.Build(plan, vm, id);
                    TestAssert.That(plan.Valid && plan.Id == id, name + " " + id.Kind + " " + id.Key + " builds a card");
                    TestAssert.That(plan.LineCount <= StrCardPlan.MaxLines && plan.Title != null && plan.Title.Length > 0, name + " keeps inside the line cap and names the item");
                    TestAssert.That(plan.Primary.Act != StrCardAct.None, name + " always has a primary button");
                    for (int i = 0; i < plan.LineCount; i++) TestAssert.That(!string.IsNullOrEmpty(plan.Lines[i]), name + " writes no empty line");
                    if (!plan.Primary.Gate.Allowed) TestAssert.That(plan.Reason.Length > 0, name + " says why a refused button is refused");
                }
            }
        }

        private static void TheLayerLineSaysWhatTheBoardIs()
        {
            StrViewModel rich = StrFixtures.Model("rich");
            string live = StrPanelText.LayerLine(rich);
            TestAssert.That(live.StartsWith("BOARD ") && live.Contains(rich.AgeText.ToUpperInvariant()) && live.EndsWith("BDF"), "a live board names its age and faction: " + live);
            string stale = StrPanelText.LayerLine(StrFixtures.Model("stale"));
            TestAssert.That(stale.Contains("STALE"), "a stale board says so: " + stale);
            string waiting = StrPanelText.LayerLine(StrFixtures.Model("waiting"));
            TestAssert.That(!waiting.Contains("OLD") && waiting.Length >= 8, "a board that never arrived claims no age: " + waiting);
            string unknown = StrPanelText.LayerLine(StrFixtures.Model("unknown"));
            TestAssert.That(!unknown.Contains(" 0S") && unknown.Contains("UNKNOWN"), "an unknown age is not a zero: " + unknown);
            TestAssert.That(StrPanelText.LayerLine(null).Length > 0, "no model still reads");
        }
    }
}
