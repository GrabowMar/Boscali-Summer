using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Runtime;

namespace BoscaliSummer.Tests.Features.Support
{
    internal static class OpsDomainTests
    {
        public static void Run()
        {
            TestTabs();
            TestFormatting();
            TestPostures();
            TestInfoGates();
            TestProgramInvestment();
            TestProgramAccrual();
            TestProgramMirror();
            TestGarrisonDoctrine();
        }

        private static void TestTabs()
        {
            string[] labels = OpsDomains.TabLabels();
            TestAssert.That(labels.Length == 5, "OPS must expose exactly five domain tabs");
            string[] expected = { "SPACE", "EW", "INFO", "SPEC OPS", "INTEL" };
            for (int i = 0; i < expected.Length; i++)
            {
                TestAssert.That(labels[i] == expected[i], "OPS tab " + i + " must read " + expected[i]);
                TestAssert.That(labels[i].Length <= 8, "an OPS tab label must fit a fifth of the bezel");
                TestAssert.That(!string.IsNullOrEmpty(OpsDomains.Mission(OpsDomains.All[i])),
                    "every OPS domain needs a mission line");
            }
        }

        private static void TestFormatting()
        {
            TestAssert.That(TheaterGrid.Kilometres(12400.0, -3100.0) == "12.4 / -3.1 KM", "grid must read in kilometres");
            TestAssert.That(TheaterGrid.Kilometres(double.NaN, 0.0).StartsWith("—"), "a non-finite grid must not print NaN");
            TestAssert.That(TheaterGrid.Clock(125.4) == "02:05" && TheaterGrid.Clock(3725.0) == "1:02:05",
                "countdowns must read MM:SS, then H:MM:SS");
            TestAssert.That(TheaterGrid.Elapsed(393856.0) == "109:24:16", "GET must read HHH:MM:SS like the Apollo wall clock");
            TestAssert.That(TheaterGrid.Clock(-1.0) == "--:--", "a negative countdown must not print");
        }

        private static void TestPostures()
        {
            TestAssert.That(EwPostures.Default == EwPosture.NoiseJamming, "a fresh EW station comes up jamming");
            TestAssert.That(EwPostures.Clamp(200) == EwPostures.Default, "a hostile posture byte must clamp");
            TestAssert.That(EwPostures.Clamp(2) == EwPosture.GhostSpoofing, "a valid posture byte must survive");

            TestAssert.That(!EwPostures.StationBacked(HackKind.Ping) && !EwPostures.StationBacked(HackKind.Track),
                "signals operations must not need a station");
            TestAssert.That(EwPostures.StationBacked(HackKind.Blackout) && EwPostures.StationBacked(HackKind.Ghost) &&
                EwPostures.StationBacked(HackKind.Spoof), "attack operations must reach through a station");

            TestAssert.That(EwPostures.Backs(EwPosture.NoiseJamming, HackKind.Blackout), "jamming backs blackout");
            TestAssert.That(!EwPostures.Backs(EwPosture.GhostSpoofing, HackKind.Blackout), "spoofing must not back blackout");
            TestAssert.That(EwPostures.Backs(EwPosture.GhostSpoofing, HackKind.Ghost) &&
                EwPostures.Backs(EwPosture.GhostSpoofing, HackKind.Spoof), "spoofing backs both deception operations");
            TestAssert.That(!EwPostures.Backs(EwPosture.SigintPassive, HackKind.Blackout) &&
                !EwPostures.Backs(EwPosture.SigintPassive, HackKind.Spoof), "a passive station backs no attack");
            TestAssert.That(EwPostures.Backs(EwPosture.SigintPassive, HackKind.Ping),
                "posture must never gate an operation that needs no station");

            for (int i = 0; i < EwPostures.All.Length; i++)
                TestAssert.That((int)EwPostures.All[i].Posture == i, "posture table must be indexed by its byte");
        }

