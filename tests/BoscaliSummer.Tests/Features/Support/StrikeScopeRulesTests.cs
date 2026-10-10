using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Support.Domain.Calls;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>What the STRIKE page of the TAC BOARD asks of Support: which actions are strikes, which the page keeps, the perk strip's pick, the trace a snapshot has reached.</summary>
    internal static class StrikeScopeRulesTests
    {
        public static void Run()
        {
            Kinds();
            Keeping();
            Perks();
            Cyber();
        }

        private static void Kinds()
        {
            TestAssert.That(StrikeScopeRules.TryKind(SupportActionId.Artillery, out StrikeKind rod) && rod == StrikeKind.Rod, "The artillery action IS the kinetic rod");
            TestAssert.That(StrikeScopeRules.TryKind(SupportActionId.Prsm, out StrikeKind prsm) && prsm == StrikeKind.Prsm, "PRSM");
            TestAssert.That(StrikeScopeRules.TryKind(SupportActionId.Cruise, out StrikeKind cruise) && cruise == StrikeKind.Cruise, "Cruise");
            TestAssert.That(StrikeScopeRules.TryKind(SupportActionId.Emp, out StrikeKind emp) && emp == StrikeKind.Emp, "EMP");
            TestAssert.That(StrikeScopeRules.TryKind(SupportActionId.FlareMissile, out StrikeKind decoy) && decoy == StrikeKind.Decoy, "The decoy barrage is the flare missile");
            TestAssert.That(!StrikeScopeRules.TryKind(SupportActionId.Recon, out _) && !StrikeScopeRules.TryKind(SupportActionId.JtacMark, out _) && !StrikeScopeRules.TryKind(SupportActionId.SamNetDown, out _) && !StrikeScopeRules.TryKind(SupportActionId.Fortify, out _), "Recon, JTAC, SAM NET and FORTIFY are not strikes");
            TestAssert.Near(StrikeScopeRules.Linger(StrikeKind.Emp, 10f, 40f), 30f, "An EMP lingers for its effect");
            TestAssert.Near(StrikeScopeRules.Linger(StrikeKind.Decoy, 10f, 40f), 30f, "...and so does the decoy barrage");
            TestAssert.Near(StrikeScopeRules.Linger(StrikeKind.Prsm, 10f, 40f), 0f, "A missile is done at its impact whatever the registry keeps it for");
            TestAssert.Near(StrikeScopeRules.Linger(StrikeKind.Emp, 10f, 5f), 0f, "A backwards expiry is no effect");
        }

        private static void Keeping()
        {
            TestAssert.That(StrikeScopeRules.Keep(StrikeKind.Rod, 8f, 0f), "In the air");
            TestAssert.That(StrikeScopeRules.Keep(StrikeKind.Rod, -5.9f, 0f), "Just landed");
            TestAssert.That(!StrikeScopeRules.Keep(StrikeKind.Rod, -6.1f, 0f), "Landed a while ago: off the page");
            TestAssert.That(StrikeScopeRules.Keep(StrikeKind.Emp, -31f, 30f), "An EMP's effect runs on, with a margin");
            TestAssert.That(!StrikeScopeRules.Keep(StrikeKind.Emp, -33f, 30f), "...then it is gone");

            int[] into = new int[3];
            int[] ids = { 4, 1, 7, 3, 9 };
            bool[] flying = { true, false, true, false, false };
            int n = StrikeScopeRules.Pick(5, ids, flying, into);
            TestAssert.Eq(n, 3, "The page shows three");
            TestAssert.That(ids[into[0]] == 4 && ids[into[1]] == 7 && ids[into[2]] == 9, "Those in the air first, then the newest landed one; shown in the order they were called");
            n = StrikeScopeRules.Pick(2, ids, flying, into);
            TestAssert.That(n == 2 && ids[into[0]] == 1 && ids[into[1]] == 4, "Fewer than three show as they are, in call order: id " + ids[into[0]] + ", " + ids[into[1]]);
            TestAssert.Eq(StrikeScopeRules.Pick(0, ids, flying, into), 0, "No strike, no track");
            bool[] all = { true, true, true, true, true };
            n = StrikeScopeRules.Pick(5, ids, all, into);
            TestAssert.That(n == 3 && ids[into[0]] == 4 && ids[into[1]] == 7 && ids[into[2]] == 9, "Five in the air: the three newest");
        }

        private static void Perks()
        {
            var state = new[] { PerkPhase.Ready, PerkPhase.Cooling, PerkPhase.Armed, PerkPhase.Cooling, PerkPhase.Ready, PerkPhase.Cooling };
            var cool = new[] { 0f, 120f, 0f, 30f, 0f, 300f };
            int[] into = new int[3];
            int n = StrikeScopeRules.PickPerks(6, state, cool, into);
            TestAssert.That(n == 3 && into[0] == 2 && into[1] == 3 && into[2] == 1, "Armed first, then the soonest to cool: " + into[0] + "," + into[1] + "," + into[2]);
            n = StrikeScopeRules.PickPerks(2, new[] { PerkPhase.Ready, PerkPhase.Ready }, new[] { 0f, 0f }, into);
            TestAssert.That(n == 2 && into[0] == 0 && into[1] == 1, "Ready perks in catalogue order when nothing else is up");
            TestAssert.Eq(StrikeScopeRules.PickPerks(0, state, cool, into), 0, "No perks, no strip");
            TestAssert.That(StrikeScopeRules.PerkName("ORBITAL ROD") == "ROD" && StrikeScopeRules.PerkName("SAM NET DOWN") == "SAM NET" && StrikeScopeRules.PerkName("JTAC LASE") == "JTAC LASE" && StrikeScopeRules.PerkName("EMP") == "EMP" && StrikeScopeRules.PerkName("PRSM") == "PRSM", "Perk names fit the strip");
            foreach (CallRow row in CallSheet.Rows) TestAssert.That(StrikeScopeRules.PerkName(row.Label).Length <= 9, "Every catalogue perk has a name of nine letters or less: " + row.Label);
            TestAssert.That(StrikeScopeRules.Front("SPACE") == PerkFront.Space && StrikeScopeRules.Front("CYBER") == PerkFront.Cyber && StrikeScopeRules.Front("SOF") == PerkFront.Sof, "Fronts");
            foreach (CallRow row in CallSheet.Rows) TestAssert.That(StrikeScopeRules.Front(row.FrontWord) == (row.Front == BoscaliSummer.Modules.Support.Domain.Fronts.Front.Space ? PerkFront.Space : row.Front == BoscaliSummer.Modules.Support.Domain.Fronts.Front.Cyber ? PerkFront.Cyber : PerkFront.Sof), "The front word of " + row.Label + " maps to its front");
        }

        private static void Cyber()
        {
            TestAssert.Near(StrikeScopeRules.TraceRate(true, 0), 1.5f, "The first hop burns 1.5 percent a second");
            TestAssert.Near(StrikeScopeRules.TraceRate(true, 2), 2.5f, "...a held node adds half a percent each");
            TestAssert.Near(StrikeScopeRules.TraceRate(false, 3), 1.5f, "Holding three nodes burns 1.5");
            TestAssert.Near(StrikeScopeRules.TraceRate(false, 0), 0f, "Nothing held, nothing hopping: nothing burns");
            TestAssert.Near(StrikeScopeRules.TraceNow(40f, 2f, 1f), 42f, "The trace runs on from the snapshot");
            TestAssert.Near(StrikeScopeRules.TraceNow(40f, 2f, 10f), 45f, "...but only as far as the next snapshot is due");
            TestAssert.Near(StrikeScopeRules.TraceNow(99f, 3f, 2f), 100f, "...and never past the end");
            TestAssert.Near(StrikeScopeRules.TraceNow(40f, 2f, -3f), 40f, "A snapshot from the future does not run backwards");
            for (int k = 0; k < 5; k++) TestAssert.Eq((int)StrikeScopeRules.Chain((NodeKind)k), k, "The chain's node kinds match the wire's: " + k);
            TestAssert.That(StrikeScopeRules.OnMission(TeamState.OnSite, MissionKind.Recon) && StrikeScopeRules.OnMission(TeamState.Moving, MissionKind.Sabotage) && StrikeScopeRules.OnMission(TeamState.Returning, MissionKind.Lase), "A team on its way, on site or coming home is on a mission");
            TestAssert.That(!StrikeScopeRules.OnMission(TeamState.Ready, MissionKind.Recon) && !StrikeScopeRules.OnMission(TeamState.OnSite, MissionKind.None) && !StrikeScopeRules.OnMission(TeamState.Lost, MissionKind.Recon), "A waiting, idle or lost team is not");
            foreach (MissionKind m in new[] { MissionKind.Recon, MissionKind.Lase, MissionKind.Sabotage, MissionKind.Seize, MissionKind.Tap })
                TestAssert.That(StrikeScopeRules.MissionWord(m).Length >= 3 && StrikeScopeRules.MissionWord(m).Length <= 7, "A mission word fits a gauge's seven letters: " + m);
        }
    }
}
