using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;

namespace BoscaliSummer.Tests.Features.Support
{
    internal static class CyberNetworkTests
    {
        private const int Limit = CyberNetwork.FieldSlots;
        private static int nextAnchor = 1000;

        public static void Run()
        {
            TestCatalogue();
            TestPlacement();
            TestInfrastructure();
            TestLinks();
            TestIsolation();
            TestBandwidthAndVerbs();
            TestIntrusionWalksToCommand();
            TestIsolationContains();
            TestHoneypotAndTrace();
            TestPatch();
            TestProbes();
            TestRaids();
            TestSelfRepairAndRelief();
            TestSeekerTally();
            TestAdvice();
            TestCampaignEscalation();
            TestHostileOperations();
            TestLoss();
            TestSnapshot();
            TestWords();
        }

        private static int Place(CyberNetwork network, CyberSiteKind kind, float x, float z)
        {
            if (CyberSites.IsStatic(kind)) return network.PlaceStatic(++nextAnchor, kind, x, z);
            int slot = network.TryBuild(kind, x, z, 100f, Limit);
            TestAssert.That(slot >= 0, "test setup could not place " + kind);
            network.SetPosition(slot, x, z, true);
            return slot;
        }

        private static double Run(CyberNetwork network, double from, double to, float step = 0.5f, float intensity = 0f)
        {
            double now = from;
            while (now < to)
            {
                now += step;
                network.Tick(now, step, intensity);
            }
            return now;
        }

        /// <summary>Tick until the model says so, or fail with the reason. Keeps the tests off the
        /// campaign's exact clock arithmetic.</summary>
        private static double RunUntil(CyberNetwork network, double from, System.Func<bool> done, string what,
                                       double limit = 400.0)
        {
            double now = from;
            while (now < from + limit)
            {
                if (done()) return now;
                now += 0.5;
                network.Tick(now, 0.5f, 0f);
            }
            TestAssert.That(false, "timed out waiting for " + what);
            return now;
        }

        /// <summary>Command at the origin, an early-warning radar 10 km east, a jammer 20 km east.</summary>
        private static CyberNetwork Chain(out int command, out int radar, out int jammer)
        {
            var network = new CyberNetwork();
            network.OriginCount = 2;
            command = Place(network, CyberSiteKind.Command, 0f, 0f);
            radar = Place(network, CyberSiteKind.EarlyWarning, 10000f, 0f);
            jammer = Place(network, CyberSiteKind.Jammer, 20000f, 0f);
            network.Tick(0.1, 0.1f, 0f);
            return network;
        }

        private static void TestCatalogue()
        {
            for (int i = 0; i < CyberSites.All.Length; i++)
            {
                CyberSiteInfo info = CyberSites.All[i];
                TestAssert.That((int)info.Kind == i + 1, "site table must be indexed by its wire byte");
                TestAssert.That(CyberSites.Known((byte)info.Kind), info.Name + " must be a known kind");
                TestAssert.That(info.Code.Length == 3 && info.CopyLimit > 0, info.Name + " needs a code and a copy limit");
                bool field = CyberSites.Fieldable((byte)info.Kind);
                TestAssert.That(field != CyberSites.IsStatic(info.Kind), info.Name + " is either a truck or airbase infrastructure");
                TestAssert.That(field ? info.Price > 0f : info.Price == 0f, info.Name + ": trucks cost, airbase nodes are free");
            }
            TestAssert.That(CyberSites.Field.Length == 4, "four field kinds a player can deploy");
            for (int i = 0; i < CyberSites.Field.Length; i++)
                TestAssert.That(CyberSites.Fieldable((byte)CyberSites.Field[i]), "the field list holds only deployable kinds");
            TestAssert.That(!CyberSites.Known(0) && !CyberSites.Known(200) && !CyberSites.Fieldable((byte)CyberSiteKind.Command),
                "none, garbage and Cyber Command are not deployable");
            TestAssert.That(CyberSites.Info(CyberSiteKind.Command).CopyLimit == 1, "one Cyber Command per faction");
            TestAssert.That(CyberSites.Info(CyberSiteKind.Relay).LinkRange > CyberSites.Info(CyberSiteKind.Sigint).LinkRange,
                "a relay must reach further than a field site");
            TestAssert.That(CyberSites.Info(CyberSiteKind.Gateway).LinkRange > CyberSites.Info(CyberSiteKind.Sigint).LinkRange,
                "an airbase gateway catches field sites further out than a truck does");
            TestAssert.That(CyberSites.Info(CyberSiteKind.Sigint).Emission == Emission.Silent,
                "the SIGINT post is the silent ear");
            TestAssert.That(CyberNetwork.StaticSlots + CyberNetwork.FieldSlots == CyberNetwork.SlotCount,
                "airbase and field slots add up to the network");
        }

        private static void TestPlacement()
        {
            var network = new CyberNetwork();
            TestAssert.That(network.CheckPlacement(CyberSiteKind.Jammer, Limit) == SitePlacement.NeedsCommand,
                "field sites need Cyber Command (an owned airbase) first");
            TestAssert.That(network.CheckPlacement((CyberSiteKind)99, Limit) == SitePlacement.NotPlaceable,
                "a garbage kind must be refused");
            Place(network, CyberSiteKind.Command, 0f, 0f);
            TestAssert.That(network.CheckPlacement(CyberSiteKind.Command, Limit) == SitePlacement.NotPlaceable &&
                network.CheckPlacement(CyberSiteKind.Gateway, Limit) == SitePlacement.NotPlaceable,
                "airbase infrastructure is never ordered by hand");
            TestAssert.That(network.TryBuild(CyberSiteKind.Command, 100f, 0f, 0f, Limit) < 0, "not even through TryBuild");
            Place(network, CyberSiteKind.Gateway, 50000f, 0f);
            Place(network, CyberSiteKind.Sigint, 1000f, 0f);
            Place(network, CyberSiteKind.Sigint, 2000f, 0f);
            TestAssert.That(network.CheckPlacement(CyberSiteKind.Sigint, Limit) == SitePlacement.CopyLimit,
                "SIGINT posts are capped by the catalogue");
            TestAssert.That(network.FieldCount == 2 && network.SiteCount == 4, "airbase nodes are not field sites");
            TestAssert.That(network.CheckPlacement(CyberSiteKind.Relay, 3) == SitePlacement.None,
                "the host limit counts field sites only");
            TestAssert.That(network.CheckPlacement(CyberSiteKind.Relay, 2) == SitePlacement.NetworkFull,
                "the host field limit caps the trucks");
            TestAssert.That(network.TryBuild(CyberSiteKind.Relay, float.NaN, 0f, 0f, Limit) < 0,
                "a non-finite mark must be refused");

            int enRoute = network.TryBuild(CyberSiteKind.Relay, 3000f, 0f, 0f, Limit);
            network.Tick(1.0, 0.1f, 0f);
            TestAssert.That(!network.Online(enRoute) && !network.OnNet(enRoute), "a site en route is not on the net");
            TestAssert.That(network.Check(CyberVerb.Honeypot, enRoute, 1.0) == CyberDenial.Deploying,
                "a site en route cannot be tasked");
        }

