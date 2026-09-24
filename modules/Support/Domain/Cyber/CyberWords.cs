using System;
using System.Globalization;

namespace BoscaliSummer.Features.Support.Domain.Cyber
{
    /// <summary>
    /// Every word the CYBER page, the console and the map use for nodes, stages, breaches,
    /// incidents and refusals, so the three always agree. Status is always a word; colour only
    /// repeats it.
    /// </summary>
    internal static class CyberWords
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly string[] Phonetic =
            {
                "ALPHA", "BRAVO", "CHARLIE", "DELTA", "ECHO", "FOXTROT", "GOLF", "HOTEL", "INDIA", "JULIET", "KILO", "LIMA",
                "MIKE", "NOVEMBER", "OSCAR", "PAPA"
            };

        public const string NetworkName = "AEGIS NET";

        /// <summary>Call sign of a slot: C2N-ALPHA, CTY-BRAVO, AFD-CHARLIE style.</summary>
        public static string Callsign(CyberNetwork network, int slot)
        {
            if (network == null || slot < 0 || slot >= CyberNetwork.SlotCount) return "NET";
            CyberNode node = network.Node(slot);
            string prefix;
            switch (node.Kind)
            {
                case NodeKind.Command: prefix = "C2N"; break;
                case NodeKind.Base: prefix = "BAS"; break;
                case NodeKind.Airfield: prefix = "AFD"; break;
                case NodeKind.City: prefix = "CTY"; break;
                default: return Phonetic[slot % Phonetic.Length];
            }
            return prefix + "-" + Phonetic[slot % Phonetic.Length];
        }

        public static string Verb(CyberVerb verb)
        {
            switch (verb)
            {
                case CyberVerb.Isolate: return "ISOLATE";
                case CyberVerb.Patch: return "PATCH";
                case CyberVerb.Honeypot: return "HONEYPOT";
                case CyberVerb.Trace: return "TRACE";
                default: return "BURN THROUGH";
            }
        }

        public static string VerbHelp(CyberVerb verb)
        {
            switch (verb)
            {
                case CyberVerb.Isolate:
                    return "Cut the node's links. An intrusion there stalls and is contained after " +
                           (int)CyberLocations.ContainSeconds + " s, but the node stops working. Press again to rejoin (free).";
                case CyberVerb.Patch:
                    return "Reimage a compromised node over " + (int)CyberLocations.PatchSeconds +
                           " s. An intrusion still sitting there re-compromises it: isolate first.";
                case CyberVerb.Honeypot:
                    return "Bait a node for " + (int)CyberLocations.HoneypotSeconds +
                           " s: an intrusion walks into it, stalls, and traces twice as fast.";
                case CyberVerb.Trace:
                    return "Follow an intrusion or a detected enemy operation home. Needs a stage-2 location's ear over it; " +
                           "bait doubles the speed. A finished trace opens a foothold for " +
                           (int)CyberLocations.FootholdSeconds + " s: operations cost 25% less and bank an intel token.";
                default:
                    return "Overpower a jamming raid with a hacked location inside or beside it.";
            }
        }

        public static string Denial(CyberDenial denial)
        {
            switch (denial)
            {
                case CyberDenial.None: return "READY";
                case CyberDenial.NoCommand: return "NO CYBER COMMAND";
                case CyberDenial.NoTarget: return "SELECT A TARGET";
                case CyberDenial.Offline: return "NODE OFFLINE";
                case CyberDenial.CommandProtected: return "NOT ON CYBER COMMAND";
                case CyberDenial.NotCompromised: return "NODE IS CLEAN";
                case CyberDenial.AlreadyPatching: return "PATCH RUNNING";
                case CyberDenial.AlreadyBaited: return "ALREADY BAITED";
                case CyberDenial.NeedsEar: return "NO STAGE-2 LOCATION OVER IT";
                case CyberDenial.NeedsCoverage: return "NO HACKED LOCATION COVERS IT";
                case CyberDenial.NotTraceable: return "NOT TRACEABLE";
                case CyberDenial.AlreadyTracing: return "TRACE RUNNING";
                case CyberDenial.LowComputing: return "LOW COMPUTING";
                default: return "RECHARGING";
            }
        }

