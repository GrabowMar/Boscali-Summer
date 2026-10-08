using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Fronts;
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
            panel.OpenForAutomation((OpsTab)Mathf.Clamp((int)Number(args, "tab", 1f), 1, 2));
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
                    foreach (CyberNodeRow n in m.CyberMirror.State.Nodes)
                        if (!n.Held && !n.Hopping) { request = m.CyberHop(n.Id); break; }
                    break; // request 0 = no node in reach (game state, not wiring)
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
                case "scan": spendMark = m.LocalAllocation; request = Scan(m, Arg(args, "unitUnit") as Unit) ? -1 : 0; break;
                case "strict": Strict = true; request = -1; break;
                case "readiness": ReadinessFloor = (int)Number(args, "floor", 5f); request = -1; break; // TEST ONLY: opens the front gate for the sim
                case "rod": request = Fire(m, Text(args, "action") == "prsm" ? SupportActionId.Prsm : SupportActionId.Artillery); break;
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

        /// <summary>RADAR SCAN over the nearest airbase <paramref name="self"/>'s faction does not hold: the CAP press arms it, then the armed map pick
        /// (a private callback a right-click would invoke) fires it, so the whole CALL path runs.</summary>
        private static bool Scan(SupportManager m, Unit self)
        {
            CallsController calls = Find<CallsController>();
            if (calls == null || self == null || FactionRegistry.airbaseLookup == null) return false;
            Airbase best = null; float bestD = float.MaxValue;
            foreach (Airbase a in FactionRegistry.airbaseLookup.Values)
            {
                if (a == null || a.center == null || a.CurrentHQ == null || a.CurrentHQ == self.NetworkHQ) continue;
                float d = (a.center.position - self.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = a; }
            }
            if (best == null) return false;
            calls.Press(SupportActionId.Recon);
            var pick = typeof(SupportManager).GetField("localPick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(m) as System.Action<GlobalPosition>;
            if (pick == null) return false;
            pick(best.center.position.ToGlobalPosition());
            return true;
        }

        public static Dictionary<string, object> Read(Dictionary<string, object> args) => Readout(null);

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
                }
            r["airborne"] = GameManager.GetLocalAircraft(out Aircraft own) && own != null && !own.disabled ? 1 : 0;
            r["spentSinceVerb"] = spendMark > 0f ? spendMark - m.LocalAllocation : 0f;
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
            return r;
        }

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

        private static Dictionary<string, object> Fail(string hook, string error)
        {
            Debug.LogWarning("[OpsAutomation] " + hook + ": " + error);
            return new Dictionary<string, object> { { "ok", false }, { "error", error } };
        }
    }
}
