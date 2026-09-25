using BoscaliSummer.Features.Support.Domain.Cyber;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>
    /// The pure CYBER model: locations and stages, resources and upgrades, coverage, the breach
    /// minigame (phases, trace, spoof, backtrace, loot, capstones), the console verbs, the
    /// adversary campaign and the snapshot mirror. No Unity, no game.
    /// </summary>
    internal static class CyberNetworkTests
    {
        public static void Run()
        {
            TestCatalogue();
            TestStagesAndIncome();
            TestUpgradesAndReach();
            TestBaseReach();
            TestBreachPhasesAndLoot();
            TestSpoofAndBacktrace();
            TestCapstone();
            TestCapstoneMirrorAndRelisting();
            TestForceProbePayment();
            TestVerbs();
            TestCampaign();
            TestSnapshot();
            TestWords();
        }

        private static void Tick(CyberNetwork network, double seconds, double from = 0.0)
        {
            double now = from;
            double end = from + seconds;
            while (now < end)
            {
                network.Tick(now, 0.25f, 0f);
                now += 0.25;
            }
            network.Tick(end, 0.25f, 0f);
        }

        private static CyberNetwork Fresh(out int command, out int city)
        {
            var network = new CyberNetwork();
            command = network.PlaceStatic(1, NodeKind.Command, 0f, 0f);
            network.BeginLocations();
            city = network.ReportLocation(100, LocationKind.City, 8000f, 0f, 0.0);
            network.EndLocations(0.0);
            return network;
        }

        private static void TestCatalogue()
        {
            TestAssert.That(CyberCatalog.Table.Length == 8, "eight map abilities");
            for (int i = 0; i < CyberCatalog.Table.Length; i++)
                TestAssert.That((int)CyberCatalog.Table[i].Kind == i, "the ability table is indexed by its wire byte");
            TestAssert.That(CyberCatalog.RequiredStage(HackKind.Ping) == 2 &&
                CyberCatalog.RequiredStage(HackKind.Track) == 3 &&
                CyberCatalog.RequiredStage(HackKind.Blackout) == 3 &&
                CyberCatalog.RequiredStage(HackKind.Ghost) == 3 &&
                CyberCatalog.RequiredStage(HackKind.Spoof) == 3 &&
                CyberCatalog.RequiredStage(HackKind.Scan) == 2 &&
                CyberCatalog.RequiredStage(HackKind.Hijack) == 3 &&
                CyberCatalog.RequiredStage(HackKind.Overload) == 3,
                "a stage-2 location unlocks PING, a stage-3 one the rest");
            TestAssert.That(Capstones.All.Length == 3 && Capstones.Intel > 0f && Capstones.RechargeSeconds > 0f,
                "three capstones, paid in intel and recharged");
            TestAssert.That(CyberLocations.Stage(LocationKind.City, 0).Computing == 0f &&
                CyberLocations.Stage(LocationKind.City, 1).Computing > 0f,
                "stage zero is nothing");
            for (int s = 2; s <= CyberLocations.StageCount; s++)
            {
                TestAssert.That(CyberLocations.Stage(LocationKind.City, s).Computing >
                    CyberLocations.Stage(LocationKind.City, s - 1).Computing, "computing rises with the stage");
                TestAssert.That(CyberLocations.Stage(LocationKind.City, s).Radius >
                    CyberLocations.Stage(LocationKind.City, s - 1).Radius, "radius rises with the stage");
            }
            TestAssert.That(CyberLocations.Stage(LocationKind.City, 4).Intel > 0f, "a mastered city yields intel");
            TestAssert.That(CyberLocations.Tier(1) == 0 && CyberLocations.Tier(2) == 1 &&
                CyberLocations.Tier(3) == 2 && CyberLocations.Tier(4) == 3, "stage to ability tier");
            TestAssert.That(CyberLocations.CitySetName("city1_buildings") &&
                CyberLocations.CitySetName("airbase_city_buildings") &&
                CyberLocations.CitySetName("City3_Buildings"), "the map's city sets are locations");
            TestAssert.That(!CyberLocations.CitySetName("turbines") && !CyberLocations.CitySetName("pylons") &&
                !CyberLocations.CitySetName("solarpanels") && !CyberLocations.CitySetName("lighthouses") &&
                !CyberLocations.CitySetName("pipelines") && !CyberLocations.CitySetName(null),
                "the map's infrastructure sets are not cities");
        }

        private static void TestStagesAndIncome()
        {
            CyberNetwork network = Fresh(out _, out int city);
            TestAssert.That(network.HasCommand && network.CommandOnline, "the command comes up with the base");
            TestAssert.That(network.Exists(city) && !network.IsHacked(city), "a reported location exists unhacked");
            TestAssert.That(network.Stats().Nodes == 1 && network.Stats().Hacked == 0,
                "an unhacked location is a target, not yet a node");
            TestAssert.That(network.ComputingIncome() > 0f, "the command earns computing");
            TestAssert.That(network.IntelIncome() == 0f, "nothing earns intel before a stage-2 location");
            TestAssert.That(network.Computing == 0f, "a fresh network banks nothing");
            Tick(network, 30.0);
            TestAssert.That(network.Computing > 20f && network.Computing <= network.ComputingCapacity(),
                "computing accrues to the cap");
            TestAssert.That(network.Reach >= CyberLocations.DefaultReach, "reach defaults to the configured base");
            TestAssert.That(network.ReachCovers(8000f, 0f), "the city is inside reach");
            TestAssert.That(!network.ReachCovers(60000f, 0f), "a far point is outside reach");
            TestAssert.That(network.RadiusOf(city, 0.0) == 0f, "an unhacked location has no radius");
        }

        private static void TestUpgradesAndReach()
        {
            CyberNetwork network = Fresh(out _, out _);
            for (int u = 0; u < 4; u++)
            {
                var upgrade = (CyberUpgrade)u;
                TestAssert.That(network.CanUpgrade(upgrade) && network.UpgradeCost(upgrade) > 0f,
                    "a fresh network can buy every upgrade");
                float before = network.Reach;
                TestAssert.That(network.TryUpgrade(upgrade), "the upgrade applies");
                if (upgrade == CyberUpgrade.Reach) TestAssert.That(network.Reach > before, "reach grows with its upgrade");
            }
            TestAssert.That(network.UpgradeLevel(CyberUpgrade.Reach) == 1, "the level is recorded");
            TestAssert.That(network.TryUpgrade(CyberUpgrade.Reach) && network.TryUpgrade(CyberUpgrade.Reach),
                "three levels fit");
            TestAssert.That(!network.CanUpgrade(CyberUpgrade.Reach) && !network.TryUpgrade(CyberUpgrade.Reach),
                "the fourth level is refused");
        }

        /// <summary>CyberReachMeters lands in BaseReach: it decides which locations a breach may
        /// target, and the Reach upgrade scales whatever base the host set.</summary>
        private static void TestBaseReach()
        {
            CyberNetwork network = Fresh(out _, out int city);
            Tick(network, 30.0);
            TestAssert.That(network.BaseReach == CyberLocations.DefaultReach, "a network starts at the default base");
            TestAssert.That(network.CheckBreach(city, 30.0) == BreachDenial.None, "the city 8 km out is in default reach");

            network.BaseReach = 5000f;
            TestAssert.That(network.Reach == 5000f, "reach follows the base");
            TestAssert.That(network.CheckBreach(city, 30.0) == BreachDenial.OutOfReach,
                "a shorter base puts the city out of reach");
            TestAssert.That(network.TryUpgrade(CyberUpgrade.Reach) && network.TryUpgrade(CyberUpgrade.Reach) &&
                network.CheckBreach(city, 30.0) == BreachDenial.OutOfReach, "two levels on a 5 km base reach 7.5 km");
            TestAssert.That(network.TryUpgrade(CyberUpgrade.Reach) && network.Reach > 8000f &&
                network.CheckBreach(city, 30.0) == BreachDenial.None, "the third level brings the city back");

            network.BaseReach = 120000f;
            TestAssert.That(network.ReachCovers(150000f, 0f), "a long base reaches where the default never could");
        }

        /// <summary>Drive one breach to completion on the city, banking computing first.</summary>
        private static bool Breach(CyberNetwork network, int slot, bool quiet, double now, out double after)
        {
            after = now;
            while (network.Computing < 200f && after < now + 400.0)
            {
                network.Tick(after, 0.25f, 0f);
                after += 0.25;
            }
            BreachDenial denial = network.TryStartBreach(slot, quiet, after);
            if (denial != BreachDenial.None) return false;
            double guard = after + 120.0;
            while (network.BreachActive && after < guard)
            {
                network.Tick(after, 0.25f, 0f);
                after += 0.25;
            }
            return true;
        }

        private static void TestBreachPhasesAndLoot()
        {
            CyberNetwork network = Fresh(out _, out int city);
            Tick(network, 30.0);
            TestAssert.That(network.CheckBreach(city, 30.0) == BreachDenial.None, "a reachable location with computing accepts");
            TestAssert.That(network.TryStartBreach(city, true, 30.0) == BreachDenial.None, "the breach opens");
            TestAssert.That(network.BreachActive && network.BreachPhase == BreachPhase.Probe, "it opens on PROBE");
            TestAssert.That(network.CheckBreach(city, 30.0) == BreachDenial.Running, "a second breach is refused while one runs");
            TestAssert.That(network.Computing < 25f, "the probe phase is paid up front");

            double now = 30.0;
            float intelBefore = network.Intel;
            while (network.BreachActive && now < 200.0)
            {
                network.Tick(now, 0.25f, 0f);
                now += 0.25;
            }
            TestAssert.That(!network.BreachActive, "the quiet session completes");
            TestAssert.That(network.IsHacked(city) && network.Stage(city) == 1, "the city is taken at stage 1");
            TestAssert.That(network.Tier(city) == 0 && !network.AnyTier(1), "stage 1 unlocks no ability");
            TestAssert.That(network.Intel > intelBefore, "the stage pays intel loot");
            TestAssert.That(network.ComputingIncome() > 1f, "the taken city earns computing");
            TestAssert.That(network.RadiusOf(city, now) > 0f, "the taken city covers a radius");
            TestAssert.That(network.AbilityCovers(1, 8000f, 0f, now) == false, "stage 1 has no ability tier");

            // Stage 2 opens the basic tier and intel income.
            TestAssert.That(Breach(network, city, true, now, out now), "the second breach runs");
            TestAssert.That(network.Stage(city) == 2, "the city reaches stage 2");
            TestAssert.That(network.AnyTier(1) && network.Tier(city) == 1, "stage 2 opens the basic tier");
            TestAssert.That(network.AbilityCovers(1, 8000f, 0f, now), "the radius covers the city itself");
            TestAssert.That(!network.AbilityCovers(1, 8000f + network.RadiusOf(city, now) + 5000f, 0f, now),
                "coverage ends at the radius");
            TestAssert.That(network.IntelIncome() > 0f, "stage 2 earns intel");
            TestAssert.That(network.Stats().StageTotal == 2, "stage total counts every stage");
        }

        private static void TestSpoofAndBacktrace()
        {
            CyberNetwork network = Fresh(out _, out int city);
            Tick(network, 120.0);
            network.TryStartBreach(city, false, 120.0);
            double now = 120.0;
            for (int i = 0; i < 40 && network.BreachActive; i++)
            {
                network.Tick(now, 0.25f, 0f);
                now += 0.25;
            }
            TestAssert.That(network.BreachTrace > 0f, "a forced run fills the trace");
            float before = network.BreachTrace;
            TestAssert.That(network.TrySpoof(now) == BreachDenial.None, "the spoof applies");
            TestAssert.That(network.BreachTrace < before, "the spoof knocks the trace back");
            TestAssert.That(network.SpoofRechargeRemaining(now) > 0f, "the spoof recharges");
            TestAssert.That(network.TrySpoof(now) == BreachDenial.Recharging, "a second spoof waits for the recharge");
            TestAssert.That(network.TryDisconnect(now), "the disconnect applies");
            TestAssert.That(!network.BreachActive && !network.IsHacked(city), "a disconnect takes nothing");

            // A forced run outruns the trace: backtrace, lockout, heat, nothing taken.
            network = Fresh(out _, out city);
            double clock = 0.0;
            float heat = network.Heat;
            TestAssert.That(Breach(network, city, false, clock, out clock), "the forced run starts");
            TestAssert.That(network.LockoutRemaining(city, clock) > 0f, "a backtrace locks the location out");
            TestAssert.That(network.Heat > heat, "a backtrace feeds the adversary");
            TestAssert.That(!network.IsHacked(city), "the failed breach takes nothing");
            TestAssert.That(network.CheckBreach(city, clock) == BreachDenial.Locked, "the lockout refuses a retry");
        }

        private static void TestCapstone()
        {
            CyberNetwork network = Fresh(out _, out int city);
            double now = 0.0;
            for (int stage = 1; stage <= CyberLocations.StageCount; stage++)
                TestAssert.That(Breach(network, city, true, now, out now), "the quiet stage " + stage + " runs");
            TestAssert.That(network.Stage(city) == 4, "the city is mastered");
            TestAssert.That(network.BreachAwaitingChoice, "a mastered location waits for its capstone");
            TestAssert.That(network.CapstoneCount(Capstone.None) == 0, "no capstone before the choice");
            TestAssert.That(network.TryChooseCapstone(Capstone.Jammer, now), "the capstone applies");
            TestAssert.That(network.CapstoneCount(Capstone.Jammer) == 1 && network.AnyCapstone(Capstone.Jammer),
                "the location fields the capstone");
            TestAssert.That(network.TryCovering(Capstone.Jammer, 8000f, 0f, now, out _), "the capstone covers the radius");
            TestAssert.That(network.TryUseCapstone(Capstone.Jammer, now), "the first use is free");
            TestAssert.That(!network.TryUseCapstone(Capstone.Jammer, now), "the second waits for the recharge");
            TestAssert.That(network.CapstoneRechargeRemaining(Capstone.Jammer, now) > 0f, "the recharge is counting");
        }

        private static void TestVerbs()
        {
            CyberNetwork network = Fresh(out int command, out int city);
            Tick(network, 30.0);
            double now = 30.0;
            TestAssert.That(network.Check(CyberVerb.Isolate, command, now) == CyberDenial.CommandProtected,
                "Cyber Command cannot be isolated");
            TestAssert.That(network.Check(CyberVerb.Patch, command, now) == CyberDenial.NotCompromised,
                "a clean node needs no patch");
            TestAssert.That(network.Check(CyberVerb.Trace, 0, now) == CyberDenial.NoTarget,
                "a verb that needs an incident refuses an empty slot");
            TestAssert.That(network.Check(CyberVerb.Honeypot, city, now) == CyberDenial.Offline,
                "an unhacked location is not on the network yet");

            TestAssert.That(Breach(network, city, true, now, out now), "take the city for the verbs");
            TestAssert.That(network.Check(CyberVerb.Honeypot, city, now) == CyberDenial.None, "a node can be baited");
            TestAssert.That(network.TryVerb(CyberVerb.Honeypot, city, now) == CyberDenial.None, "the bait applies");
            TestAssert.That(network.Node(city).HoneypotUntil > now, "the bait has a clock");
            TestAssert.That(network.RechargeRemaining(CyberVerb.Honeypot, now) > 0f, "the bait recharges");
            TestAssert.That(network.Check(CyberVerb.Honeypot, city, now) == CyberDenial.AlreadyBaited,
                "a second bait is refused while the first is dressed");
            TestAssert.That(network.TryVerb(CyberVerb.Isolate, city, now) == CyberDenial.None, "a node can be isolated");
            TestAssert.That(network.Node(city).Isolated && !network.Working(city), "an isolated node stops working");
            TestAssert.That(network.TryVerb(CyberVerb.Isolate, city, now) == CyberDenial.None, "it can rejoin");
            TestAssert.That(!network.Node(city).Isolated && network.Working(city), "the rejoin is free and immediate");
        }

        private static void TestCampaign()
        {
            CyberNetwork network = Fresh(out _, out int city);
            network.OriginCount = 1;
            double now = 0.0;
            TestAssert.That(!network.IncidentActive(0), "no incident before the campaign runs");
            TestAssert.That(Breach(network, city, true, now, out now), "take the first location");
            double end = now + 200.0;
            while (now < end)
            {
                network.Tick(now, 0.25f, 1f);
                now += 0.25;
            }
            TestAssert.That(network.Incident(0).Kind == IncidentKind.Probe, "the campaign opens incidents");
            TestAssert.That(network.NoticeSerial > 0, "the campaign speaks on the loop");
            TestAssert.That(network.Heat > 0f, "heat builds with the match");
            TestAssert.That(network.Phase != CampaignPhase.Offensive, "the campaign opens probing");
            TestAssert.That(network.Infocon <= 5 && network.Infocon >= 1, "INFOCON stays bounded");
        }

        private static void TestSnapshot()
        {
            CyberNetwork host = Fresh(out _, out int city);
            double now = 0.0;
            TestAssert.That(Breach(host, city, true, now, out now), "take a location for the snapshot");
            host.OriginCount = 1;
            var snapshot = new CyberSnapshot();
            host.Export(now, snapshot);
            TestAssert.That(snapshot.NodeCount >= 2, "the snapshot carries the nodes");
            TestAssert.That(snapshot.Computing == host.Computing && snapshot.Intel == host.Intel,
                "the snapshot carries the resources");

            var client = new CyberNetwork();
            client.Mirror(snapshot, now);
            TestAssert.That(client.NodeCount == host.NodeCount, "the mirror rebuilds every node");
            TestAssert.That(client.IsHacked(city) && client.Stage(city) == host.Stage(city), "the mirror keeps stages");
            TestAssert.That(client.ComputingIncome() == host.ComputingIncome(), "the mirror earns the same");

            snapshot.Stage[0] = 200;
            snapshot.X[0] = float.NaN;
            client.Mirror(snapshot, now);
            TestAssert.That(client.Stage(0) <= CyberLocations.StageCount, "a garbage stage clamps");

            var empty = new CyberSnapshot();
            empty.Clear();
            client.Mirror(empty, now);
            TestAssert.That(client.NodeCount == 0, "an empty snapshot clears the mirror");
        }

        private static void TestForceProbePayment()
        {
            CyberNetwork network = Fresh(out _, out int city);
            Tick(network, 30.0);
            network.SpendComputing(network.Computing - 12f);
            TestAssert.That(network.TryStartBreach(city, false, 30.0) == BreachDenial.LowComputing,
                "loud probe refuses funds between quiet and force cost");
            TestAssert.That(network.Computing == 12f && !network.BreachActive,
                "refused loud probe neither spends nor opens a session");
            Tick(network, 10.0, 30.0);
            float before = network.Computing;
            TestAssert.That(network.TryStartBreach(city, false, 40.0) == BreachDenial.None,
                "funded loud probe starts");
            TestAssert.That(System.Math.Abs(before - network.Computing - CyberNetwork.PhaseCost(BreachPhase.Probe, 1, false)) < 0.001f,
                "accepted loud probe pays its actual force cost");
        }

        private static void TestCapstoneMirrorAndRelisting()
        {
            CyberNetwork host = Fresh(out _, out int city);
            double now = 0.0;
            for (int stage = 1; stage <= CyberLocations.StageCount; stage++)
                TestAssert.That(Breach(host, city, true, now, out now), "master location for remote choice");
            var snapshot = new CyberSnapshot();
            host.Export(now, snapshot);
            var client = new CyberNetwork();
            client.Mirror(snapshot, now + 1000.0);
            TestAssert.That(client.BreachAwaitingChoice && !client.BreachActive,
                "remote mirror preserves capstone choice after breach ends");
            TestAssert.That(System.Math.Abs(client.ChoiceRemaining(now + 1000.0) - host.ChoiceRemaining(now)) < 0.01f,
                "capstone deadline rebases to the client clock");
            TestAssert.That(client.TryChooseCapstone(Capstone.Jammer, now + 1000.0) && client.Node(city).Capstone == Capstone.Jammer,
                "mirrored capstone applies to the mastered target");

            host.BeginLocations();
            host.ReportLocation(200, LocationKind.Airfield, 4000f, 0f, now);
            TestAssert.That(host.ReportLocation(100, LocationKind.City, 8000f, 0f, now) == city,
                "changed discovery order preserves the mastered anchor slot");
            host.EndLocations(now);
            TestAssert.That(host.BreachAwaitingChoice, "relisting the same anchor preserves its pending choice");
            host.BeginLocations();
            host.EndLocations(now);
            host.BeginLocations();
            int replacement = host.ReportLocation(300, LocationKind.City, 8000f, 0f, now);
            host.EndLocations(now);
            TestAssert.That(replacement == city && !host.BreachAwaitingChoice && !host.TryChooseCapstone(Capstone.Jammer, now),
                "removing and recycling the target slot cannot retarget a capstone choice");
            TestAssert.That(host.Node(replacement).Capstone == Capstone.None, "replacement never inherits the old capstone");

            snapshot.BreachTarget = 255;
            client.Mirror(snapshot, now);
            TestAssert.That(!client.BreachAwaitingChoice && !client.BreachActive, "invalid choice target fails closed");
        }

        private static void TestWords()
        {
            CyberNetwork network = Fresh(out _, out int city);
            double now = 0.0;
            TestAssert.That(CyberWords.NodeState(network, city, now) == "UNTAKEN", "an untaken location says so");
            TestAssert.That(Breach(network, city, true, now, out now), "take a location for the words");
            TestAssert.That(CyberWords.NodeState(network, city, now).StartsWith("ONLINE"), "a taken location reads online");
            TestAssert.That(CyberWords.Stage(1) == "FOOTHOLD" && CyberWords.Stage(4) == "MASTERY", "stage names");
            TestAssert.That(CyberWords.Callsign(network, network.CommandSlot).StartsWith("C2N"),
                "the command has its own call sign prefix");
            string advice = CyberWords.Advice(network, now, out _, out _);
            TestAssert.That(!string.IsNullOrEmpty(advice), "the advisor always has a line");
            TestAssert.That(CyberWords.Klaxon(CyberNotice.IntrusionDetected) &&
                !CyberWords.Klaxon(CyberNotice.NodeCompromised), "the klaxon stays narrower than the alarm");
            TestAssert.That(CyberWords.Refusal(BreachDenial.Locked).Contains("LOCKED"), "refusal words exist");
        }
    }
}