        public static string Refusal(BreachDenial denial)
        {
            switch (denial)
            {
                case BreachDenial.None: return "READY";
                case BreachDenial.NoCommand: return "NO CYBER COMMAND ONLINE";
                case BreachDenial.NoTarget: return "SELECT A LOCATION";
                case BreachDenial.NotHackable: return "NOT A HACKABLE LOCATION";
                case BreachDenial.AlreadyMine: return "ALREADY YOURS";
                case BreachDenial.OutOfReach: return "OUT OF NETWORK REACH";
                case BreachDenial.Locked: return "LOCATION LOCKED OUT";
                case BreachDenial.Running: return "A BREACH IS RUNNING";
                case BreachDenial.NotRunning: return "NO BREACH RUNNING";
                case BreachDenial.NoSession: return "NO BREACH RUNNING";
                case BreachDenial.LowComputing: return "NOT ENOUGH COMPUTING";
                case BreachDenial.Recharging: return "SPOOF RECHARGING";
                default: return "CHOOSE A CAPSTONE FIRST";
            }
        }

        /// <summary>The one word a node shows, most urgent first.</summary>
        public static string NodeState(CyberNetwork network, int slot, double now)
        {
            if (network == null || !network.Exists(slot)) return "OPEN";
            CyberNode node = network.Node(slot);
            if (node.Static && node.Down) return "DOWN";
            if (!node.Static && !node.Hacked)
                return network.LockoutRemaining(slot, now) > 0f
                    ? "LOCKED " + Seconds(network.LockoutRemaining(slot, now))
                    : "UNTAKEN";
            if (node.Compromised) return node.PatchDone > 0.0 ? "PATCHING " + Seconds(node.PatchDone - now) : "COMPROMISED";
            if (node.Isolated) return "ISOLATED";
            if (network.Jammed(slot, now)) return "JAMMED";
            if (node.HoneypotUntil > now) return "BAITED " + Seconds(node.HoneypotUntil - now);
            if (node.Hacked && node.Stage >= CyberLocations.StageCount)
                return node.Capstone == Capstone.None ? "AWAITING CAPSTONE" : "MASTERED · " + Capstones.Code(node.Capstone);
            return node.Hacked ? "ONLINE · " + Stage(node.Stage) : "HOME";
        }

        public static string Stage(int stage) =>
            stage >= 1 && stage <= CyberLocations.StageCount ? CyberLocations.StageNames[stage] : "—";

        public static string Kind(LocationKind kind) => CyberLocations.KindName(kind);

        public static string PhaseOf(BreachPhase phase)
        {
            switch (phase)
            {
                case BreachPhase.Probe: return "PROBE";
                case BreachPhase.Exploit: return "EXPLOIT";
                case BreachPhase.Extract: return "EXTRACT";
                default: return "IDLE";
            }
        }

        public static string Incident(IncidentKind kind)
        {
            switch (kind)
            {
                case IncidentKind.Probe: return "RECON PROBE";
                case IncidentKind.Intrusion: return "INTRUSION";
                case IncidentKind.Raid: return "JAMMING RAID";
                case IncidentKind.HostileOperation: return "HOSTILE OPERATION";
                default: return "—";
            }
        }

        public static string IncidentCode(IncidentKind kind)
        {
            switch (kind)
            {
                case IncidentKind.Probe: return "PRB";
                case IncidentKind.Intrusion: return "INT";
                case IncidentKind.Raid: return "RAD";
                case IncidentKind.HostileOperation: return "HOP";
                default: return "---";
            }
        }

        public static string Outcome(IncidentOutcome outcome)
        {
            switch (outcome)
            {
                case IncidentOutcome.Blocked: return "BLOCKED";
                case IncidentOutcome.Exposed: return "NETWORK EXPOSED";
                case IncidentOutcome.Contained: return "CONTAINED";
                case IncidentOutcome.Traced: return "TRACED · FOOTHOLD";
                case IncidentOutcome.Withdrew: return "ATTACKER WITHDREW";
                case IncidentOutcome.Broken: return "BURNED THROUGH";
                case IncidentOutcome.Faded: return "ENDED";
                default: return "ACTIVE";
            }
        }

