using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The runner behind every press: gate, then verb, then a local notice; a refused action never reaches a verb (annex 05 P3).</summary>
    internal static class StrActionsTests
    {
        private static readonly StrItemId Prop7 = new StrItemId(StrItemKind.Proposal, 7);
        private static readonly StrItemId Sortie3 = new StrItemId(StrItemKind.Sortie, 3);

        public static void Run()
        {
            AnAllowedPickSendsTheEngineIdsOnce();
            ARefusedActionCallsNoVerbAndLeavesTheNoticeAlone();
            ReplanNeedsTwoPressesInsideFourSeconds();
            ARefusedReplanClearsAnArmedState();
            TheArmedStateIsTheOneTheBuilderShows();
            PostureSendsOnceAndRefusesWhatIsNotAPosture();
            AcceptSendsOnceAndLeavesASixSecondNotice();
            TheWingVerbsTurnTheHostsAnswerIntoTheNotice();
            AVerbThatDeclinesIsNeverReportedAsSent();
            AMissingPieceNeverThrowsAndSendsNothing();
        }

        private sealed class FakeVerbs : IStrVerbs
        {
            public readonly List<string> Calls = new List<string>();
            public bool Ok = true;
            public string Answer = "";

            public bool Pick(int proposalId, int revision) { Calls.Add("pick " + proposalId + "/" + revision); return Ok; }
            public bool Cancel(int operationId, int revision) { Calls.Add("cancel " + operationId + "/" + revision); return Ok; }
            public bool Posture(TheaterWarPosture posture) { Calls.Add("posture " + posture); return Ok; }
            public void Accept(int contractId) { Calls.Add("accept " + contractId); }
            public bool SendWing(int contractId, out string answer) { Calls.Add("send " + contractId); answer = Answer; return Ok; }
            public bool Recall(out string answer) { Calls.Add("recall"); answer = Answer; return Ok; }
        }

        /// <summary>A healthy Operation board with distinct engine ids: operation 11 rev 4, proposal 7 rev 2, offered sortie with card id 3 and a wing entry.</summary>
        private static StrViewModel Board()
        {
            var vm = new StrViewModel
            {
                Mode = StrBoardMode.Operation, CanCommand = true, IsHost = true, SortiesAvailable = true, SortiesLive = true,
                Posture = TheaterWarPosture.Steady, AgeSeconds = 4f,
            };
            vm.Effort.Live = true; vm.Effort.Id = new StrItemId(StrItemKind.Effort, 11); vm.Effort.OperationId = 11; vm.Effort.Revision = 4;
            vm.Proposals[0] = new StrProposalRow { Id = Prop7, ProposalId = 7, Revision = 2 }; vm.ProposalCount = 1;
            vm.Sorties[0] = new StrSortieRow { Id = Sortie3, ContractId = 3, Offered = true, HasWing = true }; vm.SortieCount = 1;
            return vm;
        }

        private static StrViewModel Accepted()
        {
            StrViewModel vm = Board();
            vm.Sorties[0].Offered = false; vm.Sorties[0].Active = true;
            return vm;
        }

        private static void AnAllowedPickSendsTheEngineIdsOnce()
        {
            var v = new FakeVerbs(); var ui = default(StrUiState);
            StrAct a = StrActions.Run(StrActionKind.Pick, Prop7, 0, Board(), v, ref ui, 10f);
            TestAssert.That(a.Allowed && a.Sent, "an allowed pick is sent");
            TestAssert.That(v.Calls.Count == 1 && v.Calls[0] == "pick 7/2", "the verb gets the engine's proposal id and revision, once: " + string.Join(",", v.Calls));
            TestAssert.That(ui.Notice == null && ui.ReplanArmedUntil == 0f, "a war verb leaves no local notice (the engine answers through its own command result)");
        }

        private static void ARefusedActionCallsNoVerbAndLeavesTheNoticeAlone()
        {
            var cases = new (string Name, StrActionKind Kind, StrItemId Id, int Arg, Action<StrViewModel> Break, StrWhy Why)[]
            {
                ("a stale report", StrActionKind.Pick, Prop7, 0, vm => vm.Stale = true, StrWhy.Stale),
                ("an order in flight", StrActionKind.Pick, Prop7, 0, vm => vm.WarPending = true, StrWhy.Pending),
                ("another pilot's lock", StrActionKind.Pick, Prop7, 0, vm => { vm.Effort.LockSeconds = 60; vm.Effort.PickedBy = "Raptor 2-3"; }, StrWhy.Locked),
                ("a proposal that left", StrActionKind.Pick, new StrItemId(StrItemKind.Proposal, 99), 0, vm => { }, StrWhy.Gone),
                ("no board", StrActionKind.Pick, Prop7, 0, vm => vm.Mode = StrBoardMode.Waiting, StrWhy.NoBoard),
                ("no operation to call off", StrActionKind.Replan, StrItemId.None, 0, vm => { vm.Mode = StrBoardMode.Proposals; vm.Effort.Live = false; }, StrWhy.NoOperation),
                ("the current posture", StrActionKind.Posture, StrItemId.None, (int)TheaterWarPosture.Steady, vm => { }, StrWhy.SamePosture),
                ("a sortie that is not offered", StrActionKind.Accept, Sortie3, 0, vm => { vm.Sorties[0].Offered = false; vm.Sorties[0].Active = true; }, StrWhy.NotOffered),
                ("a full board", StrActionKind.Accept, Sortie3, 0, vm => { vm.SortieActiveLimit = 2; vm.SortieActiveCount = 2; }, StrWhy.BoardFull),
                ("a client's wing press", StrActionKind.SendWing, Sortie3, 0, vm => { vm.IsHost = false; vm.Sorties[0].Offered = false; vm.Sorties[0].Active = true; }, StrWhy.HostOnly),
                ("a wing with nothing tasked", StrActionKind.Recall, Sortie3, 0, vm => { vm.Sorties[0].Offered = false; vm.Sorties[0].Active = true; }, StrWhy.NoWing),
            };
            foreach (var c in cases)
            {
                StrViewModel vm = Board(); c.Break(vm);
                var v = new FakeVerbs(); var ui = new StrUiState { Notice = "keep", NoticeUntil = 99f, ReplanArmedUntil = 0f };
                StrAct a = StrActions.Run(c.Kind, c.Id, c.Arg, vm, v, ref ui, 10f);
                TestAssert.That(!a.Allowed && !a.Sent && a.Why == c.Why, c.Name + " is refused as " + c.Why + ", got " + a.Why);
                TestAssert.That(a.Text.Length > 0, c.Name + " says why");
                TestAssert.That(v.Calls.Count == 0, c.Name + " reaches no verb");
                TestAssert.That(ui.Notice == "keep" && ui.NoticeUntil == 99f, c.Name + " leaves the notice alone");
            }
        }

        private static void ReplanNeedsTwoPressesInsideFourSeconds()
        {
            StrViewModel vm = Board();
            var v = new FakeVerbs(); var ui = default(StrUiState);

            StrAct first = StrActions.Run(StrActionKind.Replan, vm.Effort.Id, 0, vm, v, ref ui, 100f);
            TestAssert.That(first.Allowed && !first.Sent && first.Text.Length > 0 && v.Calls.Count == 0, "the first press only arms, with a word for it");
            TestAssert.That(ui.ReplanArmedUntil == 104f, "and arms for four seconds on the presentation clock");

            StrAct second = StrActions.Run(StrActionKind.Replan, vm.Effort.Id, 0, vm, v, ref ui, 103.9f);
            TestAssert.That(second.Sent && v.Calls.Count == 1 && v.Calls[0] == "cancel 11/4", "the second press inside the window calls off the operation with the engine's ids: " + string.Join(",", v.Calls));
            TestAssert.That(ui.ReplanArmedUntil == 0f, "and disarms");

            StrActions.Run(StrActionKind.Replan, vm.Effort.Id, 0, vm, v, ref ui, 104f);
            TestAssert.That(v.Calls.Count == 1 && ui.ReplanArmedUntil == 108f, "the next press arms again, it does not repeat the call-off");

            // A second press at the end of the window, or long after it, only re-arms.
            foreach (float late in new[] { 4f, 4.001f, 60f })
            {
                var v2 = new FakeVerbs(); var ui2 = default(StrUiState);
                StrActions.Run(StrActionKind.Replan, vm.Effort.Id, 0, vm, v2, ref ui2, 100f);
                StrAct again = StrActions.Run(StrActionKind.Replan, vm.Effort.Id, 0, vm, v2, ref ui2, 100f + late);
                TestAssert.That(!again.Sent && v2.Calls.Count == 0 && ui2.ReplanArmedUntil == 100f + late + 4f, "a second press " + late + " s later only re-arms");
            }
        }

        private static void ARefusedReplanClearsAnArmedState()
        {
            StrViewModel vm = Board(); vm.Stale = true;
            var v = new FakeVerbs(); var ui = new StrUiState { ReplanArmedUntil = 200f };
            StrAct a = StrActions.Run(StrActionKind.Replan, vm.Effort.Id, 0, vm, v, ref ui, 101f);
            TestAssert.That(a.Why == StrWhy.Stale && ui.ReplanArmedUntil == 0f && v.Calls.Count == 0, "a board that went stale between the presses does not stay armed");

            StrViewModel ended = Board(); ended.Mode = StrBoardMode.Proposals; ended.Effort.Live = false;
            ui = new StrUiState { ReplanArmedUntil = 200f };
            StrActions.Run(StrActionKind.Replan, StrItemId.None, 0, ended, v, ref ui, 101f);
            TestAssert.That(ui.ReplanArmedUntil == 0f && v.Calls.Count == 0, "an operation that ended between the presses does not stay armed");
        }

        private static void TheArmedStateIsTheOneTheBuilderShows()
        {
            // The lane reads CALLS OFF while the builder says armed; the second press must agree with that, at both edges.
            StrFeeds f = StrFixtures.Feeds("rich");
            var vm = new StrViewModel(); var plat = new StrOrdinals("P"); var sor = new StrOrdinals("S");
            var ui = default(StrUiState);
            StrViewBuilder.Build(vm, in f, in ui, plat, sor, StrFixtures.Now);
            TestAssert.That(vm.Effort.Live && !vm.Effort.ReplanArmed, "the rich board has a live operation that is not armed");

            var v = new FakeVerbs();
            StrActions.Run(StrActionKind.Replan, vm.Effort.Id, 0, vm, v, ref ui, StrFixtures.Now + 0.5f);
            StrViewBuilder.Build(vm, in f, in ui, plat, sor, StrFixtures.Now + 1f);
            TestAssert.That(vm.Effort.ReplanArmed, "after the first press the builder shows it armed");
            StrViewBuilder.Build(vm, in f, in ui, plat, sor, StrFixtures.Now + 4.4f);
            TestAssert.That(vm.Effort.ReplanArmed, "still armed just inside the window");
            StrAct inside = StrActions.Run(StrActionKind.Replan, vm.Effort.Id, 0, vm, v, ref ui, StrFixtures.Now + 4.4f);
            TestAssert.That(inside.Sent && v.Calls.Count == 1 && v.Calls[0] == "cancel 21/1", "a press the builder shows as armed sends, with the fixture's operation ids: " + string.Join(",", v.Calls));

            ui = default;
            StrActions.Run(StrActionKind.Replan, vm.Effort.Id, 0, vm, v, ref ui, StrFixtures.Now + 0.5f);
            StrViewBuilder.Build(vm, in f, in ui, plat, sor, StrFixtures.Now + 4.6f);
            TestAssert.That(!vm.Effort.ReplanArmed, "the builder shows it disarmed once the window ended");
            StrAct late = StrActions.Run(StrActionKind.Replan, vm.Effort.Id, 0, vm, v, ref ui, StrFixtures.Now + 4.6f);
            TestAssert.That(!late.Sent && v.Calls.Count == 1, "and a press the builder shows as disarmed only re-arms");
        }

        private static void PostureSendsOnceAndRefusesWhatIsNotAPosture()
        {
            var v = new FakeVerbs(); var ui = default(StrUiState);
            StrAct a = StrActions.Run(StrActionKind.Posture, StrItemId.None, (int)TheaterWarPosture.Bold, Board(), v, ref ui, 5f);
            TestAssert.That(a.Sent && v.Calls.Count == 1 && v.Calls[0] == "posture Bold", "a posture change calls the verb once with the posture: " + string.Join(",", v.Calls));

            foreach (int bad in new[] { -1, 3, 7, 255 })
            {
                var v2 = new FakeVerbs();
                StrAct b = StrActions.Run(StrActionKind.Posture, StrItemId.None, bad, Board(), v2, ref ui, 5f);
                TestAssert.That(!b.Allowed && !b.Sent && b.Text.Length > 0 && v2.Calls.Count == 0, "posture " + bad + " is not a posture and is not sent");
            }
        }

        private static void AcceptSendsOnceAndLeavesASixSecondNotice()
        {
            var v = new FakeVerbs(); var ui = default(StrUiState);
            StrAct a = StrActions.Run(StrActionKind.Accept, Sortie3, 0, Board(), v, ref ui, 50f);
            TestAssert.That(a.Sent && v.Calls.Count == 1 && v.Calls[0] == "accept 3", "accept calls the verb once with the card id: " + string.Join(",", v.Calls));
            TestAssert.That(!string.IsNullOrEmpty(ui.Notice) && ui.Notice == a.Text && ui.NoticeUntil == 56f, "and leaves a notice that lasts six seconds");

            // The strip shows it for those six seconds and then goes back to the flavour line.
            StrFeeds f = StrFixtures.Feeds("rich");
            var vm = new StrViewModel(); var plat = new StrOrdinals("P"); var sor = new StrOrdinals("S");
            var ui2 = default(StrUiState); var v2 = new FakeVerbs();
            StrViewBuilder.Build(vm, in f, in ui2, plat, sor, StrFixtures.Now);
            StrItemId offered = vm.Sorties[0].Offered ? vm.Sorties[0].Id : vm.Sorties[1].Id;
            StrAct sent = StrActions.Run(StrActionKind.Accept, offered, 0, vm, v2, ref ui2, StrFixtures.Now + 1f);
            TestAssert.That(sent.Sent && v2.Calls.Count == 1, "an offered card of the rich board can be accepted");
            StrViewBuilder.Build(vm, in f, in ui2, plat, sor, StrFixtures.Now + 2f);
            TestAssert.That(vm.FooterNote == ui2.Notice, "the strip shows the notice: " + vm.FooterNote);
            StrViewBuilder.Build(vm, in f, in ui2, plat, sor, StrFixtures.Now + 7.5f);
            TestAssert.That(vm.FooterNote != ui2.Notice, "and drops it after six seconds");
        }

        private static void TheWingVerbsTurnTheHostsAnswerIntoTheNotice()
        {
            var v = new FakeVerbs { Answer = "Wing tasked: destroy the SAM." }; var ui = default(StrUiState);
            StrAct send = StrActions.Run(StrActionKind.SendWing, Sortie3, 0, Accepted(), v, ref ui, 20f);
            TestAssert.That(send.Sent && v.Calls.Count == 1 && v.Calls[0] == "send 3", "send wing calls the verb once with the card id: " + string.Join(",", v.Calls));
            TestAssert.That(send.Text == v.Answer && ui.Notice == v.Answer && ui.NoticeUntil == 26f, "the host's answer is the result and the six second notice");

            var quiet = new FakeVerbs { Answer = "" }; var ui2 = default(StrUiState);
            StrAct blank = StrActions.Run(StrActionKind.SendWing, Sortie3, 0, Accepted(), quiet, ref ui2, 20f);
            TestAssert.That(blank.Sent && blank.Text.Length > 0 && ui2.Notice == blank.Text, "an empty answer still gets a word");

            var no = new FakeVerbs { Ok = false, Answer = "No wingmen are recruited. Recruit a wing at a hangar." }; var ui3 = default(StrUiState);
            StrAct refused = StrActions.Run(StrActionKind.SendWing, Sortie3, 0, Accepted(), no, ref ui3, 20f);
            TestAssert.That(!refused.Sent && refused.Why == StrWhy.WingRefused && refused.Text == no.Answer && ui3.Notice == no.Answer, "a refusal from the host is shown verbatim and not reported as sent");
            var silent = new FakeVerbs { Ok = false, Answer = null }; var ui4 = default(StrUiState);
            StrAct nothing = StrActions.Run(StrActionKind.SendWing, Sortie3, 0, Accepted(), silent, ref ui4, 20f);
            TestAssert.That(!nothing.Sent && nothing.Why == StrWhy.WingRefused && nothing.Text.Length > 0, "a refusal with no words still says something");

            StrViewModel tasked = Accepted(); tasked.Sorties[0].WingState = WingSortieState.Tasked;
            var rv = new FakeVerbs { Answer = "Wing recalled." }; var ui5 = default(StrUiState);
            StrAct recall = StrActions.Run(StrActionKind.Recall, Sortie3, 0, tasked, rv, ref ui5, 30f);
            TestAssert.That(recall.Sent && rv.Calls.Count == 1 && rv.Calls[0] == "recall" && ui5.Notice == "Wing recalled." && ui5.NoticeUntil == 36f, "recall calls the verb once and notes the answer");
            var rno = new FakeVerbs { Ok = false, Answer = "Nothing to recall." };
            StrAct recallRefused = StrActions.Run(StrActionKind.Recall, Sortie3, 0, tasked, rno, ref ui5, 30f);
            TestAssert.That(!recallRefused.Sent && recallRefused.Why == StrWhy.WingRefused && recallRefused.Text == "Nothing to recall.", "a recall the host declines is not sent");
        }

        private static void AVerbThatDeclinesIsNeverReportedAsSent()
        {
            var no = new FakeVerbs { Ok = false }; var ui = default(StrUiState);
            StrAct pick = StrActions.Run(StrActionKind.Pick, Prop7, 0, Board(), no, ref ui, 5f);
            TestAssert.That(no.Calls.Count == 1 && !pick.Sent && !pick.Allowed && pick.Text.Length > 0, "a pick the view declines is not Sent and says so");
            StrAct posture = StrActions.Run(StrActionKind.Posture, StrItemId.None, (int)TheaterWarPosture.Bold, Board(), no, ref ui, 5f);
            TestAssert.That(!posture.Sent && !posture.Allowed, "neither is a posture");

            StrViewModel vm = Board();
            StrActions.Run(StrActionKind.Replan, vm.Effort.Id, 0, vm, no, ref ui, 100f);
            StrAct cancel = StrActions.Run(StrActionKind.Replan, vm.Effort.Id, 0, vm, no, ref ui, 101f);
            TestAssert.That(!cancel.Sent && !cancel.Allowed && ui.ReplanArmedUntil == 0f, "a call-off the view declines is not Sent and does not stay armed");
        }

        private static void AMissingPieceNeverThrowsAndSendsNothing()
        {
            var v = new FakeVerbs(); var ui = default(StrUiState);
            TestAssert.That(StrActions.Run(StrActionKind.Pick, Prop7, 0, null, v, ref ui, 1f).Why == StrWhy.NoBoard && v.Calls.Count == 0, "no model, no board, no call");
            StrAct noVerbs = StrActions.Run(StrActionKind.Pick, Prop7, 0, Board(), null, ref ui, 1f);
            TestAssert.That(!noVerbs.Allowed && !noVerbs.Sent && noVerbs.Text.Length > 0, "no verbs, nothing to send with");
            TestAssert.That(!StrActions.Run((StrActionKind)99, Prop7, 0, Board(), v, ref ui, 1f).Allowed && v.Calls.Count == 0, "an unknown action is refused");

            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var ui2 = default(StrUiState); StrViewModel vm = Board();
                StrAct a = StrActions.Run(StrActionKind.Replan, vm.Effort.Id, 0, vm, v, ref ui2, bad);
                StrAct b = StrActions.Run(StrActionKind.Replan, vm.Effort.Id, 0, vm, v, ref ui2, bad);
                TestAssert.That(!a.Sent && !b.Sent && v.Calls.Count == 0 && ui2.ReplanArmedUntil == 0f, "a broken clock (" + bad + ") never arms and never sends a call-off");
            }
        }
    }
}