        private static void TestInfoGates()
        {
            var network = new InfoNetwork();
            TestAssert.That(InfoOperations.Evaluate(network, HackKind.Ping, false, EwPosture.SigintPassive) ==
                InfoGate.FacilityMissing, "an unbuilt facility must gate first");
            TestAssert.That(InfoOperations.Evaluate(null, HackKind.Ping, true, EwPosture.NoiseJamming) ==
                InfoGate.FacilityMissing, "a missing network must read as unbuilt, not ready");

            network.TryUpgrade(FacilityId.Sigint);
            TestAssert.That(InfoOperations.Evaluate(network, HackKind.Ping, false, EwPosture.SigintPassive) ==
                InfoGate.Ready, "PING needs no station");

            network.TryUpgrade(FacilityId.Crypto);
            network.TryUpgrade(FacilityId.Disrupt);
            TestAssert.That(InfoOperations.Evaluate(network, HackKind.Blackout, false, EwPosture.NoiseJamming) ==
                InfoGate.StationMissing, "BLACKOUT needs a deployed station");
            TestAssert.That(InfoOperations.Evaluate(network, HackKind.Blackout, true, EwPosture.GhostSpoofing) ==
                InfoGate.WrongPosture, "BLACKOUT needs a jamming posture");
            TestAssert.That(InfoOperations.Evaluate(network, HackKind.Blackout, true, EwPosture.NoiseJamming) ==
                InfoGate.Ready, "a jamming station backs BLACKOUT");

            TestAssert.That(InfoOperations.Explain(InfoGate.Ready, HackKind.Ping) == null, "an open gate has no copy");
            TestAssert.That(InfoOperations.Explain(InfoGate.WrongPosture, HackKind.Spoof).Contains("GHOST SPOOFING"),
                "a posture gate must name the posture to set");
            TestAssert.That(InfoOperations.Explain(InfoGate.FacilityMissing, HackKind.Track).Contains("LV2"),
                "a facility gate must name the level to build");
        }

        private static void TestProgramInvestment()
        {
            TestAssert.That(OpsProgramLedger.ProgramCount == 6, "three SOF and three intel programs");
            for (int i = 0; i < OpsProgramLedger.Programs.Length; i++)
            {
                OpsProgramInfo info = OpsProgramLedger.Programs[i];
                TestAssert.That((int)info.Id == i, "program table must be indexed by its wire byte");
                TestAssert.That(info.Costs.Length == OpsProgramLedger.MaxTier + 1 &&
                    info.YieldPerMinute.Length == OpsProgramLedger.MaxTier + 1 &&
                    info.Tiers.Length == OpsProgramLedger.MaxTier, info.Name + " tier tables must match MaxTier");
                TestAssert.That(info.Costs[0] == 0f && info.YieldPerMinute[0] == 0f,
                    info.Name + " tier zero must be free and idle");
                for (int t = 1; t <= OpsProgramLedger.MaxTier; t++)
                    TestAssert.That(info.Costs[t] > info.Costs[t - 1] && info.YieldPerMinute[t] > info.YieldPerMinute[t - 1],
                        info.Name + " tiers must cost and yield more as they climb");
            }

            var ledger = new OpsProgramLedger();
            TestAssert.That(ledger.NextCost(OpsProgramId.Pathfinders) == 400f, "tier one priced from the table");
            TestAssert.That(ledger.YieldPerMinute(OpsReserve.SpecOps) == 0f, "an unfunded reserve yields nothing");
            TestAssert.That(ledger.SecondsToNextToken(OpsReserve.SpecOps) < 0f, "an idle reserve has no ETA");

            for (int t = 0; t < OpsProgramLedger.MaxTier; t++)
                TestAssert.That(ledger.TryInvest(OpsProgramId.Pathfinders), "tier " + (t + 1) + " must apply");
            TestAssert.That(!ledger.CanInvest(OpsProgramId.Pathfinders) && !ledger.TryInvest(OpsProgramId.Pathfinders),
                "a maxed program must refuse investment");
            TestAssert.That(ledger.NextCost(OpsProgramId.Pathfinders) == 0f, "a maxed program has no next cost");
            TestAssert.That(!ledger.CanInvest((OpsProgramId)99), "an unknown program byte must be refused");
            TestAssert.That(ledger.FundedTiers(OpsReserve.SpecOps) == 3 && ledger.FundedTiers(OpsReserve.Intel) == 0,
                "funding must stay inside its own reserve");
        }