        /// <summary>A good outcome for the defender; the model owns the rule.</summary>
        public static bool Won(IncidentOutcome outcome) => CyberNetwork.Won(outcome);

        public static string Phase(CampaignPhase phase)
        {
            switch (phase)
            {
                case CampaignPhase.Offensive: return "OFFENSIVE";
                case CampaignPhase.Active: return "ACTIVE";
                default: return "PROBING";
            }
        }

        public static string Infocon(int level) => "INFOCON " + Math.Max(1, Math.Min(5, level)).ToString(Invariant);

        /// <summary>A mission-control line for a notice; <paramref name="node"/> and
        /// <paramref name="origin"/> are already names, <paramref name="code"/> is the notice's
        /// raw origin byte when the line needs a number (a chosen capstone).</summary>
        public static string Notice(CyberNotice notice, string node, string origin, byte code = 0)
        {
            switch (notice)
            {
                case CyberNotice.ProbeDetected: return "RECON PROBE ON " + node + " · " + origin;
                case CyberNotice.ProbeBlocked: return "PROBE BLOCKED AT " + node + " · EAR HELD";
                case CyberNotice.ProbeExposed: return "PROBE SUCCEEDED · NETWORK EXPOSED TO " + origin;
                case CyberNotice.IntrusionDetected: return "INTRUSION DETECTED AT " + node + " · " + origin;
                case CyberNotice.NodeCompromised: return node + " COMPROMISED · INTRUSION SPREADING";
                case CyberNotice.CommandCompromised: return "CYBER COMMAND COMPROMISED · ABILITIES OFFLINE";
                case CyberNotice.IntrusionStalled: return "INTRUSION STALLED AT " + node;
                case CyberNotice.IntrusionContained: return "INTRUSION CONTAINED AT " + node;
                case CyberNotice.IntrusionWithdrew: return "INTRUDER WITHDREW · PATCH WHAT THEY LEFT";
                case CyberNotice.TraceStarted: return "TRACE RUNNING ON " + origin;
                case CyberNotice.TraceComplete: return "TRACE COMPLETE · FOOTHOLD IN " + origin;
                case CyberNotice.RaidStarted: return "JAMMING RAID NEAR " + node + " · COVERAGE HALVED";
                case CyberNotice.RaidBroken: return "BURN-THROUGH · RAID BROKEN";
                case CyberNotice.RaidFaded: return "JAMMING RAID ENDED";
                case CyberNotice.HostileOperation: return "NETWORK HEARD " + origin + " OPERATION NEAR " + node;
                case CyberNotice.Patched: return node + " PATCHED · CLEAN";
                case CyberNotice.Isolated: return node + " ISOLATED";
                case CyberNotice.Rejoined: return node + " REJOINED THE NET";
                case CyberNotice.Baited: return "HONEYPOT DRESSED ON " + node;
                case CyberNotice.PhaseRaised: return "ADVERSARY ESCALATING";
                case CyberNotice.SelfRepaired: return node + " REIMAGED BY THE WATCH FLOOR";
                case CyberNotice.PhaseEased: return "ADVERSARY PUSHED BACK";
                case CyberNotice.CommandUp: return "CYBER COMMAND UP AT " + node;
                case CyberNotice.BaseJoined: return node + " JOINED THE NETWORK";
                case CyberNotice.BaseLost: return node + " LOST WITH ITS AIRBASE";
                case CyberNotice.NodeDown: return node + " DOWN · ANCHOR BUILDING DESTROYED";
                case CyberNotice.NodeRestored: return node + " RESTORED · BUILDING REPAIRED";
                case CyberNotice.CommandMoved: return "CYBER COMMAND MOVED TO " + node;
                case CyberNotice.BreachStarted: return "BREACH OPEN ON " + node;
                case CyberNotice.BreachPhaseDone: return node + " · PHASE CLEAR";
                case CyberNotice.BreachStalled: return "BREACH ON " + node + " STALLED · OUT OF COMPUTING";
                case CyberNotice.StageUp: return node + " TAKEN · INFRASTRUCTURE ONLINE";
                case CyberNotice.BreachBacktrace: return "!! BACKTRACED FROM " + node + " · THEY ARE INSIDE YOUR NET";
                case CyberNotice.BreachDisconnected: return "DISCONNECTED FROM " + node + " · NOTHING TAKEN";
                case CyberNotice.CapstoneReady: return node + " IS MASTERED · CHOOSE A CAPSTONE";
                case CyberNotice.CapstoneChosen: return node + " FIELDS " + Capstones.Name((Capstone)Math.Max(0, Math.Min(3, (int)code)));
                case CyberNotice.LocationLost: return node + " LOST · THE AIRBASE CHANGED HANDS";
                default: return null;
            }
        }

