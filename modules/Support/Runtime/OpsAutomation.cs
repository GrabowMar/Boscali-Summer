using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using BoscaliSummer.Modules.Support.Presentation;
using BoscaliSummer.Modules.Support.Presentation.Fronts;
using NuclearOption.Networking;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>Hooks for the unattended in-game OPS wiring check (<c>tests/ingame/ops-panels.json</c>): open a tab the way the
    /// bezel press does, send a verb through the same <see cref="SupportManager"/> calls the buttons use, and read back what the
    /// host mirrored. Dev tooling: nothing here runs unless called.</summary>
    public static class OpsAutomation
    {
        /// <summary>Maximize the map, select the OPS screen and latch <c>tab</c> (1 FRONTS, 2 PERKS).</summary>
        public static Dictionary<string, object> Open(Dictionary<string, object> args)
        {
            CallsPanel panel = Find<CallsPanel>();
            if (panel == null) return Fail("Open", "no OPS panel in this scene");
            panel.OpenForAutomation((OpsTab)Mathf.Clamp((int)Number(args, "tab", 1f), 1, 3));
            return Readout(null);
        }

        /// <summary>Open the front window the OPEN button opens (<c>front</c> 0 SPACE, 1 CYBER, 2 SOF; <c>close</c> true shuts it).</summary>
        public static Dictionary<string, object> Window(Dictionary<string, object> args)
        {
            FrontController fronts = Find<FrontController>();
            if (fronts == null) return Fail("Window", "no front controller in this scene");
            if (Number(args, "close", 0f) > 0.5f) fronts.Close();
            else fronts.Open((Front)Mathf.Clamp((int)Number(args, "front", 0f), 0, 2));
            return Readout(null);
        }

        /// <summary>Send one verb: <c>hop</c> (first unheld CYBER node, 0 when none), <c>directive</c> (<c>front</c>, <c>directive</c> id), <c>queue</c> / <c>donate</c> (<c>front</c>, <c>programme</c>, <c>amount</c>),
        /// <c>scan</c> (the RECON PASS perk: arm, then the map pick), <c>sync</c> (all mirrors). The verdict arrives as spoken words a frame or two later.</summary>
        public static Dictionary<string, object> Verb(Dictionary<string, object> args)
        {
            SupportManager m = SupportManager.Active;
            if (m == null) return Fail("Verb", "no support manager");
            string verb = Text(args, "verb") ?? "";
            if (!replyHooked) { m.SpaceReplied += r => { lastOutcome = r.Outcome; lastCharged = r.Charged; }; replyHooked = true; }
            int request = 0;
            switch (verb)
            {
                case "hop":
                    spendMark = m.LocalAllocation;
                    int hopTo = ReachableNode(m.CyberMirror.State);
                    if (hopTo != 0) request = m.CyberHop(hopTo);
                    break; // request 0 = no node in reach (game state, not wiring)
                case "recon": // SOF RECON with the first ready team this pilot may order, at the first SPACE scan contact (a ground unit, so land)
                    spendMark = m.LocalAllocation;
                    request = Recon(m);
                    break;
                case "directive":
                    request = m.FrontSetDirective((Front)Mathf.Clamp((int)Number(args, "front", 0f), 0, 2), (FrontDirective)Mathf.Clamp((int)Number(args, "directive", 1f), 0, 6));
                    break;
                case "queue":
                    request = m.FrontQueue((Front)Mathf.Clamp((int)Number(args, "front", 0f), 0, 2), (ProgrammeId)Mathf.Clamp((int)Number(args, "programme", 0f), 0, FrontRules.Programmes.Length - 1));
                    break;
                case "donate":
                    spendMark = m.LocalAllocation;
                    request = m.FrontDonate((Front)Mathf.Clamp((int)Number(args, "front", 0f), 0, 2), (ProgrammeId)Mathf.Clamp((int)Number(args, "programme", 0f), 0, FrontRules.Programmes.Length - 1),
                        (int)Number(args, "amount", 25f));
                    break;
                case "sync":
                    m.FrontFeed.Want(true); m.CyberFeed.Want(true); m.SofFeed.Want(true); m.OpsFeed.Want(true);
                    request = -1;
                    break;
                case "scan": spendMark = m.LocalAllocation; request = Scan(m, Arg(args, "unitUnit") as Unit, Text(args, "aim") == "footprint") ? -1 : 0; break;
                case "strict": Strict = true; request = -1; break;
                case "readiness": ReadinessFloor = (int)Number(args, "floor", 5f); request = -1; break; // TEST ONLY: opens the front gate for the sim
                case "rod": request = Fire(m, Text(args, "action") == "prsm" ? SupportActionId.Prsm : SupportActionId.Artillery); break;
                case "perk": request = Perk(m, Text(args, "action") ?? ""); break; // the protocol 41 perks: overwatch, starfall, siphon, direct (see tests/ingame/ops-abilities.json)
                case "mark":
                    if (m.SpaceMirror.State.Contacts.Count > 0) request = m.SpaceMark(m.SpaceMirror.State.Contacts[0].Id);
                    break;
                case "send":
                    var marks = new List<int>();
                    foreach (var mk in m.SpaceMirror.State.Marks) if (marks.Count < 6) marks.Add(mk.Id);
                    if (marks.Count > 0) request = m.SpaceSend(marks.ToArray());
                    break;
                case "claim":
                    CallsController calls = Find<CallsController>();
                    if (calls != null && m.SpaceMirror.State.Posts.Count > 0)
                    {
                        int post = m.SpaceMirror.State.Posts[0].CallId;
                        calls.PressTasked(post); calls.PressTasked(post); // arm, then execute: the card's two presses
                        request = post;
                    }
                    break;
                case "cruise": // 20 km off the nose: the scan's contacts are long gone by this point of the run
                    if (GameManager.GetLocalAircraft(out Aircraft ac) && ac != null)
                        request = m.RequestAt(SupportActionId.Cruise, (ac.transform.position + ac.transform.forward * 20000f).ToGlobalPosition());
                    break;
                case "steer": request = Steer(m) ? 1 : 0; break;
#if !PUBLIC_RELEASE
                case "relocate": request = Relocate(m, Mathf.Clamp((int)Number(args, "bird", 1f), 0, 2), Number(args, "range", 0f)); break; // own bird over an enemy mover (tests/ingame/ops-overwatch.json)
#endif
                default: return Fail("Verb", "unknown verb '" + verb + "'");
            }
            Dictionary<string, object> r = Readout(m);
            r["request"] = request;
            return r;
        }

        /// <summary>TEST ONLY (set by the sim's <c>readiness</c> verb): perks of rung <= this pass the readiness gate and every qualification gate (the allocation is still spent). 0 = off.</summary>
        internal static int ReadinessFloor;
        private static float spendMark;
        private static bool replyHooked;
        /// <summary>TEST ONLY (the sim's <c>strict</c> verb): ignores the dev BypassRequirements config for this run, so a solo pilot meets every real gate and price.</summary>
        internal static bool Strict;
        private static int lastOutcome = -1, lastCharged;
        private static uint rodUnit;

        /// <summary>Fire <paramref name="id"/> 100 m off the first scan contact (so the MARK snap has to pull it back) and remember that contact's unit.</summary>
        private static int Fire(SupportManager m, SupportActionId id)
        {
            var contacts = m.SpaceMirror.State.Contacts;
            if (contacts.Count == 0) return 0;
            FeedContact c = contacts[0];
            rodUnit = c.UnitId;
            spendMark = m.LocalAllocation;
            GlobalPosition aim = new GlobalPosition(c.X + 100f, 0f, c.Z);
            return m.RequestAt(id, aim);
        }

        private static int directSlot = -1;

        /// <summary>
        /// The protocol 41 perks through the same RequestAt path a button press takes. <c>overwatch</c> aims at the first moving SPACE contact (else the first contact), <c>starfall</c> 100 m off the first contact like the rod,
        /// <c>siphon</c> at the own aircraft (the host ignores the aim), <c>direct</c> at the first SOF target the faction can see (an anchor first), with the first ready team this pilot may order remembered for the readout.
        /// </summary>
        private static int Perk(SupportManager m, string name)
        {
            spendMark = m.LocalAllocation;
            switch (name)
            {
                case "overwatch": // a live enemy mover the RADAR bird covers, else its nadir (the host then answers NO MOVING CONTACTS, which still proves the perk ran)
                    return m.RequestAt(SupportActionId.Overwatch, MoverInFootprint(m, 1));
                case "starfall": // a contact the KINETIC bird covers, else (or when that contact is past the pilot's 30 km support range) its nadir
                {
                    GlobalPosition at = InFootprint(m, 2, false);
                    GlobalPosition self = GameManager.GetLocalAircraft(out Aircraft pilot) && pilot != null ? pilot.transform.position.ToGlobalPosition() : at;
                    if (Vector2.Distance(new Vector2(at.x, at.z), new Vector2(self.x, self.z)) > 28000f) at = Nadir(m, 2);
#if !PUBLIC_RELEASE
                    NoteLine(at);
#endif
                    return m.RequestAt(SupportActionId.Starfall, at);
                }
                case "siphon": // the aim is ignored; the pilot may be down by now
                    return m.RequestAt(SupportActionId.Siphon, GameManager.GetLocalAircraft(out Aircraft own) && own != null ? own.transform.position.ToGlobalPosition() : Nadir(m, 1));
                case "direct":
                {
                    directSlot = -1;
                    foreach (SofTeamRow t in m.SofMirror.State.Teams)
                        if (t.State == TeamState.Ready && (t.Mine || string.IsNullOrEmpty(t.Controller))) { directSlot = t.Slot; break; }
                    SofTargetRow target = default;
                    bool found = false;
                    foreach (SofTargetRow t in m.SofMirror.State.Targets)
                    {
                        if (t.Kind != TargetKind.Anchor && t.Kind != TargetKind.Ground) continue;
                        if (!found || (t.Kind == TargetKind.Anchor && target.Kind != TargetKind.Anchor)) { target = t; found = true; }
                    }
                    return found ? m.RequestAt(SupportActionId.DirectAction, new GlobalPosition(target.X, 0f, target.Z)) : 0;
                }
                default: return 0;
            }
        }

        /// <summary>RADAR SCAN over the nearest airbase <paramref name="self"/>'s faction does not hold: the CAP press arms it, then the armed map pick
        /// (a private callback a right-click would invoke) fires it, so the whole CALL path runs.</summary>
        private static bool Scan(SupportManager m, Unit self, bool footprint = false)
        {
            CallsController calls = Find<CallsController>();
            if (footprint && calls != null) return Pick(m, calls, Nadir(m, 1)); // `aim: footprint`: at the RADAR bird's parked point, always inside its footprint
            if (calls == null || self == null || FactionRegistry.airbaseLookup == null) return false;
            Airbase best = null; float bestD = float.MaxValue;
            foreach (Airbase a in FactionRegistry.airbaseLookup.Values)
            {
                if (a == null || a.center == null || a.CurrentHQ == null || a.CurrentHQ == self.NetworkHQ) continue;
                float d = (a.center.position - self.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = a; }
            }
            if (best == null) return false;
            return Pick(m, calls, best.center.position.ToGlobalPosition());
        }

        /// <summary>RECON PASS the way the CAP press does it: arm, then the armed map pick at <paramref name="at"/>.</summary>
        private static bool Pick(SupportManager m, CallsController calls, GlobalPosition at)
        {
            calls.Press(SupportActionId.Recon);
            var pick = typeof(SupportManager).GetField("localPick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(m) as System.Action<GlobalPosition>;
            if (pick == null) return false;
            pick(at);
            return true;
        }

        /// <summary>World point under own SPACE bird <paramref name="k"/> (0 OPTICAL, 1 RADAR, 2 KINETIC) as the OPS mirror has it: inside that bird's footprint.</summary>
        private static GlobalPosition Nadir(SupportManager m, int k)
        {
            GeoBird g = m.OpsMirror.State.Geo[k];
            float now = SupportManager.MissionNow();
            Vector2 span = BoscaliSummer.Core.Game.TheaterFrame.Resolve();
            var frame = new MapFrame(span.x, span.y);
            return new GlobalPosition(frame.X(g.U(now)), 0f, frame.Z(g.V(now)));
        }

        /// <summary>A moving enemy ground vehicle (>= 4 m/s, OVERWATCH's own threshold) inside own bird <paramref name="k"/>'s footprint, nearest its nadir; else the nadir.</summary>
        private static GlobalPosition MoverInFootprint(SupportManager m, int k)
        {
            GeoBird g = m.OpsMirror.State.Geo[k];
            float now = SupportManager.MissionNow();
            Vector2 span = BoscaliSummer.Core.Game.TheaterFrame.Resolve();
            var frame = new MapFrame(span.x, span.y);
            GlobalPosition nadir = Nadir(m, k), best = nadir;
            FactionHQ mine = GameManager.GetLocalPlayer(out Player p) && p != null ? p.HQ : null;
            double bestD = double.MaxValue;
            var units = UnitRegistry.allUnits;
            for (int i = 0; units != null && i < units.Count; i++)
            {
                Unit u = units[i];
                if (!(u is GroundVehicle) || u.disabled || u.NetworkHQ == null || u.NetworkHQ == mine || Mathf.Abs(u.speed) < 4f) continue;
                GlobalPosition q = u.transform.position.ToGlobalPosition();
                if (!g.Covers(now, frame.U(q.x), frame.V(q.z), GeoSpace.Reach[k])) continue;
                double dx = q.x - nadir.x, dz = q.z - nadir.z, d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = q; }
            }
            return best;
        }

        /// <summary>A scan contact inside own bird <paramref name="k"/>'s footprint (moving ones first when <paramref name="moving"/>), else that bird's nadir.</summary>
        private static GlobalPosition InFootprint(SupportManager m, int k, bool moving)
        {
            GeoBird g = m.OpsMirror.State.Geo[k];
            float now = SupportManager.MissionNow();
            Vector2 span = BoscaliSummer.Core.Game.TheaterFrame.Resolve();
            var frame = new MapFrame(span.x, span.y);
            FeedContact best = default; bool found = false;
            foreach (FeedContact c in m.SpaceMirror.State.Contacts)
            {
                if (!g.Covers(now, frame.U(c.X), frame.V(c.Z), GeoSpace.Reach[k])) continue;
                if (!found || (moving && c.Moving && !best.Moving)) { best = c; found = true; }
            }
            return found ? new GlobalPosition(best.X, 0f, best.Z) : Nadir(m, k);
        }

        /// <summary>The first node the NETWORK room shows IN REACH (FrontViews' rule): from the own session's held chain, or with no session from a live EW truck.</summary>
        private static int ReachableNode(CyberStateData s)
        {
            CyberIntrusionRow mine = default;
            bool have = false;
            foreach (CyberIntrusionRow x in s.Intrusions) if (x.Own) { mine = x; have = true; break; }
            var trucks = new List<EwSource>(4);
            var chain = new List<CyberNode>(4);
            int ordinal = 0;
            foreach (CyberAnchorRow a in s.Anchors)
            {
                if (a.Kind != AnchorKind.EwTruck) continue;
                if (a.Health != AnchorHealth.Down) trucks.Add(new EwSource(ordinal, a.X, a.Z, AnchorRules.Reach(a.Health)));
                ordinal++;
            }
            if (have && mine.Held != null)
                foreach (int id in mine.Held)
                    foreach (CyberNodeRow r in s.Nodes) if (r.Id == id) chain.Add(new CyberNode(r.Id, r.Kind, r.X, r.Z, 0u, 0f, 0));
            foreach (CyberNodeRow r in s.Nodes)
            {
                if (r.Held || r.Hopping) continue;
                var cn = new CyberNode(r.Id, r.Kind, r.X, r.Z, 0u, 0f, 0);
                if (have ? CyberGraph.Reachable(cn, null, chain, out _, out _) : CyberGraph.Reachable(cn, trucks, null, out _, out _)) return r.Id;
            }
            return 0;
        }

        /// <summary>RECON with the first READY team this pilot may order (own or autonomous), at the first SPACE scan contact: the SHADOW map click's path.</summary>
        private static int Recon(SupportManager m)
        {
            if (!haveContact) return 0; // the SPACE feed streams contacts only while ORBIT is watched: use the last one a readout saw
            foreach (SofTeamRow t in m.SofMirror.State.Teams)
            {
                if (t.State != TeamState.Ready || (!t.Mine && !string.IsNullOrEmpty(t.Controller))) continue;
                reconSlot = t.Slot;
                return m.SofMission(t.Slot, MissionKind.Recon, 0, contactX, contactZ);
            }
            return 0;
        }
        private static int reconSlot = -1;
        private static bool haveContact;
        private static float contactX, contactZ;

        /// <summary>STEER on the TASKED tab, then the armed map pick 5 km off the nose, as the right-click would.</summary>
        private static bool Steer(SupportManager m)
        {
            CallsController calls = Find<CallsController>();
            if (calls == null || !GameManager.GetLocalAircraft(out Aircraft own) || own == null) return false;
            calls.SteerCruise();
            var pick = typeof(SupportManager).GetField("localPick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(m) as System.Action<GlobalPosition>;
            if (pick == null) return false;
            pick((own.transform.position + own.transform.forward * 5000f).ToGlobalPosition());
            return true;
        }

        public static Dictionary<string, object> Read(Dictionary<string, object> args) => Readout(null);

        private static int CountMovers(SupportManager m)
        {
            int n = 0;
            foreach (FeedContact c in m.SpaceMirror.State.Contacts) if (c.Moving) n++;
            return n;
        }

        private static Dictionary<string, object> Readout(SupportManager m)
        {
            m = m ?? SupportManager.Active;
            CallsPanel panel = Find<CallsPanel>();
            CallsController calls = Find<CallsController>();
            var r = new Dictionary<string, object> { { "ok", true }, { "manager", m != null }, { "panel", panel != null }, { "controller", calls != null } };
            if (panel != null)
            {
                r["installed"] = panel.Installed;
                r["screenActive"] = panel.ScreenActive;
                r["tab"] = (int)panel.Tab;
                r["consoleBuilt"] = panel.ConsoleRoot != null;
                OpsPageView pv = panel.View;
                r["perksReady"] = pv.PerksReady;
                r["perksTotal"] = pv.PerksTotal;
                r["frontsAhead"] = pv.FrontsAhead;
                r["alerts"] = pv.Alerts;
                r["taskedRows"] = pv.Tasked.Count;
                var cards = new List<object>(3);
                foreach (FrontCardView c in pv.Cards)
                    cards.Add(new Dictionary<string, object> { { "front", (int)c.Front }, { "readiness", c.Readiness }, { "superiority", c.Superiority }, { "directive", c.Directive }, { "building", c.Building }, { "alert", c.Alert } });
                r["cards"] = cards;
            }
            FrontController fronts = Find<FrontController>();
            if (fronts != null)
            {
                r["windowOpen"] = fronts.IsOpen;
                r["windowOpenN"] = fronts.IsOpen ? 1 : 0;
                r["windowFront"] = (int)fronts.Current;
                r["frontWords"] = fronts.LastWords;
            }
            if (calls != null) { r["words"] = calls.LastWords; r["wordsAt"] = calls.LastWordsAt; }
            if (m == null) return r;
            r["online"] = m.Online;
            r["allocation"] = m.LocalAllocation;
            r["spaceKnown"] = m.SpaceMirror.Known;
            r["spaceActive"] = m.SpaceMirror.State.Active;
            r["cyberKnown"] = m.CyberMirror.Known;
            r["cyberActive"] = m.CyberMirror.State.Active;
            r["cyberNodes"] = m.CyberMirror.State.Nodes.Count;
            r["cyberIntrusions"] = m.CyberMirror.State.Intrusions.Count;
            r["sofKnown"] = m.SofMirror.Known;
            r["sofActive"] = m.SofMirror.State.Active;
            r["sofTeams"] = m.SofMirror.State.Teams.Count;
            int ready = 0;
            foreach (SofTeamRow t in m.SofMirror.State.Teams)
            {
                if (t.State == TeamState.Ready) ready++;
                if (t.Slot == reconSlot) { r["reconState"] = (int)t.State; r["reconMission"] = (int)t.Mission; r["reconMine"] = t.Mine ? 1 : 0; }
                if (t.Slot == directSlot) { r["directState"] = (int)t.State; r["directMission"] = (int)t.Mission; }
            }
            r["sofReady"] = ready;
            int myHeld = 0, hopping = 0;
            foreach (CyberIntrusionRow x in m.CyberMirror.State.Intrusions)
                if (x.Own) { myHeld = x.Held != null ? x.Held.Length : 0; hopping = x.Phase == IntrusionPhase.Hopping ? 1 : 0; }
            r["cyberMyHeld"] = myHeld;
            r["cyberHopping"] = hopping;
            float nearest = float.MaxValue;
            foreach (CyberAnchorRow a in m.CyberMirror.State.Anchors)
                if (a.Kind == AnchorKind.EwTruck && a.Health != AnchorHealth.Down)
                    foreach (CyberNodeRow n in m.CyberMirror.State.Nodes)
                        nearest = Mathf.Min(nearest, Mathf.Sqrt((a.X - n.X) * (a.X - n.X) + (a.Z - n.Z) * (a.Z - n.Z)));
            r["cyberNearestKm"] = nearest < float.MaxValue ? Mathf.RoundToInt(nearest / 1000f) : -1;
            r["cyberReachable"] = ReachableNode(m.CyberMirror.State) != 0 ? 1 : 0;
            if (m.SpaceMirror.State.Contacts.Count > 0) { haveContact = true; contactX = m.SpaceMirror.State.Contacts[0].X; contactZ = m.SpaceMirror.State.Contacts[0].Z; }
            else if (!haveContact && m.CyberMirror.State.Nodes.Count > 0) { haveContact = true; contactX = m.CyberMirror.State.Nodes[0].X; contactZ = m.CyberMirror.State.Nodes[0].Z; } // an enemy installation: land
            r["sofCamps"] = m.SofMirror.State.Camps.Count;
            r["opsKnown"] = m.OpsMirror.Known;
            r["opsActive"] = m.OpsMirror.State.Active;
            r["opsRows"] = m.OpsMirror.State.Rows.Count;
            r["frontKnown"] = m.FrontMirror.Known;
            if (m.FrontMirror.Known)
                for (int f = 0; f < FrontRules.FrontCount; f++)
                {
                    FrontRow row = m.FrontMirror.State.Fronts[f];
                    r["front" + f + "Readiness"] = (int)row.Readiness;
                    r["front" + f + "Directive"] = (int)row.Directive;
                    r["front" + f + "Queue"] = row.Queue.Count;
                    r["front" + f + "Budget"] = (int)row.Budget;
                    r["front" + f + "Power"] = (int)System.Math.Round(FrontSuperiority.Power(row.Superiority) * 100f);
                }
            r["airborne"] = GameManager.GetLocalAircraft(out Aircraft own) && own != null && !own.disabled ? 1 : 0;
            r["spentSinceVerb"] = spendMark > 0f ? spendMark - m.LocalAllocation : 0f;
            r["lastReplyAction"] = (int)m.LastReplyAction; r["lastReplyResult"] = (int)m.LastReplyResult; r["lastReplyContacts"] = m.LastReplyContacts;
            r["spaceMovers"] = CountMovers(m);
            if (rodUnit != 0u)
            {
                bool alive = UnitRegistry.TryGetUnit(new PersistentID { Id = rodUnit }, out Unit tu) && tu != null && !tu.disabled;
                r["rodTargetAlive"] = alive ? 1 : 0;
            }
            if (m.FrontMirror.Known && m.FrontMirror.State.Fronts[0].Queue.Count > 0) r["front0Pct"] = (int)m.FrontMirror.State.Fronts[0].Queue[0].Percent;
            float budgetTotal = 0f;
            if (m.FrontMirror.Known) for (int f = 0; f < FrontRules.FrontCount; f++) budgetTotal += m.FrontMirror.State.Fronts[f].Budget;
            r["budgetTotal"] = (int)budgetTotal;
            r["spaceContacts"] = m.SpaceMirror.State.Contacts.Count;
            r["spaceMarks"] = m.SpaceMirror.State.Marks.Count;
            r["spacePosts"] = m.SpaceMirror.State.Posts.Count;
            r["cruiseFlying"] = m.TryFlyingCruise(out _, out int cruiseLegs) ? 1 : 0;
            r["cruiseLegs"] = cruiseLegs;
#if !PUBLIC_RELEASE
            FollowReadout(m, r);
#endif
            return r;
        }

#if !PUBLIC_RELEASE
        // ---- TEST ONLY: the OVERWATCH and STARFALL proofs (tests/ingame/ops-overwatch.json) ------------------------------------------------

        private static float burnSeconds;
        private static GlobalPosition lineAim, lineFrom;
        private static bool haveLine;
        private static readonly List<Unit> followed = new List<Unit>(24);
        private static readonly Dictionary<uint, Vector2> firstLive = new Dictionary<uint, Vector2>(), firstContact = new Dictionary<uint, Vector2>();

        private static MapFrame Theatre()
        {
            Vector2 span = BoscaliSummer.Core.Game.TheaterFrame.Resolve();
            return new MapFrame(span.x, span.y);
        }

        /// <summary>OVERWATCH's own test (SpaceObservations.Follow): an enemy ground vehicle moving at 4 m/s or more, in the open.</summary>
        private static bool EnemyMover(Unit u, FactionHQ mine) =>
            u is GroundVehicle && !u.disabled && u.NetworkHQ != null && u.NetworkHQ != mine && Mathf.Abs(u.speed) >= 4f &&
            u.LineOfSight(u.transform.position + Vector3.up * 20000f, 1000f);

        /// <summary>
        /// Own bird <paramref name="k"/> burns over the enemy mover nearest it through the ORBIT room's CONFIRM BURN verb (<see cref="SupportManager.RelocateBird"/>), and the SPACE focus
        /// is pinned there (a player's own verb) so the SPACE director leaves a covering bird where it is (and moves any other bird onto the pin). With <paramref name="range"/> &gt; 0 the
        /// bird and the pin go short of the mover instead, at most that far from the own aircraft (a strike perk must land inside the pilot's 30 km support range).
        /// -1 = the bird already covers that point well (no burn), 0 = nothing moves anywhere or no OPS mirror yet; <c>burnSeconds</c> in the readout is the transfer time.
        /// </summary>
        private static int Relocate(SupportManager m, int k, float range)
        {
            burnSeconds = 0f;
            if (!m.OpsMirror.Known) return 0;
            GeoBird g = m.OpsMirror.State.Geo[k];
            float now = SupportManager.MissionNow(), bu = g.U(now), bv = g.V(now), best = float.MaxValue, tu = 0f, tv = 0f;
            MapFrame frame = Theatre();
            FactionHQ mine = GameManager.GetLocalPlayer(out Player p) && p != null ? p.HQ : null;
            var units = UnitRegistry.allUnits;
            for (int i = 0; units != null && i < units.Count; i++)
            {
                if (!EnemyMover(units[i], mine)) continue;
                GlobalPosition q = units[i].transform.position.ToGlobalPosition();
                float u = frame.U(q.x), v = frame.V(q.z), d = GeoBird.Distance(bu, bv, u, v);
                if (d < best) { best = d; tu = u; tv = v; }
            }
            if (best == float.MaxValue) return 0;
            if (range > 0f && GameManager.GetLocalAircraft(out Aircraft own) && own != null)
            {
                GlobalPosition o = own.transform.position.ToGlobalPosition();
                var toward = new Vector2(frame.X(tu) - o.x, frame.Z(tv) - o.z);
                if (toward.magnitude > range) toward = toward.normalized * range;
                tu = frame.U(o.x + toward.x); tv = frame.V(o.z + toward.y);
                best = GeoBird.Distance(bu, bv, tu, tv);
            }
            m.FrontSetFocus(Front.Space, frame.X(tu), frame.Z(tv)); // the director moves any bird that misses the pin onto it
            if (best <= GeoSpace.Reach[k] * 0.6f) return -1; // well inside, so still covered when the perk fires
            burnSeconds = best / GeoBird.Slew;
            return m.RelocateBird(k, tu, tv);
        }

        /// <summary>The STARFALL line as the host walks it: from the own aircraft through <paramref name="aim"/>.</summary>
        private static void NoteLine(GlobalPosition aim)
        {
            lineAim = aim;
            lineFrom = GameManager.GetLocalAircraft(out Aircraft own) && own != null ? own.transform.position.ToGlobalPosition() : new GlobalPosition(aim.x, 0f, aim.z - 1000f);
            haveLine = true;
        }

        /// <summary>
        /// Shuts the OPS window and puts the native free camera side-on to the last STARFALL line: <c>distance</c> m off it, <c>back</c> m behind the aim along it,
        /// <c>height</c> m above the aim's ground (and at least 150 m above the ground under the camera), looking at <c>look</c> m above the aim, <c>fov</c> degrees.
        /// </summary>
        public static Dictionary<string, object> Frame(Dictionary<string, object> args)
        {
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (!haveLine || camera == null) return Fail("Frame", haveLine ? "no camera" : "no STARFALL line yet");
            Find<FrontController>()?.Close();
            if (!SupportTargeting.TryMapPoint(lineAim, out Vector3 ground)) ground = lineAim.ToLocalPosition();
            Vector3 along = lineAim.ToLocalPosition() - lineFrom.ToLocalPosition();
            along.y = 0f;
            along = along.sqrMagnitude > 1f ? along.normalized : Vector3.forward;
            Vector3 eye = ground + Vector3.Cross(Vector3.up, along) * Number(args, "distance", 4000f) - along * Number(args, "back", 0f) + Vector3.up * Number(args, "height", 600f);
            if (SupportTargeting.TryMapPoint(eye.ToGlobalPosition(), out Vector3 under) && eye.y < under.y + 150f) eye.y = under.y + 150f;
            Vector3 look = ground + Vector3.up * Number(args, "look", 0f);
            float fov = Number(args, "fov", 60f);
            camera.SetCameraPosition(eye.ToGlobalPosition(), Quaternion.LookRotation(look - eye));
            camera.cameraVelocity = Vector3.zero;
            camera.allowInputs = false;
            camera.SetDesiredFoV(fov, fov);
            SceneSingleton<DynamicMap>.i?.Minimize();
            Dictionary<string, object> r = Readout(null);
            r["eyeAgl"] = Mathf.RoundToInt(eye.y - ground.y);
            return r;
        }

        /// <summary>
        /// The host's OVERWATCH jobs against the client's SPACE mirror: how many units are followed, how many of them the mirror shows (and as moving), how far each contact
        /// sits from its unit (lag), and how far the units and their contacts have travelled since the first readout that saw them (moved / trail).
        /// </summary>
        private static void FollowReadout(SupportManager m, Dictionary<string, object> r)
        {
            FactionHQ mine = GameManager.GetLocalPlayer(out Player p) && p != null ? p.HQ : null;
            int movers = 0;
            var units = UnitRegistry.allUnits;
            for (int i = 0; units != null && i < units.Count; i++) if (EnemyMover(units[i], mine)) movers++;
            r["enemyMovers"] = movers;
            r["burnSeconds"] = Mathf.RoundToInt(burnSeconds);
            float now = SupportManager.MissionNow();
            for (int k = 0; k < 3; k++) r["bird" + k + "Burning"] = m.OpsMirror.State.Geo[k].Moving(now) ? 1 : 0;
            followed.Clear();
            if (mine != null) m.CopyFollowed(mine, followed);
            int alive = 0, seen = 0, moving = 0;
            float lag = 0f, moved = 0f, trail = 0f;
            foreach (Unit u in followed)
            {
                if (u == null || u.disabled) continue;
                alive++;
                uint id = u.persistentID.Id;
                GlobalPosition q = u.transform.position.ToGlobalPosition();
                var live = new Vector2(q.x, q.z);
                if (!firstLive.TryGetValue(id, out Vector2 start)) firstLive[id] = start = live;
                moved = Mathf.Max(moved, Vector2.Distance(start, live));
                foreach (FeedContact c in m.SpaceMirror.State.Contacts)
                {
                    if (c.UnitId != id) continue;
                    seen++;
                    if (c.Moving) moving++;
                    var at = new Vector2(c.X, c.Z);
                    lag = Mathf.Max(lag, Vector2.Distance(at, live));
                    if (!firstContact.TryGetValue(id, out Vector2 c0)) firstContact[id] = c0 = at;
                    trail = Mathf.Max(trail, Vector2.Distance(c0, at));
                    break;
                }
            }
            r["followed"] = followed.Count;
            r["followedAlive"] = alive;
            r["followedSeen"] = seen;
            r["followedMoving"] = moving;
            r["followedLagM"] = Mathf.RoundToInt(lag);
            r["followedMovedM"] = Mathf.RoundToInt(moved);
            r["followedTrailM"] = Mathf.RoundToInt(trail);
        }
#endif

        /// <summary>Solo acceptance readout (appended): <see cref="Readout"/> plus the host's view of the other faction's fronts, own superiority per front and the window state.</summary>
        public static Dictionary<string, object> SoloRead(Dictionary<string, object> args)
        {
            SupportManager m = SupportManager.Active;
            Dictionary<string, object> r = Readout(m);
            if (m == null) return r;
            if (m.FrontMirror.Known)
            {
                int sup = 0;
                for (int f = 0; f < FrontRules.FrontCount; f++)
                {
                    FrontRow row = m.FrontMirror.State.Fronts[f];
                    r["front" + f + "Sup"] = (int)row.Superiority;
                    r["front" + f + "Pct"] = row.Queue.Count > 0 ? (int)row.Queue[0].Percent : -1;
                    sup += row.Superiority;
                }
                r["supSum"] = sup;
            }
            r["lastOutcome"] = lastOutcome; r["lastCharged"] = lastCharged;
            r["enemyBirdsDown"] = (int)m.OpsMirror.State.EnemyBirdsDown;
            if (FrontService.Active != null && GameManager.GetLocalPlayer(out Player p) && p != null &&
                FrontService.Active.EnemyReadout(p.HQ, out int rd, out int q, out int es, out float hb))
            { r["enemyReadiness"] = rd; r["enemyQueued"] = q; r["enemySup"] = es; r["hostBudget"] = (int)hb; }
            return r;
        }

        // The module runtime root is HideAndDontSave, which Object.FindObjectOfType never returns.
        private static T Find<T>() where T : Object
        {
            T[] all = Resources.FindObjectsOfTypeAll<T>();
            return all.Length > 0 ? all[0] : null;
        }

        private static Dictionary<string, object> Fail(string hook, string error) => Failure("OpsAutomation", hook, error);
    }
}