        private static void TestProgramAccrual()
        {
            var ledger = new OpsProgramLedger();
            ledger.TryInvest(OpsProgramId.HumintNetwork); // 0.15 tokens per minute

            ledger.Tick(float.NaN);
            ledger.Tick(-1f);
            TestAssert.That(ledger.Progress(OpsReserve.Intel) == 0f, "invalid ticks must not accrue");

            ledger.Tick(1000f);
            TestAssert.That(ledger.Tokens(OpsReserve.Intel) == 0 &&
                ledger.Progress(OpsReserve.Intel) <= 0.15f / 60f * OpsProgramLedger.MaximumTickSeconds + 0.0001f,
                "one stalled frame must not pay out a burst");

            for (int i = 0; i < 400 * 12; i++) ledger.Tick(0.1f); // eight minutes at 10 Hz
            TestAssert.That(ledger.Tokens(OpsReserve.Intel) == 1, "0.15/min over eight minutes accrues one token");
            TestAssert.That(ledger.Tokens(OpsReserve.SpecOps) == 0, "intel funding must not fill SOF readiness");
            TestAssert.That(ledger.SecondsToNextToken(OpsReserve.Intel) > 0f, "a funded reserve reports its ETA");

            TestAssert.That(!ledger.TryConsume(OpsReserve.Intel, 2), "an overdraw must not spend");
            TestAssert.That(ledger.Tokens(OpsReserve.Intel) == 1, "a refused spend must be all-or-nothing");
            TestAssert.That(!ledger.TryConsume(OpsReserve.Intel, 0), "a zero spend must be refused");
            TestAssert.That(ledger.TryConsume(OpsReserve.Intel, 1) && ledger.Tokens(OpsReserve.Intel) == 0,
                "a covered spend must debit the reserve");

            var full = new OpsProgramLedger();
            for (int t = 0; t < OpsProgramLedger.MaxTier; t++) full.TryInvest(OpsProgramId.AssetRecruitment);
            for (int i = 0; i < 2000; i++) full.Tick(OpsProgramLedger.MaximumTickSeconds);
            TestAssert.That(full.Tokens(OpsReserve.Intel) == OpsProgramLedger.ReserveCap, "a reserve must stop at its cap");
            TestAssert.That(full.Full(OpsReserve.Intel) && full.Progress(OpsReserve.Intel) == 0f &&
                full.SecondsToNextToken(OpsReserve.Intel) < 0f, "a full reserve must not bank hidden progress");

            full.Clear();
            TestAssert.That(full.Tier(OpsProgramId.AssetRecruitment) == 0 && full.Tokens(OpsReserve.Intel) == 0,
                "scene teardown must clear tiers and tokens");
        }

        private static void TestProgramMirror()
        {
            var host = new OpsProgramLedger();
            host.TryInvest(OpsProgramId.SabotageCells);
            host.TryInvest(OpsProgramId.DecryptionArray);
            host.TryInvest(OpsProgramId.DecryptionArray);
            for (int i = 0; i < 100; i++) host.Tick(2f);

            var tiers = new byte[OpsProgramLedger.ProgramCount];
            for (int i = 0; i < tiers.Length; i++) tiers[i] = (byte)host.Tier((OpsProgramId)i);

            var client = new OpsProgramLedger();
            client.Mirror(tiers, (byte)host.Tokens(OpsReserve.SpecOps), (byte)host.Tokens(OpsReserve.Intel),
                host.ProgressByte(OpsReserve.SpecOps), host.ProgressByte(OpsReserve.Intel));
            for (int i = 0; i < tiers.Length; i++)
                TestAssert.That(client.Tier((OpsProgramId)i) == host.Tier((OpsProgramId)i), "mirrored tiers must match");
            TestAssert.That(client.Tokens(OpsReserve.Intel) == host.Tokens(OpsReserve.Intel) &&
                client.Tokens(OpsReserve.SpecOps) == host.Tokens(OpsReserve.SpecOps), "mirrored tokens must match");
            TestAssert.That(System.Math.Abs(client.Progress(OpsReserve.Intel) - host.Progress(OpsReserve.Intel)) < 0.01f,
                "mirrored progress must survive byte quantisation");

            client.Mirror(new byte[] { 250, 9, 3 }, 200, 255, 255, 255);
            TestAssert.That(client.Tier(OpsProgramId.Pathfinders) == OpsProgramLedger.MaxTier &&
                client.Tier(OpsProgramId.AssetRecruitment) == 0, "hostile or short tier arrays must clamp");
            TestAssert.That(client.Tokens(OpsReserve.SpecOps) == OpsProgramLedger.ReserveCap &&
                client.Progress(OpsReserve.SpecOps) == 0f, "hostile token bytes must clamp to the cap");
            client.Mirror(null, 0, 0, 0, 0);
            TestAssert.That(client.Tier(OpsProgramId.SabotageCells) == 0, "a null tier array must clear, not throw");
        }

