using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.C2;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Presentation;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>Hooks for the unattended in-game OPS wiring check (<c>tests/ingame/ops-panels.json</c>): open a tab the way the
    /// bezel press does, send a verb through the same <see cref="SupportManager"/> calls the buttons use, and read back what the
    /// host mirrored. Dev tooling: nothing here runs unless called.</summary>
    public static class OpsAutomation
    {
        /// <summary>Maximize the map, select the OPS screen and latch <c>tab</c> (1 CAP .. 5 BOARD).</summary>
        public static Dictionary<string, object> Open(Dictionary<string, object> args)
        {
            CallsPanel panel = Find<CallsPanel>();
            if (panel == null) return Fail("Open", "no OPS panel in this scene");
            panel.OpenForAutomation((C2Tab)Mathf.Clamp((int)Number(args, "tab", 1f), 1, 5));
            return Readout(null);
        }

        /// <summary>Send one verb: <c>hop</c> (first unheld CYBER node, 0 when none), <c>plan</c> (ASAT on OPTICAL, the NET START button), <c>raise</c> (SOF team), <c>fund</c> (CYBER operation),
        /// <c>sync</c> (all three mirrors). The verdict arrives as the controller's spoken words a frame or two later.</summary>
        public static Dictionary<string, object> Verb(Dictionary<string, object> args)
        {
            SupportManager m = SupportManager.Active;
            if (m == null) return Fail("Verb", "no support manager");
            string verb = Text(args, "verb") ?? "";
            int request = 0;
            switch (verb)
            {
                case "hop":
                    foreach (CyberNodeRow n in m.CyberMirror.State.Nodes)
                        if (!n.Held && !n.Hopping) { request = m.CyberHop(n.Id); break; }
                    break; // request 0 = no node in reach (game state, not wiring)
                case "plan": request = m.OpsPlan(OpKind.Asat, 0); break;
                case "raise": request = m.SofRaise(); break;
                case "fund": request = m.OpsFund(OpDomain.Cyber, false); break;
                case "sync":
                    m.CyberFeed.Want(true); m.SofFeed.Want(true); m.OpsFeed.Want(true);
                    request = -1;
                    break;
                case "scan": request = Scan(m, Arg(args, "unitUnit") as Unit) ? -1 : 0; break;
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
            }
            if (calls != null) { r["words"] = calls.LastWords; r["wordsAt"] = calls.LastWordsAt; }
            if (m == null) return r;
            r["online"] = m.Online;
            r["credit"] = m.LocalCredit;
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
            r["spaceContacts"] = m.SpaceMirror.State.Contacts.Count;
            r["spaceMarks"] = m.SpaceMirror.State.Marks.Count;
            r["spacePosts"] = m.SpaceMirror.State.Posts.Count;
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
