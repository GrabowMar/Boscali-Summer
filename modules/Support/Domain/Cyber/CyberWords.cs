using System;
using System.Globalization;

namespace BoscaliSummer.Features.Support.Domain.Cyber
{
    /// <summary>
    /// Every word the CYBER pages, the console and the map use for sites, verbs, incidents and
    /// refusals, so the three always agree. Status is always a word; colour only repeats it.
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

        /// <summary>Call sign of a slot: KILO-3 style, stable for the slot's life.</summary>
        public static string Callsign(CyberNetwork network, int slot)
        {
            if (slot < 0 || slot >= CyberNetwork.SlotCount) return "NET";
            CyberSiteKind kind = network != null ? network.Site(slot).Kind : CyberSiteKind.None;
            return kind == CyberSiteKind.None
                ? Phonetic[slot]
                : CyberSites.Code(kind) + "-" + Phonetic[slot];
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
                    return "Cut the site's links. An intrusion there stalls and is contained after " +
                           (int)CyberNetwork.ContainSeconds + " s, but the site stops working. Press again to rejoin (free).";
                case CyberVerb.Patch:
                    return "Reimage a compromised site over " + (int)CyberNetwork.PatchSeconds +
                           " s. An intrusion still sitting there re-compromises it: isolate first.";
                case CyberVerb.Honeypot:
                    return "Bait a site for " + (int)CyberNetwork.HoneypotSeconds +
                           " s: an intrusion walks into it, stalls, and traces twice as fast.";
                case CyberVerb.Trace:
                    return "Follow an intrusion or a detected enemy operation home. Needs a working SIGINT post; " +
                           "bait doubles the speed. A finished trace opens a foothold for " +
                           (int)CyberNetwork.FootholdSeconds + " s: operations cost 25% less, need no jammer of your " +
                           "own in reach, and you bank an INTEL token.";
                default:
                    return "Overpower a jamming raid with a working jammer inside or beside it.";
            }
        }

        public static string Denial(CyberDenial denial)
        {
            switch (denial)
            {
                case CyberDenial.None: return "READY";
                case CyberDenial.NoCommand: return "NO CYBER COMMAND";
                case CyberDenial.NoTarget: return "SELECT A TARGET";
                case CyberDenial.Deploying: return "SITE EN ROUTE";
                case CyberDenial.Lost: return "SITE LOST";
                case CyberDenial.OffNet: return "SITE OFF-NET";
                case CyberDenial.CommandProtected: return "NOT ON CYBER COMMAND";
                case CyberDenial.NotCompromised: return "SITE IS CLEAN";
                case CyberDenial.AlreadyPatching: return "PATCH RUNNING";
                case CyberDenial.AlreadyBaited: return "ALREADY BAITED";
                case CyberDenial.NeedsSigint: return "NEEDS A WORKING SIG POST";
                case CyberDenial.NeedsJammer: return "NO JAMMER IN REACH";
                case CyberDenial.NotTraceable: return "NOT TRACEABLE";
                case CyberDenial.AlreadyTracing: return "TRACE RUNNING";
                case CyberDenial.LowBandwidth: return "LOW BANDWIDTH";
                default: return "RECHARGING";
            }
        }

        public static string Placement(SitePlacement placement)
        {
            switch (placement)
            {
                case SitePlacement.None: return "FITS";
                case SitePlacement.NeedsCommand: return "HOLD AN AIRBASE FIRST";
                case SitePlacement.CopyLimit: return "COPY LIMIT";
                case SitePlacement.NetworkFull: return "NETWORK FULL";
                default: return "NOT PLACEABLE";
            }
        }

        /// <summary>The one word a site shows, most urgent first.</summary>
        public static string SiteState(CyberNetwork network, int slot, double now)
        {
            if (network == null || !network.Exists(slot)) return "OPEN";
            CyberSite site = network.Site(slot);
            if (site.Lost) return "LOST";
            if (site.Down) return "DOWN";
            if (site.Deploying) return "EN ROUTE";
            if (site.Compromised) return site.PatchDone > 0.0 ? "PATCHING " + Seconds(site.PatchDone - now) : "COMPROMISED";
            if (site.Isolated) return "ISOLATED";
            if (!network.OnNet(slot)) return "OFF-NET";
            if (site.HoneypotUntil > now) return "BAITED " + Seconds(site.HoneypotUntil - now);
            if (network.Jammed(slot, now)) return "JAMMED";
            return "ONLINE";
        }

        public static string Mode(EwPosture mode)
        {
            switch (mode)
            {
                case EwPosture.SigintPassive: return "EMCON";
                case EwPosture.GhostSpoofing: return "DECEPTION";
                default: return "NOISE";
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
                case IncidentOutcome.Exposed: return "EMITTERS EXPOSED";
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

        /// <summary>A mission-control line for a notice; <paramref name="origin"/> is already a name.</summary>
        public static string Notice(CyberNotice notice, string site, string origin)
        {
            switch (notice)
            {
                case CyberNotice.SiteOnline: return site + " ON STATION · LINKING";
                case CyberNotice.SiteLost: return site + " DESTROYED · SITE LOST";
                case CyberNotice.CommandLost: return "CYBER COMMAND LOST WITH ITS AIRBASE · NETWORK DOWN";
                case CyberNotice.ProbeDetected: return "RECON PROBE ON " + site + " · " + origin;
                case CyberNotice.ProbeBlocked: return "PROBE BLOCKED AT " + site + " · SIGINT HELD";
                case CyberNotice.ProbeExposed: return "PROBE SUCCEEDED · EMITTERS EXPOSED TO " + origin;
                case CyberNotice.IntrusionDetected: return "INTRUSION DETECTED AT " + site + " · " + origin;
                case CyberNotice.SiteCompromised: return site + " COMPROMISED · INTRUSION SPREADING";
                case CyberNotice.CommandCompromised: return "CYBER COMMAND COMPROMISED · OPERATIONS LOCKED";
                case CyberNotice.IntrusionStalled: return "INTRUSION STALLED AT " + site;
                case CyberNotice.IntrusionContained: return "INTRUSION CONTAINED AT " + site;
                case CyberNotice.IntrusionWithdrew: return "INTRUDER WITHDREW · PATCH WHAT THEY LEFT";
                case CyberNotice.TraceStarted: return "TRACE RUNNING ON " + origin;
                case CyberNotice.TraceComplete: return "TRACE COMPLETE · FOOTHOLD IN " + origin;
                case CyberNotice.RaidStarted: return "JAMMING RAID NEAR " + site + " · LINKS DEGRADED";
                case CyberNotice.RaidBroken: return "BURN-THROUGH · RAID BROKEN";
                case CyberNotice.RaidFaded: return "JAMMING RAID ENDED";
                case CyberNotice.HostileOperation: return "SIGINT HEARD " + origin + " OPERATION NEAR " + site;
                case CyberNotice.Patched: return site + " PATCHED · CLEAN";
                case CyberNotice.Isolated: return site + " ISOLATED";
                case CyberNotice.Rejoined: return site + " REJOINED THE NET";
                case CyberNotice.Baited: return "HONEYPOT DRESSED ON " + site;
                case CyberNotice.PhaseRaised: return "ADVERSARY ESCALATING";
                case CyberNotice.SeekerDefeated: return "ECM · RADAR MISSILE LOST LOCK OVER " + site;
                case CyberNotice.SelfRepaired: return site + " REIMAGED BY THE WATCH FLOOR";
                case CyberNotice.PhaseEased: return "ADVERSARY PUSHED BACK";
                case CyberNotice.CommandUp: return "CYBER COMMAND UP AT " + site + " · BACKBONE LIVE";
                case CyberNotice.GatewayJoined: return site + " JOINED THE BACKBONE";
                case CyberNotice.GatewayLost: return site + " LOST WITH ITS AIRBASE";
                case CyberNotice.NodeDown: return site + " DOWN · ANCHOR BUILDING DESTROYED";
                case CyberNotice.NodeRestored: return site + " RESTORED · BUILDING REPAIRED";
                case CyberNotice.CommandMoved: return "CYBER COMMAND MOVED TO " + site;
                default: return null;
            }
        }

        /// <summary>Marked in the loop as something the watch officer must read.</summary>
        public static bool Alarm(CyberNotice notice) =>
            notice == CyberNotice.IntrusionDetected || notice == CyberNotice.CommandCompromised ||
            notice == CyberNotice.CommandLost || notice == CyberNotice.RaidStarted ||
            notice == CyberNotice.SiteCompromised || notice == CyberNotice.ProbeExposed ||
            notice == CyberNotice.SiteLost || notice == CyberNotice.GatewayLost || notice == CyberNotice.NodeDown;

        /// <summary>
        /// Worth a klaxon. Deliberately narrower than <see cref="Alarm"/>: a sound for every
        /// serious line teaches the player to stop hearing it, so only a break-in, a breach or
        /// loss of Cyber Command, and a jamming raid make noise.
        /// </summary>
        public static bool Klaxon(CyberNotice notice) =>
            notice == CyberNotice.IntrusionDetected || notice == CyberNotice.CommandCompromised ||
            notice == CyberNotice.CommandLost || notice == CyberNotice.RaidStarted;

        /// <summary>
        /// The one thing to do next, in the order a watch officer would do it, with the verb and
        /// target the console's [SPACE] shortcut fires. Empty-state advice comes first: a page
        /// that cannot act says what to build. Returns null only when nothing is worth saying.
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
                if (network.Site(command).PatchDone > 0.0) return "C2 PATCH RUNNING · HOLD";
                verb = CyberVerb.Patch;
                target = command;
                return "CYBER COMMAND BREACHED · PATCH IT [2] · OPERATIONS ARE LOCKED";
            }

            // Live incidents, worst first.
            for (int i = 0; i < CyberNetwork.IncidentSlots; i++)
            {
                if (!network.IncidentActive(i)) continue;
                CyberIncident incident = network.Incident(i);
                if (incident.Kind != IncidentKind.Intrusion) continue;
                int slot = incident.Site;
                if (!incident.Held && network.Exists(slot) && !network.Site(slot).Isolated)
                {
                    verb = CyberVerb.Isolate;
                    target = slot;
                    return network.Site(slot).Compromised
                        ? "INTRUSION IN " + Callsign(network, slot) + " · ISOLATE IT [1] BEFORE IT SPREADS"
                        : "INTRUSION LANDING ON " + Callsign(network, slot) + " · ISOLATE [1] OR BAIT [3] NOW";
                }
                if (!incident.Tracing)
                {
                    if (!network.AnyWorking(CyberSiteKind.Sigint))
                        return "INTRUSION HELD · A SIG POST WOULD LET YOU TRACE IT HOME";
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
                    network.AnyWorking(CyberSiteKind.Sigint))
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
                if (incident.Kind == IncidentKind.Probe && !network.SigintCovers(incident.X, incident.Z, now))
                    return "RECON PROBE ON " + Callsign(network, incident.Site) +
                           " · NO SIG EAR ON IT; YOUR EMITTERS WILL BE EXPOSED";
            }

            // Then the housekeeping the network needs.
            for (int slot = 0; slot < CyberNetwork.SlotCount; slot++)
            {
                CyberSite site = network.Site(slot);
                if (!site.Compromised || site.Lost || site.PatchDone > 0.0) continue;
                verb = CyberVerb.Patch;
                target = slot;
                return Callsign(network, slot) + " IS COMPROMISED · PATCH IT [2]";
            }
            for (int slot = 0; slot < CyberNetwork.SlotCount; slot++)
            {
                if (!network.Exists(slot) || !network.Site(slot).Isolated) continue;
                verb = CyberVerb.Isolate;
                target = slot;
                return Callsign(network, slot) + " IS ISOLATED AND IDLE · REJOIN IT [1], IT IS FREE";
            }
            for (int slot = 0; slot < CyberNetwork.SlotCount; slot++)
            {
                if (!network.Online(slot) || network.OnNet(slot)) continue;
                return Callsign(network, slot) + " IS OFF-NET · MOVE IT TOWARD AN AIRBASE OR ADD A RELAY MAST";
            }
            for (int slot = 0; slot < CyberNetwork.SlotCount; slot++)
            {
                if (!network.Exists(slot) || !network.Site(slot).Down) continue;
                return Callsign(network, slot) + " IS DOWN · ITS AIRBASE BUILDING IS DESTROYED UNTIL REPAIRED";
            }
            if (!network.AnyWorking(CyberSiteKind.Sigint))
                return "NO SIG POST · IT BLOCKS RECON PROBES AND IS THE ONLY WAY TO TRACE";
            if (!network.AnyWorking(CyberSiteKind.Jammer))
                return "NO JAMMER · ONE SHIELDS FRIENDLIES FROM RADAR MISSILES AND BACKS OPERATIONS";
            if (!network.AnyWorking(CyberSiteKind.EarlyWarning))
                return "NO EARLY-WARNING RADAR · ONE FEEDS HOSTILE AIR INTO YOUR PICTURE";
            if (network.Stats().Congested)
                return "BANDWIDTH CONGESTED · ADD A RELAY MAST OR SCRAP A DRAW";
            if (network.AnyFoothold(now))
                return "FOOTHOLD OPEN · OPERATIONS ARE CHEAPER AND NEED NO JAMMER IN REACH";
            return "NETWORK HOLDING · " + network.Defended + " DEFENDED / " + network.Breached + " BREACHED";
        }

        public static string Seconds(double seconds) =>
            Math.Max(0, (int)Math.Ceiling(seconds)).ToString(Invariant) + "s";
    }
}