        /// <summary>The host reports the airbases it holds once a second; the network follows them.</summary>
        private static void TestInfrastructure()
        {
            var network = new CyberNetwork();
            network.BeginInfrastructure();
            int root = network.ReportInfrastructure(1, CyberSiteKind.Command, 0f, 0f, false, 1.0);
            int far = network.ReportInfrastructure(2, CyberSiteKind.Gateway, 90000f, 0f, false, 1.0);
            network.EndInfrastructure(1.0);
            network.Tick(1.1, 0.1f, 0f);
            TestAssert.That(network.HasCommand && network.CommandOnline, "Cyber Command comes up by itself on an airbase");
            TestAssert.That(network.Static(root) && network.Static(far), "airbase nodes are static");
            TestAssert.That(network.Bandwidth >= CyberNetwork.StarterBandwidth, "the root arrives with starter bandwidth");
            TestAssert.That(network.Linked(root, far) && network.Backbone(root, far) && network.OnNet(far),
                "the backbone links airbases whatever the distance");
            TestAssert.That(network.Stats().Capacity == CyberNetwork.BaseCapacity + CyberNetwork.GatewayCapacity,
                "a gateway on the net adds capacity");
            TestAssert.That(network.NoticeKind(0) == CyberNotice.GatewayJoined && network.NoticeKind(1) == CyberNotice.CommandUp,
                "the loop announces the backbone");

            int truck = Place(network, CyberSiteKind.Sigint, 97000f, 0f);
            network.Tick(1.2, 0.1f, 0f);
            TestAssert.That(network.OnNet(truck) && network.Hops(truck) == 2,
                "a field site links to the nearest airbase node, not only to Cyber Command");
            TestAssert.That(!network.TryScrap(far, out _, out _) && !network.TryRelocate(far),
                "airbase nodes cannot be scrapped or moved");
            TestAssert.That(network.Check(CyberVerb.Isolate, far, 1.2) != CyberDenial.CommandProtected,
                "a gateway can still be isolated to hold an intrusion");

            // The anchor building on the far base is destroyed: the node goes down, the truck strands.
            network.BeginInfrastructure();
            network.ReportInfrastructure(1, CyberSiteKind.Command, 0f, 0f, false, 2.0);
            network.ReportInfrastructure(2, CyberSiteKind.Gateway, 90000f, 0f, true, 2.0);
            network.EndInfrastructure(2.0);
            network.Tick(2.1, 0.1f, 0f);
            TestAssert.That(network.Site(far).Down && !network.Online(far) && !network.OnNet(truck),
                "a destroyed anchor takes the node and what hangs off it offline");
            TestAssert.That(CyberWords.SiteState(network, far, 2.1) == "DOWN", "the node reads DOWN");
            TestAssert.That(network.NoticeKind(0) == CyberNotice.NodeDown, "the loop says the node went down");
            string advice = CyberWords.Advice(network, 2.1, out _, out _);
            TestAssert.That(advice.Contains("DOWN") || advice.Contains("OFF-NET"), "the advisor names the gap");

            // Repaired, then the root base is captured: Cyber Command moves to the surviving base.
            network.BeginInfrastructure();
            network.ReportInfrastructure(2, CyberSiteKind.Command, 90000f, 0f, false, 3.0);
            network.EndInfrastructure(3.0);
            network.Tick(3.1, 0.1f, 0f);
            TestAssert.That(network.CommandSlot == far && network.CommandOnline, "Cyber Command moves when its base falls");
            TestAssert.That(!network.Exists(root), "the captured base leaves the network");
            TestAssert.That(network.OnNet(truck), "the truck by the surviving base is back on the net");
            bool moved = false, lost = false;
            for (int i = 0; i < network.NoticeCount; i++)
            {
                moved |= network.NoticeKind(i) == CyberNotice.CommandMoved;
                lost |= network.NoticeKind(i) == CyberNotice.CommandLost;
            }
            TestAssert.That(moved && lost, "the loop says the old root was lost and the new one took over");

            // Every base gone: no command, no net.
            network.BeginInfrastructure();
            network.EndInfrastructure(4.0);
            network.Tick(4.1, 0.1f, 0f);
            TestAssert.That(!network.HasCommand && !network.OnNet(truck), "a faction with no airbase has no network");
            TestAssert.That(network.CheckPlacement(CyberSiteKind.Relay, Limit) == SitePlacement.NeedsCommand,
                "and cannot deploy trucks");

            // A snapshot carries the static and down flags; a client never invents them.
            var host = new CyberNetwork();
            host.BeginInfrastructure();
            host.ReportInfrastructure(7, CyberSiteKind.Command, 0f, 0f, false, 1.0);
            int gateway = host.ReportInfrastructure(8, CyberSiteKind.Gateway, 30000f, 0f, true, 1.0);
            host.EndInfrastructure(1.0);
            var snapshot = new CyberSnapshot();
            host.Export(1.0, snapshot);
            var client = new CyberNetwork();
            client.Mirror(snapshot, 50.0);
            TestAssert.That(client.Static(gateway) && client.Site(gateway).Down && client.HasCommand,
                "static and down survive the wire");
            TestAssert.That(client.Site(gateway).Anchor == 0, "the host's airbase identity never crosses the wire");
            snapshot.Flags[0] = 16;
            snapshot.Kind[0] = (byte)CyberSiteKind.Relay;
            client.Mirror(snapshot, 51.0);
            TestAssert.That(!client.Site(snapshot.Slot[0]).Static, "a field kind flagged static is not believed");
        }

