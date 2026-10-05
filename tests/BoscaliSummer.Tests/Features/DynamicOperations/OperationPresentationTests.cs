using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.DynamicOperations.Domain;

namespace BoscaliSummer.Tests.Features.DynamicOperations
{
    internal static class OperationPresentationTests
    {
        public static void Run()
        {
            var supply = Active(OperationKind.SupplyEscort);
            ObjectiveTasking join = Project(supply, false);
            TestAssert.That(join.Family == ObjectiveFamily.Logistics && join.CurrentPhase == 0,
                "Supply cover begins with a truthful join instruction");
            TestAssert.That(join.AbortConsequence == OperationFailure.DismissalMessage(true),
                "Abort confirmation receives the actual host-owned consequence");
            supply.Observe(1f, 1f, true, true, false, true);
            ObjectiveTasking cover = Project(supply, true);
            TestAssert.That(cover.CurrentPhase == 1 && cover.Phases[0].Status == ObjectivePhaseStatus.Done,
                "Observed continuous cover advances the presentation");
            for (int i = 2; i <= 60; i++) supply.Observe(i, 1f, true, true, false, true);
            ObjectiveTasking transfer = Project(supply, true);
            TestAssert.That(transfer.CurrentPhase == 2 && transfer.Phases[2].Status == ObjectivePhaseStatus.Current &&
                transfer.Phases[2].Progress == 0f && transfer.NextAction.IndexOf("transfer", StringComparison.OrdinalIgnoreCase) >= 0,
                "Cover alone waits for actual native supply transfer, without a fabricated road destination");
            supply.Observe(61f, 1f, true, true, false, false);
            TestAssert.That(Project(supply, false).CurrentPhase == 0,
                "Interrupted cover resets join and cover presentation with the authoritative interval");
            for (int i = 1; i <= 60; i++) supply.Observe(61f + i, 1f, true, true, false, true);
            supply.Observe(122f, 0f, true, true, false, true, serviced: true);
            ObjectiveTasking completed = Project(supply, true);
            TestAssert.That(completed.Lifecycle == ObjectiveLifecycle.Completed && completed.CurrentPhase == -1 &&
                Array.TrueForAll(completed.Phases, p => p.Status == ObjectivePhaseStatus.Done),
                "Only authoritative completion marks all phases done");

            var report = Active(OperationKind.SortieReport);
            for (int i = 1; i <= 30; i++) report.Observe(i, 1f, true, true, false, true);
            TestAssert.That(report.BeginReturn(31f), "Report acquisition enters the existing return state");
            ObjectiveTasking returning = Project(report, false);
            TestAssert.That(returning.CurrentPhase == 1 && returning.Phases[0].Status == ObjectivePhaseStatus.Done &&
                returning.Phases[1].Progress == 0f && returning.NextAction.Contains("same aircraft") && returning.NextAction.Contains("Friendly Base"),
                "Return instructions preserve carrier and destination constraints");

            var assessment = Active(OperationKind.DamageAssessment);
            TestAssert.That(Project(assessment, true, false).CurrentPhase == 0 &&
                Project(assessment, true, true).CurrentPhase == 1,
                "Damage assessment never claims a survey before neutralization");
            ObjectiveTasking lost = OperationPresentation.Build(assessment, false, false, ObjectiveAsset.Site,
                ObjectiveContact.Lost, -1f, "effect", "Friendly Base");
            TestAssert.That(lost.Blocker.Contains("Contact") && lost.ContactAgeSeconds == -1f,
                "Lost host contact exposes an explicit blocker and unknown age");
            assessment.Cancel(2f);
            ObjectiveTasking cancelled = Project(assessment, false);
            TestAssert.That(cancelled.CurrentPhase == -1 && cancelled.Lifecycle == ObjectiveLifecycle.Cancelled &&
                Array.TrueForAll(cancelled.Phases, p => p.Status != ObjectivePhaseStatus.Current),
                "Closed failed tasks have no current execution gate");

            foreach (OperationKind kind in Enum.GetValues(typeof(OperationKind)))
            {
                ObjectiveTasking task = Project(Active(kind), false);
                TestAssert.That(task.Family != ObjectiveFamily.Unknown && task.Phases.Length > 0 && task.Phases.Length <= 3 &&
                    task.CurrentPhase >= 0 && !string.IsNullOrWhiteSpace(task.NextAction),
                    "Every existing family has bounded real phases and an instruction: " + kind);
            }
            var cards = new[] { Card(1, 100f), Card(2, 100000f) };
            var selected = new ContractCard[1];
            var distances = new float[1];
            TestAssert.That(ContractSelection.Select(cards, 2, 0f, 0f, selected, distances, 1, 2) == 1 && selected[0].Id == 2,
                "Explicit local HUD selection wins over a closer contract and remains visible beyond vicinity");
            TestAssert.That(ContractSelection.Select(cards, 2, 0f, 0f, selected, distances, 0, 2) == 0,
                "An empty HUD budget cannot index a negative slot");
            TestAssert.That(ContractSelection.Select(cards, 2, 0f, 0f, selected, distances, 1, -1) == 0,
                "An explicit HUD deselection never falls back to a nearby task");
            TestAssert.That(OperationRequestPolicy.Allows(true, 0, 2, .1f),
                "A real action may supersede a pending read refresh with a new correlation token");
            TestAssert.That(!OperationRequestPolicy.Allows(true, 2, 3, 10f) &&
                !OperationRequestPolicy.Allows(true, 2, 0, 10f),
                "Pending actions block both repeated actions and read queries until acknowledged");
            TestAssert.That(!OperationRequestPolicy.Allows(false, 0, 0, 1f) &&
                OperationRequestPolicy.Allows(false, 0, 0, 2f) && OperationRequestPolicy.Allows(false, 0, 2, 0f),
                "Read queries remain bounded at two seconds while intentional actions can dispatch");
            TestAssert.That(OperationRequestPolicy.Correlated(false, true, 4, 4, 2, 2, 7, 7, true, true),
                "Only a matching current-host reply may resolve a pending action");
            TestAssert.That(!OperationRequestPolicy.Correlated(false, true, 4, 4, 2, 2, 7, 7, false, true) &&
                !OperationRequestPolicy.Correlated(false, true, 4, 4, 2, 2, 7, 7, true, false),
                "A ghost or prior connection and a changed faction cannot correlate an action reply");
            TestAssert.That(!OperationRequestPolicy.Correlated(false, true, 3, 4, 2, 2, 7, 7, true, true) &&
                !OperationRequestPolicy.Correlated(false, true, 4, 4, 1, 2, 7, 7, true, true) &&
                !OperationRequestPolicy.Correlated(false, true, 4, 4, 2, 2, 6, 7, true, true) &&
                !OperationRequestPolicy.Correlated(false, false, 4, 4, 2, 2, 7, 7, true, true),
                "Old protocol, scene, refresh token, or unsolicited replies cannot acknowledge the current action");
            TestAssert.That(OperationRequestPolicy.SelectAccepted(true, false, 2, 2) &&
                !OperationRequestPolicy.SelectAccepted(true, true, 2, 2) &&
                !OperationRequestPolicy.SelectAccepted(true, false, 2, 3) &&
                !OperationRequestPolicy.SelectAccepted(false, false, 2, 2),
                "Accepted-task acknowledgment can select its HUD, but denied aborts and unrelated snapshots preserve local deselection");
        }

        private static Operation Active(OperationKind kind)
        {
            var op = new Operation(1, 1, kind, OperationReward.None, 0f, 100, 20);
            op.Accept(0f, "Pilot", 1);
            return op;
        }

        private static ObjectiveTasking Project(Operation op, bool station, bool neutralized = false) =>
            OperationPresentation.Build(op, station, neutralized, ObjectiveAsset.GroundVehicle,
                ObjectiveContact.Known, 0f, "Existing effect", "Friendly Base");

        private static ContractCard Card(int id, float x)
        {
            ContractCard.TryRead(new SecondaryObjectiveView(id, "COVER THE SUPPLY RUN", "", "", "ACTIVE", "", 0f,
                300f, 100, 20, false, false, true, true, x, 0f, 1500f), out ContractCard card);
            return card;
        }
    }
}