        private static void TestGarrisonDoctrine()
        {
            TestAssert.That(OpsGarrison.UpgradeCount == 2, "fortification and insertion doctrine");
            for (int i = 0; i < OpsGarrison.Upgrades.Length; i++)
            {
                GarrisonUpgradeInfo info = OpsGarrison.Upgrades[i];
                TestAssert.That((int)info.Id == i, "the doctrine table must be indexed by its wire byte");
                TestAssert.That(info.Costs.Length == OpsGarrison.MaxRank + 1 && info.Ranks.Length == OpsGarrison.MaxRank,
                    info.Name + " rank tables must match MaxRank");
                TestAssert.That(info.Costs[0] == 0, info.Name + " rank zero must be free");
                for (int rank = 1; rank <= OpsGarrison.MaxRank; rank++)
                {
                    TestAssert.That(info.Costs[rank] > info.Costs[rank - 1], info.Name + " ranks must cost more as they climb");
                    TestAssert.That(!string.IsNullOrEmpty(info.Ranks[rank - 1]), info.Name + " rank " + rank + " needs copy");
                }
            }

            TestAssert.That(OpsGarrison.MaxRank == 3, "the rank word is written over a three-rank track");
            TestAssert.That(OpsGarrison.RankLabel(0) == "UNTRAINED" && OpsGarrison.RankLabel(1) == "RANK I/III" &&
                OpsGarrison.RankLabel(3) == "RANK III/III" && OpsGarrison.RankLabel(9) == "RANK III/III",
                "rank words must clamp to the track");
            TestAssert.That(OpsGarrison.EffectLabel(GarrisonUpgradeId.FortificationDoctrine, 0) == "1 POSITION/ORDER" &&
                OpsGarrison.EffectLabel(GarrisonUpgradeId.FortificationDoctrine, OpsGarrison.MaxRank) == "4 POSITIONS/ORDER" &&
                OpsGarrison.EffectLabel(GarrisonUpgradeId.InsertionRigging, 2) == "3 CAMPS/INSERTION",
                "short effect words must track the rank");

            var garrison = new OpsGarrison();
            TestAssert.That(garrison.FortificationShells == 1 && garrison.InsertionCamps == 1,
                "an untrained garrison does everything once");
            TestAssert.That(garrison.NextCost(GarrisonUpgradeId.FortificationDoctrine) == 2, "rank one priced from the table");
            TestAssert.That(garrison.Rank(GarrisonUpgradeId.FortificationDoctrine) == 0 &&
                garrison.Rank((GarrisonUpgradeId)99) == 0, "unknown doctrine bytes read as untrained");

            for (int rank = 0; rank < OpsGarrison.MaxRank; rank++)
                TestAssert.That(garrison.TryUpgrade(GarrisonUpgradeId.FortificationDoctrine),
                    "rank " + (rank + 1) + " must apply");
            TestAssert.That(!garrison.CanUpgrade(GarrisonUpgradeId.FortificationDoctrine) &&
                !garrison.TryUpgrade(GarrisonUpgradeId.FortificationDoctrine), "a maxed track must refuse investment");
            TestAssert.That(garrison.NextCost(GarrisonUpgradeId.FortificationDoctrine) == 0, "a maxed track has no next cost");
            TestAssert.That(garrison.FortificationShells == 1 + OpsGarrison.MaxRank &&
                garrison.InsertionCamps == 1, "doctrine effects must stay inside their own track");

            // Mirror is the client path; a hostile byte clamps to the ceiling and a short
            // array clears rather than throwing.
            var client = new OpsGarrison();
            client.Mirror(new byte[] { 250, 2 });
            TestAssert.That(client.Rank(GarrisonUpgradeId.FortificationDoctrine) == OpsGarrison.MaxRank &&
                client.Rank(GarrisonUpgradeId.InsertionRigging) == 2, "hostile doctrine bytes must clamp");
            TestAssert.That(client.FortificationShells == 1 + OpsGarrison.MaxRank &&
                client.InsertionCamps == 3, "mirrored effects must match the mirrored ranks");
            client.Mirror(null);
            TestAssert.That(client.Rank(GarrisonUpgradeId.FortificationDoctrine) == 0 &&
                client.Rank(GarrisonUpgradeId.InsertionRigging) == 0, "a null mirror must clear, not throw");

            client.Mirror(new byte[] { 1, 1 });
            client.Clear();
            TestAssert.That(client.FortificationShells == 1 && client.InsertionCamps == 1,
                "scene teardown must clear the base of operations");
        }
    }
}