        private static void TestLinks()
        {
            CyberNetwork network = Chain(out int command, out int radar, out int jammer);
            TestAssert.That(network.OnNet(command) && network.OnNet(radar) && network.OnNet(jammer),
                "a 10 km chain must link back to command");
            TestAssert.That(network.Hops(jammer) == 2 && network.Hops(radar) == 1 && network.Hops(command) == 0,
                "hops count links from command");
            TestAssert.That(network.Linked(command, radar) && !network.Linked(command, jammer),
                "links close only inside range");

            int sigint = Place(network, CyberSiteKind.Sigint, 45000f, 0f);
            network.Tick(0.2, 0.1f, 0f);
            TestAssert.That(!network.OnNet(sigint), "a site 25 km past the chain is off the net");
            TestAssert.That(network.EffectScale(sigint, 0.2) == CyberNetwork.OffNetScale,
                "an off-net site works at half strength");
            TestAssert.That(network.Check(CyberVerb.Honeypot, sigint, 0.2) == CyberDenial.OffNet,
                "an off-net site cannot be tasked from the console");

            int relay = Place(network, CyberSiteKind.Relay, 30000f, 0f);
            network.Tick(0.3, 0.1f, 0f);
            TestAssert.That(network.OnNet(relay) && network.OnNet(sigint), "a relay bridges the gap");
            TestAssert.That(network.Stats().Capacity == CyberNetwork.BaseCapacity + CyberNetwork.RelayCapacity,
                "a relay on the net adds bandwidth capacity");
        }

        private static void TestIsolation()
        {
            CyberNetwork network = Chain(out int command, out int radar, out int jammer);
            Run(network, 0.1, 30.0);
            TestAssert.That(network.Check(CyberVerb.Isolate, command, 30.0) == CyberDenial.CommandProtected,
                "Cyber Command cannot be isolated");
            float before = network.Bandwidth;
            TestAssert.That(network.TryVerb(CyberVerb.Isolate, radar, 30.0) == CyberDenial.None, "isolate must apply");
            TestAssert.That(network.Site(radar).Isolated && !network.OnNet(radar) && !network.OnNet(jammer),
                "isolating the middle site cuts everything behind it");
            TestAssert.That(!network.Working(radar) && network.EffectScale(radar, 30.0) == 0f,
                "an isolated site stops working");
            TestAssert.That(network.Bandwidth < before, "isolate spends bandwidth");
            float spent = network.Bandwidth;
            TestAssert.That(network.TryVerb(CyberVerb.Isolate, radar, 30.1) == CyberDenial.None,
                "rejoining must work during the recharge");
            TestAssert.That(network.Bandwidth == spent, "rejoining is free");
            TestAssert.That(network.OnNet(jammer), "rejoining restores the links behind the site");
        }

        private static void TestBandwidthAndVerbs()
        {
            CyberNetwork network = Chain(out _, out int radar, out int jammer);
            TestAssert.That(network.Bandwidth >= CyberNetwork.StarterBandwidth,
                "Cyber Command arrives with starter bandwidth so the first incident can be answered");
            TestAssert.That(network.TryVerb(CyberVerb.Honeypot, radar, 0.2) == CyberDenial.None,
                "the starter pool covers a first countermeasure");
            TestAssert.That(network.Check(CyberVerb.Trace, 0, 0.2) == CyberDenial.NoTarget,
                "with no incident there is nothing to trace");
            TestAssert.That(network.Bandwidth < CyberNetwork.VerbCost(CyberVerb.Trace),
                "that first countermeasure spends most of the pool");
            CyberStats stats = network.Stats();
            TestAssert.That(stats.Produced == 6f && stats.Drawn == 3f && !stats.Congested,
                "command feeds the radar and the jammer");
            Run(network, 0.1, 10.1);
            TestAssert.That(network.Bandwidth > 15f, "a surplus network refills");
            Run(network, 10.1, 200.0);
            TestAssert.That(network.Bandwidth == stats.Capacity, "bandwidth caps at capacity");

            TestAssert.That(network.TryVerb(CyberVerb.Honeypot, radar, 200.0) == CyberDenial.None, "bait must apply");
            TestAssert.That(network.Check(CyberVerb.Honeypot, jammer, 200.1) == CyberDenial.Recharging,
                "a verb recharges after use");
            TestAssert.That(network.RechargeRemaining(CyberVerb.Honeypot, 200.1) > 0f, "recharge is readable");
            TestAssert.That(network.Check(CyberVerb.Honeypot, radar, 230.0) == CyberDenial.AlreadyBaited,
                "a site cannot be baited twice");
            TestAssert.That(network.Check(CyberVerb.Patch, jammer, 230.0) == CyberDenial.NotCompromised,
                "patching a clean site is refused");
            TestAssert.That(network.Check(CyberVerb.Trace, 0, 230.0) == CyberDenial.NoTarget,
                "tracing nothing is refused");
            TestAssert.That(network.Check((CyberVerb)9, radar, 230.0) == CyberDenial.NoTarget,
                "a garbage verb is refused");

            var congested = new CyberNetwork();
            Place(congested, CyberSiteKind.Command, 0f, 0f);
            Place(congested, CyberSiteKind.Jammer, 1000f, 0f);
            Place(congested, CyberSiteKind.Jammer, 2000f, 0f);
            int loud = Place(congested, CyberSiteKind.Jammer, 3000f, 0f);
            Place(congested, CyberSiteKind.EarlyWarning, 4000f, 0f);
            congested.Tick(1.0, 0.1f, 0f);
            TestAssert.That(congested.Stats().Congested, "four draws exceed command's output");
            TestAssert.That(congested.EffectScale(loud, 1.0) == CyberNetwork.CongestedScale,
                "a congested network degrades its sites");
        }

