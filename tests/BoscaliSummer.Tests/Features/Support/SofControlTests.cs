using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>OPS-06 / OPS-07: an operator's order reserves a SOF team against OVERLORD and other operators until it is home; a faction with no camp never reports CAMPS MAX.</summary>
    internal static class SofControlTests
    {
        private const ulong A = 7, B = 8, Overlord = SpaceContacts.WatchOfficerId;

        private sealed class Ports : ISofPorts
        {
            public float Now { get; set; } = 100f;
            public int Humans => 2;
            public int Owner => 1;
            public bool Fob => false;
            public SofOutcome TrySpend(ulong op, int cr, out int detail) { detail = 0; return SofOutcome.None; }
            public void Refund(ulong op, int cr) { }
            public SofScene Scene(float x, float z) => default;
            public bool CyberNear(float x, float z) => false;
            public bool Roll(int chancePercent) => true;
            public void Reveal(float x, float z, float radius, float seconds) { }
            public bool LaseBegin(int slot, uint key, float x, float z) => true;
            public void LaseEnd(int slot, uint key) { }
            public bool TargetAlive(TargetKind kind, AnchorSub sub, uint key) => true;
            public bool Sabotage(AnchorSub sub, uint key) => true;
            public bool Tap(TargetKind kind, uint key, float seconds) => true;
            public bool BuildingAlive(uint key) => true;
            public bool PostCover(int slot, float x, float z) => true;
            public bool PostLase(int slot, float x, float z) => true;
            public void Pay(ulong pilot, int cr) { }
        }

        public static void Run()
        {
            var p = new Ports();
            var camps = new SofCampSet(1);
            camps.SetPosition(0, 0f, 0f); camps.Set(0, 1f, false, 0f);
            var d = new SofDesk(p, camps);
            int slot = d.Raise(Overlord, 4).Slot; // a TRAIN TEAM team: OVERLORD's pool
            for (int i = 0; i < 100; i++) { p.Now += 1f; d.Tick(); }
            TestAssert.Eq(d.Teams[slot].State, TeamState.Ready, "the pool team is deployed");

            TestAssert.Eq(d.Divert(A, slot, 500f, 0f).Outcome, SofOutcome.Diverted, "an operator takes a pool team with an order");
            TestAssert.Eq(d.Teams[slot].Raiser, A, "and it answers to that operator");
            TestAssert.Eq(d.Order(B, slot, TeamVerb.Hold).Outcome, SofOutcome.Controlled, "a second operator cannot take it");
            TestAssert.Eq(d.Divert(B, slot, 0f, 500f).Outcome, SofOutcome.Controlled, "nor divert it");
            TestAssert.Eq(d.Order(Overlord, slot, TeamVerb.Exfil).Outcome, SofOutcome.Controlled, "OVERLORD cannot recall it");
            TestAssert.Eq(d.Teams[slot].Raiser, A, "a refusal changes nothing");
            for (int i = 0; i < 10; i++) { p.Now += 1f; d.Tick(); }
            TestAssert.Eq(d.Order(A, slot, TeamVerb.Exfil).Outcome, SofOutcome.Ordered, "the owner keeps full control");
            for (int i = 0; i < 80; i++) { p.Now += 1f; d.Tick(); }
            TestAssert.Eq(d.Teams[slot].State, TeamState.Ready, "home again");
            TestAssert.Eq(d.Teams[slot].Raiser, Overlord, "back home it returns to the pool");
            TestAssert.Eq(d.Order(B, slot, TeamVerb.Cancel).Outcome, SofOutcome.Ordered, "and another operator may take it");

            var book = new FrontBook();
            var none = new FrontWorld();
            TestAssert.That(book.RefusalCode(Front.Sof, ProgrammeId.Camp, none) != FrontOutcome.CampsMax, "no camp was ever placed is not CAMPS MAX");
            TestAssert.That(book.Refusal(Front.Sof, ProgrammeId.Camp, none).Length > 0 && !book.Queue(Front.Sof, ProgrammeId.Camp, none, 0f, out _), "and the CAMP programme is refused with words");
            TestAssert.Eq(book.RefusalCode(Front.Sof, ProgrammeId.Camp, new FrontWorld { Camps = 2, CampMax = 2 }), FrontOutcome.CampsMax, "real camps at max still say so");
        }
    }
}