        /// <summary>Marked in the loop as something the watch officer must read.</summary>
        public static bool Alarm(CyberNotice notice) =>
            notice == CyberNotice.IntrusionDetected || notice == CyberNotice.CommandCompromised ||
            notice == CyberNotice.CommandLost || notice == CyberNotice.RaidStarted ||
            notice == CyberNotice.NodeCompromised || notice == CyberNotice.ProbeExposed ||
            notice == CyberNotice.BaseLost || notice == CyberNotice.NodeDown ||
            notice == CyberNotice.BreachBacktrace || notice == CyberNotice.LocationLost;

        /// <summary>
        /// Worth a klaxon. Deliberately narrower than <see cref="Alarm"/>: only a break-in, a
        /// command breach or loss, a jamming raid and a backtrace make noise.
        /// </summary>
        public static bool Klaxon(CyberNotice notice) =>
            notice == CyberNotice.IntrusionDetected || notice == CyberNotice.CommandCompromised ||
            notice == CyberNotice.CommandLost || notice == CyberNotice.RaidStarted ||
            notice == CyberNotice.BreachBacktrace;

        /// <summary>
        /// The one thing to do next, in the order a watch officer would do it, with the verb and
        /// target the console's [SPACE] shortcut fires. Empty-state advice comes first: a page
        /// that cannot act says what to do.
        /// </summary>
        public static string Advice(CyberNetwork network, double now, out CyberVerb verb, out int target)
        {
            verb = CyberVerb.Isolate;
            target = -1;
            if (network == null) return "AWAITING THEATER DATA";
            if (!network.HasCommand) return "HOLD AN AIRBASE · CYBER COMMAND COMES UP ON IT BY ITSELF";
            if (!network.CommandOnline)
                return "CYBER COMMAND DOWN · ITS AIRBASE BUILDING IS DESTROYED; IT RETURNS WHEN REPAIRED";

            int command = network.CommandSlot;
            if (network.CommandCompromised)
            {
                if (network.Node(command).PatchDone > 0.0) return "C2 PATCH RUNNING · HOLD";
                verb = CyberVerb.Patch;
                target = command;
                return "CYBER COMMAND BREACHED · PATCH IT [2] · ABILITIES ARE OFFLINE";
            }

            // Live incidents, worst first.
            for (int i = 0; i < CyberNetwork.IncidentSlots; i++)
            {
                if (!network.IncidentActive(i)) continue;
                CyberIncident incident = network.Incident(i);
                if (incident.Kind != IncidentKind.Intrusion) continue;
                int slot = incident.Site;
                if (!incident.Held && network.Exists(slot) && !network.Node(slot).Isolated)
                {
                    verb = CyberVerb.Isolate;
                    target = slot;
                    return network.Node(slot).Compromised
                        ? "INTRUSION IN " + Callsign(network, slot) + " · ISOLATE IT [1] BEFORE IT SPREADS"
                        : "INTRUSION LANDING ON " + Callsign(network, slot) + " · ISOLATE [1] OR BAIT [3] NOW";
                }
                if (!incident.Tracing)
                {
                    if (!network.EarCovers(incident.X, incident.Z, now))
                        return "INTRUSION HELD · A STAGE-2 LOCATION OVER IT WOULD LET YOU TRACE IT HOME";
                    verb = CyberVerb.Trace;
                    target = i;
                    return "INTRUSION HELD AT " + Callsign(network, slot) + " · TRACE IT [4] FOR A FOOTHOLD";
                }
            }
            for (int i = 0; i < CyberNetwork.IncidentSlots; i++)
            {
                if (!network.IncidentActive(i)) continue;
                CyberIncident incident = network.Incident(i);
                if (incident.Kind == IncidentKind.HostileOperation && !incident.Tracing &&
                    network.EarCovers(incident.X, incident.Z, now))
                {
                    verb = CyberVerb.Trace;
                    target = i;
                    return "ENEMY OPERATION HEARD · TRACE IT [4] WHILE IT LASTS";
                }
                if (incident.Kind == IncidentKind.Raid && network.Check(CyberVerb.BurnThrough, i, now) == CyberDenial.None)
                {
                    verb = CyberVerb.BurnThrough;
                    target = i;
                    return "JAMMING RAID ON THE NET · BURN THROUGH IT [5]";
                }
                if (incident.Kind == IncidentKind.Probe && !network.EarCovers(incident.X, incident.Z, now))
                    return "RECON PROBE ON " + Callsign(network, incident.Site) +
                           " · NO STAGE-2 EAR ON IT; YOUR NETWORK WILL BE EXPOSED";
            }

            // Then the housekeeping the network needs.
            for (int slot = 0; slot < CyberNetwork.SlotCount; slot++)
            {
                CyberNode node = network.Node(slot);
                if (!node.Compromised || node.PatchDone > 0.0) continue;
                verb = CyberVerb.Patch;
                target = slot;
                return Callsign(network, slot) + " IS COMPROMISED · PATCH IT [2]";
            }
            for (int slot = 0; slot < CyberNetwork.SlotCount; slot++)
            {
                if (!network.Exists(slot) || !network.Node(slot).Isolated) continue;
                verb = CyberVerb.Isolate;
                target = slot;
                return Callsign(network, slot) + " IS ISOLATED AND IDLE · REJOIN IT [1], IT IS FREE";
            }
            for (int slot = 0; slot < CyberNetwork.SlotCount; slot++)
            {
                if (!network.Exists(slot) || !network.Node(slot).Down) continue;
                return Callsign(network, slot) + " IS DOWN · ITS AIRBASE BUILDING IS DESTROYED UNTIL REPAIRED";
            }

            // Then the offensive game: the breach and the next location.
            if (network.BreachAwaitingChoice)
                return "A LOCATION IS MASTERED · CHOOSE ITS CAPSTONE IN THE CONSOLE";
            if (network.BreachActive)
            {
                int percent = (int)Math.Round(Math.Max(0f, Math.Min(1f, network.BreachTrace)) * 100f);
                return "BREACH " + PhaseOf(network.BreachPhase) + " · TRACE " + percent + "% · SPOOF IF IT CLIMBS";
            }
            if (network.HackedCount == 0)
                return "NO LOCATIONS TAKEN · OPEN THE CONSOLE AND BREACH A CITY OR AIRFIELD";
            int inReach = 0;
            for (int slot = CyberNetwork.TargetBase; slot < CyberNetwork.SlotCount; slot++)
            {
                if (!network.Exists(slot) || network.IsHacked(slot)) continue;
                CyberNode node = network.Node(slot);
                if (network.ReachCovers(node.X, node.Z)) inReach++;
            }
            if (inReach == 0)
                return "NO LOCATION IN REACH · BUY NETWORK REACH OR TAKE AN AIRFIELD CLOSER";
            if (network.Computing < CyberLocations.ExploitCostPerStage)
                return "COMPUTING LOW · IT FUELS EVERY BREACH PHASE";
            if (network.AnyFoothold(now))
                return "FOOTHOLD OPEN · ABILITIES ARE CHEAPER AND NEED NO COVERAGE";
            return "NETWORK HOLDING · " + inReach + " LOCATION" + (inReach == 1 ? "" : "S") +
                   " IN REACH · BREACH ONE FROM THE CONSOLE";
        }

        public static string Seconds(double seconds) =>
            Math.Max(0, (int)Math.Ceiling(seconds)).ToString(Invariant) + "s";
    }
}