        private static void TestIntrusionWalksToCommand()
        {
            CyberNetwork network = Chain(out int command, out int radar, out int jammer);
            TestAssert.That(network.Infocon == 5, "a quiet network sits at INFOCON 5");
            int index = network.Force(IncidentKind.Intrusion, 1.0);
            CyberIncident incident = network.Incident(index);
            TestAssert.That(incident.Kind == IncidentKind.Intrusion && incident.Site == jammer,
                "an intrusion enters at the edge");
            TestAssert.That(network.Infocon <= 3, "an intrusion raises INFOCON");

            double now = Run(network, 1.0, 1.0 + CyberNetwork.IntrusionLanding + 0.5);
            TestAssert.That(network.Site(jammer).Compromised && !network.Working(jammer), "the entry site falls");
            now = RunUntil(network, now, () => network.Site(radar).Compromised,
                "the intrusion to walk one link toward command");
            now = RunUntil(network, now, () => network.CommandCompromised, "the intrusion to reach Cyber Command");
            TestAssert.That(network.Infocon == 1, "a compromised command is INFOCON 1");
            TestAssert.That(network.Stats().Produced == 0f, "a compromised command stops feeding bandwidth");

            now = Run(network, now, 1.0 + CyberNetwork.IntrusionLifetime + 1.0);
            TestAssert.That(network.Incident(index).Outcome == IncidentOutcome.Withdrew,
                "the intruder leaves after its lifetime");
            TestAssert.That(network.Breached == 1 && network.Defended == 0, "a withdrawal counts as a breach");
            TestAssert.That(network.CommandCompromised, "what it compromised stays compromised until patched");
            TestAssert.That(command >= 0, "command slot exists");

            // A compromised Cyber Command never heals itself, so the field sites behind it cannot either.
            Run(network, now, now + CyberNetwork.SelfRepairSeconds + 5.0);
            TestAssert.That(network.CommandCompromised && network.Site(radar).Compromised,
                "nothing self-repairs while Cyber Command is breached");
        }

        private static void TestIsolationContains()
        {
            CyberNetwork network = Chain(out _, out _, out int jammer);
            Run(network, 0.1, 30.0);
            float heat = network.Heat;
            int index = network.Force(IncidentKind.Intrusion, 30.0);
            network.TryVerb(CyberVerb.Isolate, jammer, 30.5);
            double now = Run(network, 30.5, 30.5 + CyberNetwork.ContainSeconds + 1.0);
            CyberIncident incident = network.Incident(index);
            TestAssert.That(incident.Outcome == IncidentOutcome.Contained, "an isolated intrusion is contained");
            TestAssert.That(network.Defended == 1 && network.Breached == 0, "containing counts as a win");
            TestAssert.That(network.Heat <= heat, "a win never raises the adversary's heat");
            TestAssert.That(!network.Site(jammer).Compromised, "isolating before landing saves the site");
            TestAssert.That(network.ActiveIncidents(IncidentKind.None) == 0, "a contained incident is no longer active");
            Run(network, now, now + CyberNetwork.ResolvedLinger + 1.0);
            TestAssert.That(network.Incident(index).Kind == IncidentKind.None, "resolved incidents clear after a while");
        }

        private static void TestHoneypotAndTrace()
        {
            CyberNetwork network = Chain(out _, out int radar, out int jammer);
            int sigint = Place(network, CyberSiteKind.Sigint, 0f, 5000f);
            double now = Run(network, 0.1, 60.0);
            int index = network.Force(IncidentKind.Intrusion, now);
            TestAssert.That(network.Incident(index).Site == jammer || network.Incident(index).Site == sigint,
                "the entry is an edge site");

            // Bait the radar: whatever the entry, the walk toward command passes it.
            TestAssert.That(network.TryVerb(CyberVerb.Honeypot, radar, now) == CyberDenial.None, "bait must apply");
            TestAssert.That(network.TryVerb(CyberVerb.Trace, index, now) == CyberDenial.None, "trace must start");
            TestAssert.That(network.Check(CyberVerb.Trace, index, now + 0.1) == CyberDenial.AlreadyTracing,
                "one trace per incident");
            now = Run(network, now, now + CyberNetwork.TraceSeconds + 1.0);
            CyberIncident incident = network.Incident(index);
            TestAssert.That(incident.Outcome == IncidentOutcome.Traced, "a trace completes");
            TestAssert.That(network.FootholdOn(incident.Origin, now) && network.AnyFoothold(now),
                "a finished trace opens a foothold on its origin");
            TestAssert.That(network.FootholdRemaining(incident.Origin, now) <= CyberNetwork.FootholdSeconds,
                "the foothold is time limited");
            Run(network, now, now + CyberNetwork.FootholdSeconds + 1.0);
            TestAssert.That(!network.AnyFoothold(now + CyberNetwork.FootholdSeconds + 1.0), "a foothold expires");

            CyberNetwork deaf = Chain(out _, out _, out _);
            double later = Run(deaf, 0.1, 60.0);
            int blind = deaf.Force(IncidentKind.Intrusion, later);
            TestAssert.That(deaf.Check(CyberVerb.Trace, blind, later) == CyberDenial.NeedsSigint,
                "tracing needs a SIGINT post");
            int probe = deaf.Force(IncidentKind.Probe, later);
            TestAssert.That(deaf.Check(CyberVerb.Trace, probe, later) == CyberDenial.NotTraceable,
                "a probe cannot be traced");

            // A honeypot on the path stalls the intruder.
            CyberNetwork bait = Chain(out _, out int baitRadar, out int baitJammer);
            double t = Run(bait, 0.1, 60.0);
            int walk = bait.Force(IncidentKind.Intrusion, t);
            bait.TryVerb(CyberVerb.Honeypot, baitRadar, t);
            t = RunUntil(bait, t, () => bait.Incident(walk).Site == baitRadar, "the intruder to walk into the bait");
            t = Run(bait, t, t + 1.0);
            TestAssert.That(bait.Incident(walk).Held && !bait.Site(baitRadar).Compromised,
                "an intruder that walks into bait is held there");
            // Bait holds what it catches: the hold outlasts the honeypot itself.
            t = Run(bait, t, t + CyberNetwork.HoneypotSeconds + 1.0);
            TestAssert.That(bait.Incident(walk).Outcome == IncidentOutcome.Contained || bait.Incident(walk).Held,
                "a caught intruder stays held after the bait lapses");
            TestAssert.That(bait.Site(baitJammer).Compromised, "the entry still fell");
            Run(bait, t, t + CyberNetwork.ContainSeconds + 1.0);
            TestAssert.That(bait.Incident(walk).Outcome == IncidentOutcome.Contained, "held long enough, it is contained");
        }

