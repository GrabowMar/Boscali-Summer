using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Hooks for the two-sided OPS check (<c>tests/ingame/ops-defence.json</c>, protocol 42). The enemy acts through its own host desks as its WATCH OFFICER identity (a real hop on one of our
    /// nodes, a real team walking at our camp); we answer through the same <see cref="SupportManager"/> calls the PURGE and SWEEP buttons send. Dev tooling: nothing here runs unless called.
    /// </summary>
    public static class DefenceAutomation
    {
        private const ulong Enemy = SpaceContacts.WatchOfficerId;
        private const float ExposeMetres = 40000f;
        private const int MaxStamps = 120;
        private static int heldNode, raidSlot = -1, purgedReleases, purgeRequest, sweepRequest;
        private static FactionHQ observedFor;
        private static readonly List<Unit> scratch = new List<Unit>(MaxStamps);

        /// <summary>
        /// One step: <c>ai_off</c> (AI FACTIONS off, so the enemy director neither drops our hold nor recalls the raid), <c>expose</c> (the enemy sees our ground units within 40 km of its EW trucks),
        /// <c>breach</c> (the enemy hops our most valuable node in reach), <c>purge</c> (our PURGE), <c>raid</c> (an enemy team 2 km from our camp, walking in), <c>sweep</c> (our SWEEP).
        /// </summary>
        public static Dictionary<string, object> Step(Dictionary<string, object> args)
        {
            SupportManager m = SupportManager.Active;
            if (m == null || m.Cyber == null || m.Sof == null) return Fail("Step", "no support manager or no CYBER / SOF service");
            if (!GameManager.GetLocalPlayer<Player>(out Player me) || me == null || me.HQ == null) return Fail("Step", "no local player");
            FactionHQ us = me.HQ, them = m.Cyber.EnemyOf(us);
            if (them == null) return Fail("Step", "no enemy faction with a CYBER desk");
            if (observedFor != them)
            {
                observedFor = them;
                m.Cyber.Observed += (owner, e, victim) => { if (owner == observedFor && e.Kind == CyberEventKind.Released && e.Reason == IntrusionEnd.Purged) purgedReleases++; };
            }
            var r = new Dictionary<string, object> { { "ok", true } };
            switch (Text(args, "verb") ?? "")
            {
                case "ai_off":
                    m.Settings.AiFactionsEnabled.Value = false;
                    break;
                case "expose":
                    r["stamped"] = Expose(m, us, them);
                    break;
                case "breach":
                    heldNode = Breach(m, us, them, out string kind);
                    r["node"] = heldNode; r["kind"] = kind;
                    break;
                case "purge":
                    purgeRequest = m.CyberPurge();
                    r["request"] = purgeRequest;
                    break;
                case "raid":
                    raidSlot = Raid(m, us, them, out string why);
                    r["slot"] = raidSlot; r["why"] = why;
                    break;
                case "sweep":
                    sweepRequest = m.SofSweep();
                    r["request"] = sweepRequest;
                    break;
                default:
                    return Fail("Step", "unknown verb");
            }
            return r;
        }

        /// <summary>What both sides know right now: host desks, our mirrors, and every HQ radio line sent since the scene began.</summary>
        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            SupportManager m = SupportManager.Active;
            if (m == null || m.Cyber == null || m.Sof == null) return Fail("Read", "no support manager");
            if (!GameManager.GetLocalPlayer<Player>(out Player me) || me == null || me.HQ == null) return Fail("Read", "no local player");
            FactionHQ us = me.HQ, them = m.Cyber.EnemyOf(us);
            float now = SupportManager.MissionNow();
            var r = new Dictionary<string, object> { { "ok", true }, { "heldNode", heldNode }, { "purgedReleases", purgedReleases }, { "raidSlot", raidSlot } };
            if (m.Cyber.TryDesk(us, out CyberDesk ours))
            {
                r["hostBreaches"] = ours.Breaches.Count;
                r["purgeRunning"] = ours.PurgeEndsAt > now ? 1 : 0;
                r["purgeCooling"] = ours.PurgeReadyAt > now ? 1 : 0;
            }
            CyberMirror cm = m.CyberMirror;
            r["mirrorBreaches"] = cm.Known && cm.State.Active ? cm.State.Breaches.Count : -1;
            if (them != null && m.Cyber.TryDesk(them, out CyberDesk theirs))
            {
                CyberIntrusion x = theirs.Network.Of(Enemy);
                r["enemyHolds"] = x != null ? x.HeldCount : 0;
                r["enemyHoldsOurNode"] = x != null && heldNode > 0 && x.Holds(heldNode) ? 1 : 0;
                r["enemyTrace"] = x != null ? Mathf.RoundToInt(x.Trace) : -1;
            }
            if (m.Sof.TryDesk(us, out SofDesk ourSof))
            {
                r["hostHostiles"] = ourSof.Hostiles.Count;
                r["sweepRunning"] = ourSof.SweepEndsAt > now ? 1 : 0;
                r["sweepCooling"] = ourSof.SweepReadyAt > now ? 1 : 0;
            }
            SofMirror sm = m.SofMirror;
            int near = 0, hunted = 0;
            if (sm.Known && sm.State.Active) foreach (SofEnemyRow e in sm.State.Enemies) { if (e.Near) near++; if (e.Hunted) hunted++; }
            r["mirrorNear"] = near; r["mirrorHunted"] = hunted;
            if (them != null && raidSlot >= 0 && m.Sof.TryDesk(them, out SofDesk theirSof))
            {
                SofTeam t = theirSof.Teams[raidSlot];
                r["raidActive"] = t.Active ? 1 : 0;
                r["raidState"] = t.State.ToString();
                r["raidHunted"] = t.HuntedUntil > now ? 1 : 0;
                r["raidExposure"] = Mathf.RoundToInt(t.Exposure);
            }
            var said = new List<object>(RodGuard.Said.Count);
            foreach (string s in RodGuard.Said) said.Add(s);
            r["said"] = said;
            r["toldBreach"] = Told(us, "NETWORK BREACH");
            r["toldPurged"] = Told(us, "PURGE COMPLETE");
            r["toldHostile"] = Told(us, "HOSTILE SOF TEAM DETECTED");
            r["toldQrf"] = Told(us, "QRF IN CONTACT");
            r["toldEnemyPurged"] = Told(them, "NODE PURGED", true);
            r["toldEnemyQrf"] = Told(them, "ENEMY QRF ON", true);
            return r;
        }

        /// <summary>1 when a line containing <paramref name="words"/> went to <paramref name="hq"/> (delivered; or only attempted when <paramref name="attemptOk"/>: an AI faction has nobody to hear it).</summary>
        private static int Told(FactionHQ hq, string words, bool attemptOk = false)
        {
            if (hq == null) return 0;
            foreach (string s in RodGuard.Said)
                if (s.StartsWith(hq.name + "|", System.StringComparison.Ordinal) && s.Contains(words) && (attemptOk || s.EndsWith("|sent", System.StringComparison.Ordinal))) return 1;
            return 0;
        }

        /// <summary>A real sighting of our ground units within 40 km of the enemy's EW trucks, then an early node refresh, so its desk can list our radars and SAM nets as it would after a real scan.</summary>
        private static int Expose(SupportManager m, FactionHQ us, FactionHQ them)
        {
            if (!m.Cyber.TryDesk(them, out CyberDesk theirs)) return 0;
            var trucks = new List<EwSource>(AnchorRules.MaxTrucks);
            theirs.Anchors.CopyTrucks(trucks);
            scratch.Clear();
            List<Unit> all = UnitRegistry.allUnits;
            for (int i = 0; all != null && i < all.Count && scratch.Count < MaxStamps; i++)
            {
                Unit u = all[i];
                if (u == null || u.disabled || u.NetworkHQ != us || !(u is GroundVehicle)) continue;
                GlobalPosition p = u.transform.position.ToGlobalPosition();
                foreach (EwSource t in trucks)
                    if (CyberGraph.InReach(t.X, t.Z, (float)p.x, (float)p.z, ExposeMetres)) { scratch.Add(u); break; }
            }
            foreach (Unit u in scratch)
            {
                try { them.RpcUpdateTrackingInfo(u.persistentID); }
                catch (System.Exception e) { Plugin.Logger?.LogDebug("[Support.Sim] expose refused: " + e.Message); }
            }
            m.Cyber.RefreshNodesSoon(them);
            return scratch.Count;
        }

        /// <summary>The enemy hops the most valuable of our nodes it can see and reach (SAM C2, RADAR, UPLINK, RELAY, DATA CENTER). 0 when none is in reach.</summary>
        private static int Breach(SupportManager m, FactionHQ us, FactionHQ them, out string kind)
        {
            kind = "";
            if (!m.Cyber.TryDesk(them, out CyberDesk theirs)) return 0;
            int victim = m.FactionKeyOf(us);
            var order = new List<CyberNode>();
            foreach (CyberNode n in theirs.Visible) if (n.Victim == victim) order.Add(n);
            order.Sort((a, b) => CyberWatchBrain.Priority(b.Kind).CompareTo(CyberWatchBrain.Priority(a.Kind)));
            foreach (CyberNode n in order)
            {
                CyberResult hop = theirs.Hop(Enemy, n.Id);
                Plugin.Logger?.LogInfo("[Support.Sim] enemy hop " + n.Kind + " #" + n.Id + ": " + hop.Outcome);
                if (!hop.Ok) continue;
                kind = n.Kind.ToString();
                return n.Id;
            }
            kind = "none of " + order.Count + " visible";
            return 0;
        }

        /// <summary>An enemy team raised at once, standing 2 km east of our best camp (else our first airbase) and walking in. Its slot, or -1.</summary>
        private static int Raid(SupportManager m, FactionHQ us, FactionHQ them, out string why)
        {
            why = "";
            if (!m.Sof.TryDesk(them, out SofDesk theirs)) { why = "enemy has no SOF desk"; return -1; }
            float x, z;
            if (!(m.Sof.TryDesk(us, out SofDesk ours) && ours.Camps.TryBest(out _, out x, out z, out _)) && !FirstAirbase(us, out x, out z)) { why = "we have no camp and no airbase"; return -1; }
            SofResult raised = theirs.Raise(Enemy, SofRules.MaxTeams);
            if (!raised.Ok) { why = "raise: " + raised.Outcome; return -1; }
            SofTeam t = theirs.Teams[raised.Slot];
            t.State = TeamState.Ready; t.RaiseEndsAt = SupportManager.MissionNow();
            t.X = x + 2000f; t.Z = z; t.HomeX = t.X; t.HomeZ = t.Z;
            SofResult go = theirs.Divert(Enemy, raised.Slot, x, z);
            why = "divert: " + go.Outcome;
            return raised.Slot;
        }

        private static bool FirstAirbase(FactionHQ hq, out float x, out float z)
        {
            x = z = 0f;
            if (FactionRegistry.airbaseLookup == null) return false;
            foreach (Airbase a in FactionRegistry.airbaseLookup.Values)
            {
                if (a == null || a.disabled || a.CurrentHQ != hq || a.center == null) continue;
                GlobalPosition p = a.center.position.ToGlobalPosition();
                x = (float)p.x; z = (float)p.z;
                return true;
            }
            return false;
        }

        private static Dictionary<string, object> Fail(string hook, string error) => Failure("DefenceAutomation", hook, error);
    }
}
