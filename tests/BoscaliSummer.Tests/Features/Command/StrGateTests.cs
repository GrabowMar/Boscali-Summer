using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>One reason string per refusal, from the model alone (annex 05 P3 gate rules, in order, first failure wins).</summary>
    internal static class StrGateTests
    {
        private static readonly StrItemId Prop7 = new StrItemId(StrItemKind.Proposal, 7);
        private static readonly StrItemId Sortie3 = new StrItemId(StrItemKind.Sortie, 3);

        public static void Run()
        {
            PickRefusalsComeInOrder();
            ALockHeldByAnotherPilotNamesThemAndTheClock();
            MoneyIsNeverAPickRefusal();
            ReplanNeedsALiveOperation();
            PostureRefusesTheCurrentOne();
            AcceptRefusalsComeInOrder();
            TheWingButtonIsHostOnlyAndNeedsAnAcceptedSortie();
            RecallNeedsATaskingAndNothingElse();
            AStaleFeedDisablesOnlyItsOwnVerbs();
            AMissingModelOrActionRefusesInsteadOfThrowing();
        }

        /// <summary>A healthy Operation board: a live effort, one proposal (id 7), one offered sortie (id 3) with a wing entry.</summary>
        private static StrViewModel Board()
        {
            var vm = new StrViewModel
            {
                Mode = StrBoardMode.Operation, CanCommand = true, IsHost = true, SortiesAvailable = true, SortiesLive = true,
                Posture = TheaterWarPosture.Steady, AgeSeconds = 4f,
            };
            vm.Effort.Live = true; vm.Effort.Id = new StrItemId(StrItemKind.Effort, 11);
            vm.Proposals[0] = new StrProposalRow { Id = Prop7, ProposalId = 7, Revision = 2 }; vm.ProposalCount = 1;
            vm.Sorties[0] = new StrSortieRow { Id = Sortie3, ContractId = 3, Offered = true, HasWing = true }; vm.SortieCount = 1;
            return vm;
        }

        private static void PickRefusalsComeInOrder()
        {
            StrViewModel vm = Board();
            TestAssert.That(StrGate.Check(vm, StrActionKind.Pick, Prop7).Allowed, "a healthy board lets the pilot pick");

            foreach (StrBoardMode mode in new[] { StrBoardMode.Assessing, StrBoardMode.NoOpening, StrBoardMode.Reduced, StrBoardMode.StaffOff,
                                                  StrBoardMode.HostLacksStaff, StrBoardMode.NoFaction, StrBoardMode.Waiting, StrBoardMode.NoAnswer })
            {
                StrViewModel m = Board(); m.Mode = mode; m.Banner.Head = "THE STAFF IS SURVEYING THE FRONT";
                StrAct a = StrGate.Check(m, StrActionKind.Pick, Prop7);
                TestAssert.That(a.Why == StrWhy.NoBoard && a.Text == "THE STAFF IS SURVEYING THE FRONT", mode + " has no board to pick from and says what the banner says");
            }

            StrViewModel stale = Board(); stale.Stale = true; stale.AgeSeconds = 20.7f; stale.WarPending = true; stale.CanCommand = false;
            StrAct s = StrGate.Check(stale, StrActionKind.Pick, Prop7);
            TestAssert.That(s.Why == StrWhy.Stale && s.Text.Contains("20 S OLD") && s.Text.Contains("HOST"), "a stale report refuses first, with its age");

            StrViewModel pending = Board(); pending.WarPending = true; pending.CanCommand = false;
            TestAssert.That(StrGate.Check(pending, StrActionKind.Pick, Prop7).Why == StrWhy.Pending, "an order in flight refuses before the staff's own state");

            StrViewModel off = Board(); off.CanCommand = false;
            StrAct n = StrGate.Check(off, StrActionKind.Pick, Prop7);
            TestAssert.That(n.Why == StrWhy.NoBoard && n.Text.Length > 10, "a staff that is not taking orders refuses with a sentence");

            StrAct gone = StrGate.Check(vm, StrActionKind.Pick, new StrItemId(StrItemKind.Proposal, 8));
            TestAssert.That(gone.Why == StrWhy.Gone, "a proposal that left the deck is Gone");
            TestAssert.That(StrGate.Check(vm, StrActionKind.Pick, new StrItemId(StrItemKind.Front, 7)).Why == StrWhy.Gone &&
                            StrGate.Check(vm, StrActionKind.Pick, StrItemId.None).Why == StrWhy.Gone, "only a proposal id can be picked, whatever its key");
        }

        private static void ALockHeldByAnotherPilotNamesThemAndTheClock()
        {
            StrViewModel vm = Board();
            vm.Effort.LockSeconds = 92; vm.Effort.LockText = "01:32"; vm.Effort.PickedBy = "Viper 1-1";
            StrAct a = StrGate.Check(vm, StrActionKind.Pick, Prop7);
            TestAssert.That(a.Why == StrWhy.Locked && a.Text.Contains("VIPER 1-1") && a.Text.Contains("01:32"), "the lock names the pilot and the time left");

            vm.Effort.PickedByLocal = true;
            TestAssert.That(StrGate.Check(vm, StrActionKind.Pick, Prop7).Allowed, "the picker may redirect at any time");
            vm.Effort.PickedByLocal = false; vm.Effort.PickedByStaff = true;
            TestAssert.That(StrGate.Check(vm, StrActionKind.Pick, Prop7).Allowed, "a staff pick locks nobody out");
            vm.Effort.PickedByStaff = false; vm.Effort.LockSeconds = 0;
            TestAssert.That(StrGate.Check(vm, StrActionKind.Pick, Prop7).Allowed, "an expired lock holds nobody");
            vm.Effort.LockSeconds = -1;
            TestAssert.That(StrGate.Check(vm, StrActionKind.Pick, Prop7).Allowed, "no lock holds nobody");
            vm.Effort.LockSeconds = 30; vm.Effort.LockText = ""; vm.Effort.PickedBy = "";
            StrAct unnamed = StrGate.Check(vm, StrActionKind.Pick, Prop7);
            TestAssert.That(unnamed.Why == StrWhy.Locked && unnamed.Text == "LOCKED", "an unknown picker is not invented");
        }

        private static void MoneyIsNeverAPickRefusal()
        {
            StrViewModel vm = Board();
            vm.Wave.Present = true; vm.Wave.Pool = 0; vm.Wave.Cost = 1850; vm.Wave.Affordable = StrTri.No; vm.Wave.Held = true;
            TestAssert.That(StrGate.Check(vm, StrActionKind.Pick, Prop7).Allowed, "an empty pool does not change a pick");
            vm.Wave.Pool = -1; vm.Wave.Affordable = StrTri.Unknown;
            TestAssert.That(StrGate.Check(vm, StrActionKind.Pick, Prop7).Allowed, "an unknown pool does not either");
        }

        private static void ReplanNeedsALiveOperation()
        {
            StrViewModel vm = Board();
            TestAssert.That(StrGate.Check(vm, StrActionKind.Replan, vm.Effort.Id).Allowed, "an operation can be called off");
            StrViewModel none = Board(); none.Mode = StrBoardMode.Proposals; none.Effort.Live = false;
            TestAssert.That(StrGate.Check(none, StrActionKind.Replan, StrItemId.None).Why == StrWhy.NoOperation, "with no operation there is nothing to call off");
            StrViewModel stale = Board(); stale.Stale = true;
            TestAssert.That(StrGate.Check(stale, StrActionKind.Replan, vm.Effort.Id).Why == StrWhy.Stale, "a stale report refuses a call-off");
            StrViewModel pend = Board(); pend.WarPending = true;
            TestAssert.That(StrGate.Check(pend, StrActionKind.Replan, vm.Effort.Id).Why == StrWhy.Pending, "a pending order refuses a call-off");
        }

        private static void PostureRefusesTheCurrentOne()
        {
            StrViewModel vm = Board();
            StrAct same = StrGate.Check(vm, StrActionKind.Posture, StrItemId.None, (int)TheaterWarPosture.Steady);
            TestAssert.That(same.Why == StrWhy.SamePosture && same.Text.Contains("STEADY"), "the current posture is refused by name");
            TestAssert.That(StrGate.Check(vm, StrActionKind.Posture, StrItemId.None, (int)TheaterWarPosture.Bold).Allowed, "another posture is allowed");
            foreach (StrBoardMode mode in new[] { StrBoardMode.Proposals, StrBoardMode.Assessing, StrBoardMode.NoOpening })
            {
                StrViewModel m = Board(); m.Mode = mode;
                TestAssert.That(StrGate.Check(m, StrActionKind.Posture, StrItemId.None, (int)TheaterWarPosture.Bold).Allowed, "the posture row works in " + mode);
            }
            StrViewModel reduced = Board(); reduced.Mode = StrBoardMode.Reduced; reduced.Banner.Head = "NO GROUND WAR HERE";
            TestAssert.That(StrGate.Check(reduced, StrActionKind.Posture, StrItemId.None, (int)TheaterWarPosture.Bold).Why == StrWhy.NoBoard, "a mission with no ground war has no posture to set");
            StrViewModel stale = Board(); stale.Stale = true;
            TestAssert.That(StrGate.Check(stale, StrActionKind.Posture, StrItemId.None, (int)TheaterWarPosture.Bold).Why == StrWhy.Stale, "a stale report refuses a posture change");
        }

        private static void AcceptRefusalsComeInOrder()
        {
            StrViewModel vm = Board();
            TestAssert.That(StrGate.Check(vm, StrActionKind.Accept, Sortie3).Allowed, "an offered sortie can be accepted");

            StrViewModel none = Board(); none.SortiesAvailable = false; none.SortiesLive = false;
            TestAssert.That(StrGate.Check(none, StrActionKind.Accept, Sortie3).Why == StrWhy.NoBoard, "with no sortie feed there is nothing to accept");
            StrViewModel stale = Board(); stale.SortiesLive = false; stale.SortieActionPending = true;
            TestAssert.That(StrGate.Check(stale, StrActionKind.Accept, Sortie3).Why == StrWhy.Stale, "a feed that is not current refuses before anything else");
            StrViewModel pending = Board(); pending.SortieActionPending = true;
            TestAssert.That(StrGate.Check(pending, StrActionKind.Accept, Sortie3).Why == StrWhy.Pending, "an accept in flight refuses a second one");

            StrViewModel taken = Board(); taken.Sorties[0].Offered = false; taken.Sorties[0].Active = true;
            TestAssert.That(StrGate.Check(taken, StrActionKind.Accept, Sortie3).Why == StrWhy.NotOffered, "an accepted sortie is not offered");
            TestAssert.That(StrGate.Check(vm, StrActionKind.Accept, new StrItemId(StrItemKind.Sortie, 99)).Why == StrWhy.NotOffered, "a sortie off the board is not offered");

            StrViewModel full = Board(); full.SortieActiveLimit = 3; full.SortieActiveCount = 3;
            StrAct f = StrGate.Check(full, StrActionKind.Accept, Sortie3);
            TestAssert.That(f.Why == StrWhy.BoardFull && f.Text.Contains("3 OF 3"), "at the ceiling the refusal names the count");
            full.SortieActiveLimit = 0;
            TestAssert.That(StrGate.Check(full, StrActionKind.Accept, Sortie3).Allowed, "no reported ceiling means no ceiling to hit");
            full.SortieActiveLimit = 4;
            TestAssert.That(StrGate.Check(full, StrActionKind.Accept, Sortie3).Allowed, "below the ceiling is fine");
        }

        private static void TheWingButtonIsHostOnlyAndNeedsAnAcceptedSortie()
        {
            StrViewModel vm = Board(); vm.Sorties[0].Offered = false; vm.Sorties[0].Active = true;
            TestAssert.That(StrGate.Check(vm, StrActionKind.SendWing, Sortie3).Allowed, "an accepted sortie with a wing order can be sent");

            StrViewModel client = Board(); client.IsHost = false; client.Sorties[0].Offered = false; client.Sorties[0].Active = true;
            StrAct h = StrGate.Check(client, StrActionKind.SendWing, Sortie3);
            TestAssert.That(h.Why == StrWhy.HostOnly && h.Text.StartsWith("ONLY THE HOST CAN SEND A WING") && h.Text.Contains("FLY IT YOURSELF"), "a client is told who can send the wing");

            StrAct offered = StrGate.Check(Board(), StrActionKind.SendWing, Sortie3);
            TestAssert.That(offered.Why == StrWhy.WingRefused && offered.Text.StartsWith("ACCEPT THIS SORTIE FIRST"), "an offered card says to accept it first");

            StrViewModel over = Board(); over.Sorties[0].Offered = false; over.Sorties[0].Active = false;
            TestAssert.That(StrGate.Check(over, StrActionKind.SendWing, Sortie3).Why == StrWhy.NoWing, "a finished sortie takes no wing");

            StrViewModel nowing = Board(); nowing.Sorties[0].Offered = false; nowing.Sorties[0].Active = true; nowing.Sorties[0].HasWing = false;
            StrAct n = StrGate.Check(nowing, StrActionKind.SendWing, Sortie3);
            TestAssert.That(n.Why == StrWhy.NoWing && n.Text.Contains("RECRUIT"), "no wing order names the way out");

            StrViewModel refused = Board(); refused.Sorties[0].Offered = false; refused.Sorties[0].Active = true;
            refused.Sorties[0].WingRefusal = "No wingmen are recruited. Recruit a wing at a hangar, then accept the sortie.";
            StrAct r = StrGate.Check(refused, StrActionKind.SendWing, Sortie3);
            TestAssert.That(r.Why == StrWhy.WingRefused && r.Text == refused.Sorties[0].WingRefusal, "the host's own refusal is shown verbatim");

            TestAssert.That(StrGate.Check(vm, StrActionKind.SendWing, new StrItemId(StrItemKind.Sortie, 99)).Why == StrWhy.NoWing, "a sortie off the board has no wing button");
        }

        private static void RecallNeedsATaskingAndNothingElse()
        {
            StrViewModel vm = Board(); vm.Sorties[0].Offered = false; vm.Sorties[0].Active = true;
            TestAssert.That(StrGate.Check(vm, StrActionKind.Recall, Sortie3).Why == StrWhy.NoWing, "nothing tasked, nothing to recall");
            foreach (WingSortieState state in new[] { WingSortieState.Tasked, WingSortieState.OnStation })
            {
                vm.Sorties[0].WingState = state;
                TestAssert.That(StrGate.Check(vm, StrActionKind.Recall, Sortie3).Allowed, "a wing that is " + state + " can be recalled");
            }
            foreach (WingSortieState state in new[] { WingSortieState.Done, WingSortieState.Failed, WingSortieState.Recalled, WingSortieState.None })
            {
                vm.Sorties[0].WingState = state;
                TestAssert.That(StrGate.Check(vm, StrActionKind.Recall, Sortie3).Why == StrWhy.NoWing, "a wing that is " + state + " is not recalled");
            }
            vm.Sorties[0].WingState = WingSortieState.Tasked; vm.SortiesLive = false;
            TestAssert.That(StrGate.Check(vm, StrActionKind.Recall, Sortie3).Allowed, "recalling does not wait for the sortie list");
            vm.IsHost = false;
            TestAssert.That(StrGate.Check(vm, StrActionKind.Recall, Sortie3).Why == StrWhy.HostOnly, "a client cannot recall");
        }

        private static void AStaleFeedDisablesOnlyItsOwnVerbs()
        {
            StrViewModel warStale = Board(); warStale.Stale = true; warStale.Sorties[0].Offered = false; warStale.Sorties[0].Active = true;
            TestAssert.That(StrGate.Check(warStale, StrActionKind.Pick, Prop7).Why == StrWhy.Stale, "a stale war report refuses pick");
            TestAssert.That(StrGate.Check(warStale, StrActionKind.Replan, warStale.Effort.Id).Why == StrWhy.Stale, "and replan");
            TestAssert.That(StrGate.Check(warStale, StrActionKind.Posture, StrItemId.None, 2).Why == StrWhy.Stale, "and posture");
            TestAssert.That(StrGate.Check(warStale, StrActionKind.SendWing, Sortie3).Allowed, "but not the wing, which is the sortie list's verb");
            warStale.Sorties[0].Offered = true; warStale.Sorties[0].Active = false;
            TestAssert.That(StrGate.Check(warStale, StrActionKind.Accept, Sortie3).Allowed, "nor accept");

            StrViewModel sortieStale = Board(); sortieStale.SortiesLive = false; sortieStale.Sorties[0].Offered = false; sortieStale.Sorties[0].Active = true;
            TestAssert.That(StrGate.Check(sortieStale, StrActionKind.Accept, Sortie3).Why == StrWhy.Stale, "a stale sortie list refuses accept");
            TestAssert.That(StrGate.Check(sortieStale, StrActionKind.SendWing, Sortie3).Why == StrWhy.Stale, "and send wing");
            TestAssert.That(StrGate.Check(sortieStale, StrActionKind.Pick, Prop7).Allowed, "but not pick");
            TestAssert.That(StrGate.Check(sortieStale, StrActionKind.Posture, StrItemId.None, 2).Allowed, "nor posture");
        }

        private static void AMissingModelOrActionRefusesInsteadOfThrowing()
        {
            TestAssert.That(StrGate.Check(null, StrActionKind.Pick, Prop7).Why == StrWhy.NoBoard, "no model, no board");
            TestAssert.That(!StrGate.Check(Board(), (StrActionKind)99, Prop7).Allowed, "an unknown action is refused");
            TestAssert.That(!new StrAct(StrWhy.NoBoard, null).Sent, "a refusal sends nothing");
        }
    }
}