        private static void TestSelfRepairAndRelief()
        {
            CyberNetwork network = Chain(out _, out _, out int jammer);
            double now = Run(network, 0.1, 60.0);
            int index = network.Force(IncidentKind.Intrusion, now);
            now = Run(network, now, now + CyberNetwork.IntrusionLanding + 0.5);
            TestAssert.That(network.Site(jammer).Compromised, "setup: the entry site fell");
            network.TryVerb(CyberVerb.Isolate, jammer, now);
            now = Run(network, now, now + CyberNetwork.ContainSeconds + 1.0);
            TestAssert.That(network.Incident(index).Outcome == IncidentOutcome.Contained, "setup: contained");

            // Left alone, the watch floor reimages a field site the adversary no longer holds.
            TestAssert.That(network.Site(jammer).Compromised, "it is still dirty right after containment");
            now = Run(network, now, now + CyberNetwork.SelfRepairSeconds + 2.0);
            TestAssert.That(!network.Site(jammer).Compromised, "a quiet compromised field site reimages itself");
            TestAssert.That(network.NoticeKind(0) == CyberNotice.SelfRepaired ||
                network.NoticeKind(1) == CyberNotice.SelfRepaired, "the loop says so");

            // Winning pushes heat back down; the player steers the escalation.
            var hot = new CyberNetwork { OriginCount = 1 };
            int command = Place(hot, CyberSiteKind.Command, 0f, 0f);
            Place(hot, CyberSiteKind.EarlyWarning, 8000f, 0f);
            hot.Tick(0.5, 0.1f, 0f);
            TestAssert.That(hot.Bandwidth >= CyberNetwork.StarterBandwidth,
                "Cyber Command arrives with enough bandwidth to answer the first incident");
            TestAssert.That(command >= 0, "command placed");
            for (int i = 0; i < 6; i++) hot.NoteOffensive();
            float raised = hot.Heat;
            int probe = hot.Force(IncidentKind.Probe, 1.0);
            Place(hot, CyberSiteKind.Sigint, 8000f, 1000f);
            hot.Tick(1.5, 0.5f, 0f);
            Run(hot, 1.5, 1.5 + CyberNetwork.ProbeSeconds + 1.0);
            TestAssert.That(hot.Incident(probe).Outcome == IncidentOutcome.Blocked, "setup: the probe was blocked");
            TestAssert.That(hot.Heat < raised && hot.Defended == 1, "a defended incident cools the adversary");
        }

        private static void TestSeekerTally()
        {
            CyberNetwork network = Chain(out _, out _, out int jammer);
            network.NoteSeekerDefeated(jammer, 10.0);
            TestAssert.That(network.SeekersDefeated == 1 && network.NoticeKind(0) == CyberNotice.SeekerDefeated,
                "a broken seeker is counted and called");
            network.NoteSeekerDefeated(jammer, 10.5);
            TestAssert.That(network.SeekersDefeated == 2 && network.NoticeKind(1) != CyberNotice.SeekerDefeated,
                "the loop is not spammed by a salvo");
            network.NoteSeekerDefeated(jammer, 10.0 + CyberNetwork.SeekerNoticeGap + 0.5);
            TestAssert.That(network.NoticeKind(0) == CyberNotice.SeekerDefeated, "it speaks again after the gap");
        }

        private static void TestAdvice()
        {
            TestAssert.That(CyberWords.Advice(null, 0.0, out _, out _).Contains("AWAITING"),
                "advice survives a missing network");
            var empty = new CyberNetwork();
            TestAssert.That(CyberWords.Advice(empty, 0.0, out _, out int none).Contains("CYBER COMMAND") && none < 0,
                "an empty network is told what to build first");

            CyberNetwork network = Chain(out int command, out _, out int jammer);
            double now = Run(network, 0.1, 60.0);
            string quiet = CyberWords.Advice(network, now, out _, out _);
            TestAssert.That(quiet.Contains("SIG POST"), "a network with no SIGINT is told why it wants one");

            int index = network.Force(IncidentKind.Intrusion, now);
            string landing = CyberWords.Advice(network, now, out CyberVerb verb, out int target);
            TestAssert.That(verb == CyberVerb.Isolate && target == jammer && landing.Contains("ISOLATE"),
                "a landing intrusion is answered with ISOLATE on its site");
            now = Run(network, now, now + CyberNetwork.IntrusionLanding + 0.5);
            network.TryVerb(CyberVerb.Isolate, jammer, now);
            now = Run(network, now, now + 2.0);
            TestAssert.That(CyberWords.Advice(network, now, out CyberVerb held, out int incident).Contains("TRACE") ==
                (held == CyberVerb.Trace && incident == index) || !network.AnyWorking(CyberSiteKind.Sigint),
                "a held intrusion offers a trace only with a SIGINT post");

            now = Run(network, now, now + CyberNetwork.ContainSeconds + 2.0);
            string dirty = CyberWords.Advice(network, now, out CyberVerb patch, out int slot);
            TestAssert.That(patch == CyberVerb.Patch && slot == jammer && dirty.Contains("PATCH"),
                "a site the adversary actually took is answered with PATCH");
            network.TryVerb(CyberVerb.Patch, jammer, now);
            now = Run(network, now, now + CyberNetwork.PatchSeconds + 1.0);
            string clean = CyberWords.Advice(network, now, out CyberVerb rejoin, out int cut);
            TestAssert.That(rejoin == CyberVerb.Isolate && cut == jammer && clean.Contains("REJOIN"),
                "a clean site left isolated is answered with REJOIN");
        }

        private static void TestPatch()
        {
            CyberNetwork network = Chain(out _, out _, out int jammer);
            double now = Run(network, 0.1, 60.0);
            int index = network.Force(IncidentKind.Intrusion, now);
            now = Run(network, now, now + CyberNetwork.IntrusionLanding + 0.5);
            TestAssert.That(network.Site(jammer).Compromised, "setup: the jammer fell");
            network.TryVerb(CyberVerb.Isolate, jammer, now);
            TestAssert.That(network.TryVerb(CyberVerb.Patch, jammer, now) == CyberDenial.None,
                "an isolated compromised site can still be patched");
            TestAssert.That(network.Check(CyberVerb.Patch, jammer, now + 1.0) == CyberDenial.AlreadyPatching,
                "one patch at a time");
            now = Run(network, now, now + CyberNetwork.PatchSeconds + 0.5);
            TestAssert.That(!network.Site(jammer).Compromised, "a patch cleans the site");
            TestAssert.That(network.Incident(index).Outcome == IncidentOutcome.Active,
                "the stalled intrusion is still being contained");
        }

        private static void TestProbes()
        {
            CyberNetwork exposed = Chain(out _, out int radar, out int jammer);
            int probe = exposed.Force(IncidentKind.Probe, 1.0);
            int target = exposed.Incident(probe).Site;
            TestAssert.That(target == radar || target == jammer, "a probe targets an emitter");
            double now = Run(exposed, 1.0, 1.0 + CyberNetwork.ProbeSeconds + 0.5);
            TestAssert.That(exposed.Incident(probe).Outcome == IncidentOutcome.Exposed, "an unheard probe succeeds");
            TestAssert.That(exposed.ExposedUntil > now, "a successful probe exposes the emitters");

            CyberNetwork guarded = Chain(out _, out _, out _);
            Place(guarded, CyberSiteKind.Sigint, 15000f, 2000f);
            guarded.Tick(0.5, 0.1f, 0f);
            int blocked = guarded.Force(IncidentKind.Probe, 1.0);
            Run(guarded, 1.0, 1.0 + CyberNetwork.ProbeSeconds + 0.5);
            TestAssert.That(guarded.Incident(blocked).Outcome == IncidentOutcome.Blocked, "a SIGINT post blocks a probe");
            TestAssert.That(guarded.ExposedUntil == 0.0, "a blocked probe exposes nothing");

            var silent = new CyberNetwork();
            Place(silent, CyberSiteKind.Command, 0f, 0f);
            Place(silent, CyberSiteKind.Sigint, 1000f, 0f);
            silent.Tick(0.5, 0.1f, 0f);
            TestAssert.That(silent.Force(IncidentKind.Probe, 1.0) < 0, "a silent network gives a probe nothing to find");
        }

        private static void TestRaids()
        {
            CyberNetwork network = Chain(out _, out int radar, out int jammer);
            Run(network, 0.1, 60.0);
            int raid = network.Force(IncidentKind.Raid, 60.0);
            CyberIncident incident = network.Incident(raid);
            TestAssert.That(incident.Kind == IncidentKind.Raid && incident.Site == -1, "a raid is a sector, not a site");
            network.Tick(60.5, 0.5f, 0f);
            bool anyJammed = network.Jammed(radar, 60.5) || network.Jammed(jammer, 60.5);
            TestAssert.That(anyJammed, "a raid covers the site it was aimed near");
            TestAssert.That(network.EffectRadius(radar, 60.5) <= CyberSites.Info(CyberSiteKind.EarlyWarning).EffectRadius,
                "radar cover never grows in a raid");

            network.TrySetMode(jammer, EwPosture.SigintPassive);
            TestAssert.That(network.Check(CyberVerb.BurnThrough, raid, 60.5) == CyberDenial.NeedsJammer,
                "a jammer in EMCON cannot burn through");
            network.TrySetMode(jammer, EwPosture.NoiseJamming);
            if (network.Working(jammer))
            {
                TestAssert.That(network.TryVerb(CyberVerb.BurnThrough, raid, 60.5) == CyberDenial.None,
                    "a working jammer burns through");
                TestAssert.That(network.Incident(raid).Outcome == IncidentOutcome.Broken, "the raid is broken");
                TestAssert.That(!network.Jammed(radar, 60.6), "a broken raid stops jamming");
            }

            CyberNetwork faded = Chain(out _, out _, out _);
            int natural = faded.Force(IncidentKind.Raid, 1.0);
            Run(faded, 1.0, 1.0 + CyberNetwork.RaidSeconds + 0.5);
            TestAssert.That(faded.Incident(natural).Outcome == IncidentOutcome.Faded, "an unanswered raid ends by itself");
            TestAssert.That(faded.Check(CyberVerb.BurnThrough, natural, 200.0) == CyberDenial.NoTarget,
                "a finished raid cannot be burned");
        }

        private static void TestCampaignEscalation()
        {
            CyberNetwork network = Chain(out _, out _, out _);
            network.Seed(1234);
            Run(network, 0.1, 10.0, 0.5f, 0f);
            TestAssert.That(network.Heat == 0f && network.NextIncident == 0.0, "intensity zero switches the campaign off");

            var idle = new CyberNetwork { OriginCount = 1 };
            Run(idle, 0.1, 200.0, 0.5f, 1f);
            TestAssert.That(idle.Heat == 0f, "no campaign runs against a faction with no Cyber Command");

            var bases = new CyberNetwork { OriginCount = 1 };
            Place(bases, CyberSiteKind.Command, 0f, 0f);
            Place(bases, CyberSiteKind.Gateway, 40000f, 0f);
            Run(bases, 0.1, 400.0, 0.5f, 1f);
            TestAssert.That(bases.Heat == 0f && bases.ActiveIncidents(IncidentKind.None) == 0,
                "airbase nodes alone draw no campaign: it starts when the faction fields a truck");

            double now = Run(network, 10.0, 10.0 + CyberNetwork.FirstIncidentDelay - 5.0, 0.5f, 1f);
            TestAssert.That(network.ActiveIncidents(IncidentKind.None) == 0, "the adversary gives a grace period");
            TestAssert.That(network.Phase == CampaignPhase.Probing, "the campaign opens probing");
            now = Run(network, now, now + 10.0, 0.5f, 1f);
            TestAssert.That(network.Incident(0).Kind == IncidentKind.Probe, "the first move is a probe");

            now = Run(network, now, CyberNetwork.ActiveHeat / CyberNetwork.HeatPerSecond + 20.0, 0.5f, 1f);
            TestAssert.That(network.Phase == CampaignPhase.Active, "heat raises the phase with time");
            network.NoteOffensive();
            network.NoteOffensive();
            network.NoteOffensive();
            network.NoteOffensive();
            network.NoteOffensive();
            TestAssert.That(network.Heat >= CyberNetwork.ActiveHeat + 5f * CyberNetwork.OffensiveHeatSpike - 1f,
                "offensive operations draw heat");

            int intrusions = 0, raids = 0;
            for (int i = 0; i < 30; i++)
            {
                now = Run(network, now, now + 60.0, 1f, 1f);
                intrusions += network.ActiveIncidents(IncidentKind.Intrusion);
                raids += network.ActiveIncidents(IncidentKind.Raid);
                TestAssert.That(network.ActiveIncidents(IncidentKind.Intrusion) <= 2, "at most two intrusions at once");
                TestAssert.That(network.ActiveIncidents(IncidentKind.Raid) <= 1, "at most one raid at once");
            }
            TestAssert.That(network.Phase == CampaignPhase.Offensive && network.Heat <= CyberNetwork.HeatMaximum,
                "heat caps in the offensive phase");
            TestAssert.That(intrusions > 0 && raids > 0, "a hot campaign intrudes and raids");
        }

        private static void TestHostileOperations()
        {
            CyberNetwork deaf = Chain(out _, out _, out _);
            TestAssert.That(!deaf.ReportHostile(1, 10000f, 0f, 1.0), "without SIGINT an enemy operation goes unheard");

            CyberNetwork network = Chain(out _, out _, out _);
            Place(network, CyberSiteKind.Sigint, 12000f, 3000f);
            network.Tick(0.5, 0.1f, 0f);
            TestAssert.That(!network.ReportHostile(1, 90000f, 0f, 1.0), "an operation outside the ear goes unheard");
            TestAssert.That(!network.ReportHostile(200, 10000f, 0f, 1.0), "a garbage origin is dropped");
            TestAssert.That(network.ReportHostile(1, 10000f, 0f, 1.0), "an operation under the ear is heard");
            TestAssert.That(network.ActiveIncidents(IncidentKind.HostileOperation) == 1, "a heard operation is an incident");
            TestAssert.That(network.NoticeKind(0) == CyberNotice.HostileOperation, "the loop hears it");
            Run(network, 1.0, 1.0 + CyberNetwork.HostileSeconds + 1.0);
            TestAssert.That(network.ActiveIncidents(IncidentKind.HostileOperation) == 0, "an untraced operation fades");
        }

        private static void TestLoss()
        {
            CyberNetwork network = Chain(out int command, out int radar, out int jammer);
            network.MarkLost(radar, 5.0);
            network.Tick(5.1, 0.1f, 0f);
            TestAssert.That(network.Site(radar).Lost && !network.OnNet(jammer), "losing a link site strands its tail");
            TestAssert.That(network.NoticeKind(0) == CyberNotice.SiteLost, "a loss is announced");
            TestAssert.That(network.CheckPlacement(CyberSiteKind.EarlyWarning, Limit) == SitePlacement.None,
                "a lost site frees its copy at once");
            TestAssert.That(!network.TryScrap(radar, out _, out _), "a lost site cannot be scrapped for a refund");
            Run(network, 5.1, 5.1 + CyberNetwork.LostLingerSeconds + 1.0);
            TestAssert.That(!network.Exists(radar), "a lost slot frees after the linger");

            TestAssert.That(network.TryRelocate(jammer) && !network.Online(jammer) && !network.OnNet(jammer),
                "a relocating site drops off the net");
            TestAssert.That(!network.TryRelocate(jammer), "a site already moving cannot be moved again");
            network.SetPosition(jammer, 2000f, 0f, true);
            network.Tick(40.0, 0.1f, 0f);
            TestAssert.That(network.OnNet(jammer) && network.Site(jammer).X == 2000f, "it relinks where it stops");

            TestAssert.That(!network.TryScrap(command, out _, out _), "Cyber Command is never scrapped");
            TestAssert.That(network.TryScrap(jammer, out CyberSiteKind kind, out float paid) &&
                kind == CyberSiteKind.Jammer && paid == 100f, "scrapping returns what was paid");

            CyberNetwork beheaded = Chain(out _, out _, out _);
            beheaded.BeginInfrastructure();
            beheaded.EndInfrastructure(1.0);
            beheaded.Tick(1.1, 0.1f, 0f);
            TestAssert.That(beheaded.NoticeKind(0) == CyberNotice.CommandLost && beheaded.Stats().OnNet == 0,
                "losing the command airbase takes the whole net down");
        }

        private static void TestSnapshot()
        {
            CyberNetwork host = Chain(out _, out int radar, out int jammer);
            Place(host, CyberSiteKind.Sigint, 12000f, 3000f);
            double now = Run(host, 0.1, 60.0);
            host.TrySetMode(jammer, EwPosture.GhostSpoofing);
            host.NoteSeekerDefeated(jammer, now);
            host.NoteSeekerDefeated(jammer, now);
            host.TryVerb(CyberVerb.Honeypot, radar, now);
            int intrusion = host.Force(IncidentKind.Intrusion, now);
            host.TryVerb(CyberVerb.Trace, intrusion, now);
            now = Run(host, now, now + 5.0);

            var snapshot = new CyberSnapshot();
            host.Export(now, snapshot);
            TestAssert.That(snapshot.SiteCount == 4 && snapshot.IncidentCount == 1, "export carries sites and incidents");

            var client = new CyberNetwork();
            double clientNow = 500.0;
            client.Mirror(snapshot, clientNow);
            TestAssert.That(client.SiteCount == 4 && client.OnNet(radar), "the mirror rebuilds the net and its links");
            TestAssert.That(client.Site(jammer).Mode == EwPosture.GhostSpoofing, "jammer mode survives the wire");
            TestAssert.That(client.Site(radar).HoneypotUntil > clientNow, "bait clocks are rebased");
            TestAssert.That(System.Math.Abs(client.Bandwidth - host.Bandwidth) < 0.01f, "bandwidth survives the wire");
            TestAssert.That(client.SeekersDefeated == 2, "the ECM tally survives the wire");
            TestAssert.That(client.IncidentActive(intrusion) || client.ActiveIncidents(IncidentKind.Intrusion) == 1,
                "the intrusion survives the wire");
            CyberIncident mirrored = client.Incident(0);
            TestAssert.That(mirrored.Tracing && mirrored.Trace > 0f, "trace progress survives the wire");
            TestAssert.That(client.NoticeSerial == host.NoticeSerial && client.NoticeKind(0) == host.NoticeKind(0),
                "notices survive the wire");
            TestAssert.That(System.Math.Abs(client.RechargeRemaining(CyberVerb.Honeypot, clientNow) -
                host.RechargeRemaining(CyberVerb.Honeypot, now)) < 0.01f, "recharges are rebased");

            double stable = client.Site(radar).HoneypotUntil;
            host.Export(now + 0.2, snapshot);
            client.Mirror(snapshot, clientNow + 0.2);
            TestAssert.That(client.Site(radar).HoneypotUntil == stable, "a small clock drift is ignored");

            snapshot.Kind[0] = 99;
            snapshot.X[1] = float.NaN;
            snapshot.IncidentKind[0] = 77;
            snapshot.Bandwidth = float.PositiveInfinity;
            snapshot.Mode[2] = 250;
            snapshot.SiteCount = 200;
            client.Mirror(snapshot, clientNow + 1.0);
            TestAssert.That(client.SiteCount <= 2, "garbage sites are dropped");
            TestAssert.That(client.ActiveIncidents(IncidentKind.None) == 0, "a garbage incident is dropped");
            TestAssert.That(client.Bandwidth == 0f, "garbage bandwidth reads as empty");

            snapshot.Clear();
            client.Mirror(snapshot, clientNow + 2.0);
            TestAssert.That(client.SiteCount == 0 && !client.HasCommand, "an empty snapshot clears the mirror");
        }

        private static void TestWords()
        {
            for (int i = 0; i <= (int)CyberDenial.Recharging; i++)
                TestAssert.That(!string.IsNullOrEmpty(CyberWords.Denial((CyberDenial)i)), "every denial needs a word");
            for (int i = 0; i < CyberNetwork.VerbCount; i++)
            {
                TestAssert.That(!string.IsNullOrEmpty(CyberWords.Verb((CyberVerb)i)) &&
                    !string.IsNullOrEmpty(CyberWords.VerbHelp((CyberVerb)i)), "every verb needs a name and help");
                TestAssert.That(CyberNetwork.VerbCost((CyberVerb)i) > 0f && CyberNetwork.VerbRecharge((CyberVerb)i) > 0f,
                    "every verb costs and recharges");
            }
            for (int i = 1; i <= (int)CyberNetwork.LastNotice; i++)
                TestAssert.That(!string.IsNullOrEmpty(CyberWords.Notice((CyberNotice)i, "EWR-KILO", "RED")),
                    "every notice needs a loop line");
            // Alert fatigue: everything that sounds the klaxon is an alarm, but not the other way round.
            int klaxons = 0, alarms = 0;
            for (int i = 1; i <= (int)CyberNetwork.LastNotice; i++)
            {
                var notice = (CyberNotice)i;
                if (CyberWords.Klaxon(notice))
                {
                    klaxons++;
                    TestAssert.That(CyberWords.Alarm(notice), "a klaxon line must also read as an alarm");
                }
                if (CyberWords.Alarm(notice)) alarms++;
            }
            TestAssert.That(klaxons > 0 && klaxons < alarms, "the klaxon must be rarer than the alarm lines");

            CyberNetwork network = Chain(out _, out int radar, out _);
            TestAssert.That(CyberWords.Callsign(network, radar) == "EWR-BRAVO", "call signs read CODE-PHONETIC");
            TestAssert.That(CyberWords.SiteState(network, radar, 1.0) == "ONLINE", "a healthy site reads ONLINE");
            TestAssert.That(CyberWords.SiteState(network, 11, 1.0) == "OPEN", "an empty slot reads OPEN");
            TestAssert.That(CyberWords.Infocon(0) == "INFOCON 1" && CyberWords.Infocon(9) == "INFOCON 5",
                "INFOCON clamps to 1..5");
            TestAssert.That(CyberWords.Won(IncidentOutcome.Traced) && !CyberWords.Won(IncidentOutcome.Exposed),
                "outcomes read as wins or losses");
        }
    }
}
