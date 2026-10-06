using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Networking;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>What every WMC tab reads at a refresh (spec M7b §6): the wing's rows — the host's service, or a client's
    /// mirror of the same snapshot entries.</summary>
    internal sealed class WmcContext
    {
        public WingService Wing;
        public readonly SnapshotMember[] Rows = new SnapshotMember[WcSnapshot.MaxMembers];
        public int Count;
        public bool Client, Stale;
        public float MissionTime;
        /// <summary>Who the next order goes to (spec WMC program §4): the selection by aircraft id, and the scope it makes this
        /// refresh.</summary>
        public readonly WmcSelection Selection = new WmcSelection();
        public WingScope Scope;
        /// <summary>The element the scope points at without detaching (A for the wing, the first selected member's).</summary>
        public int ScopeElement;
        /// <summary>The scope in words ("WING", "ELEMENT B", "#3 #4").</summary>
        public string ScopeLabel = "WING";
        /// <summary>The route editor's draft and the map's right-click orders (spec WMC program §4-§5).</summary>
        public readonly RouteDraft Draft = new RouteDraft();
        public readonly WmcMapInput Map = new WmcMapInput();
        /// <summary>The QUEUE toggle on ORDERS: map orders add to the scope's element lane (as holding shift does) instead of replacing its task.</summary>
        public bool Queue;

        /// <summary>Scope, label and element from the selection over this refresh's rows. The panel calls it on refresh, and
        /// every selection handler calls it at once (review P3 I3), so an order pressed right after a click goes to what
        /// the click chose.</summary>
        public void Rescope()
        {
            Scope = Selection.Scope(Rows, Count);
            ScopeLabel = Selection.Label(Rows, Count);
            ScopeElement = 0;
            if (Scope.Kind == ScopeKind.Element) ScopeElement = Scope.Element;
            else if (Scope.Kind == ScopeKind.Members)
            {
                int i = WingRows.IndexOf(Rows, Count, Scope.Members[0]);
                if (i >= 0) ScopeElement = Rows[i].Element;
            }
        }

        /// <summary>Whether row <paramref name="m"/> is in this refresh's scope.</summary>
        public bool InScope(in SnapshotMember m)
        {
            switch (Scope.Kind)
            {
                case ScopeKind.Element: return m.Element == Scope.Element;
                case ScopeKind.Members:
                    if (Scope.Members == null) return false;
                    foreach (uint id in Scope.Members)
                        if (id == m.Id) return true;
                    return false;
                default: return true;
            }
        }

        /// <summary>The value of <paramref name="axis"/> every aircraft in the scope flies (host), or -1 when they differ;
        /// an empty scope reads its element's doctrine.</summary>
        public int ScopeValue(DoctrineAxis axis)
        {
            if (Wing == null || Client) return -1;
            int v = -2;
            for (int i = 0; i < Count; i++)
            {
                if (!InScope(Rows[i])) continue;
                WingMember m = MemberOf(Rows[i].Id);
                if (m == null) continue;
                int x = Wing.DoctrineFor(m).Get(axis);
                if (v == -2) v = x;
                else if (v != x) return -1;
            }
            return v == -2 ? Wing.DoctrineOf(ScopeElement).Get(axis) : v;
        }

        /// <summary>The doctrine the scope flies (host); false when its aircraft fly different ones (MIXED).</summary>
        public bool ScopeDoctrine(out WingDoctrine d)
        {
            d = Wing != null && !Client ? Wing.DoctrineOf(ScopeElement) : WingDoctrine.Reserve;
            if (Wing == null || Client) return true;
            bool first = true;
            for (int i = 0; i < Count; i++)
            {
                if (!InScope(Rows[i])) continue;
                WingMember m = MemberOf(Rows[i].Id);
                if (m == null) continue;
                WingDoctrine x = Wing.DoctrineFor(m);
                if (first)
                {
                    d = x;
                    first = false;
                }
                else if (x != d) return false;
            }
            return true;
        }

        /// <summary>Orders run on the host (client orders are M6c-2).</summary>
        public bool CanOrder => !Client && Wing != null && Wing.Selection != null;

        /// <summary>The live member flying the aircraft with this persistent id (host only), or null.</summary>
        public WingMember MemberOf(uint id)
        {
            if (Wing == null || id == 0u) return null;
            foreach (WingMember m in Wing.Members)
                if (!m.Released && m.Aircraft != null && m.Aircraft.persistentID.Id == id) return m;
            return null;
        }

        /// <summary>The aircraft with this persistent id, or null.</summary>
        public static Unit UnitOf(uint id) => id != 0u && new PersistentID { Id = id }.TryGetUnit(out Unit u) ? u : null;
    }

    /// <summary>Layout helpers the WMC tabs share.</summary>
    internal static class WmcUi
    {
        /// <summary>The row-value colour of a level class ("ok", "warn", "bad", "").</summary>
        public static Color LevelColor(string level) =>
            level == "bad" ? AvTheme.RailDanger : level == "warn" ? AvTheme.RailCaution : level == "ok" ? AvTheme.RailReady
            : AvTheme.TextPrimary;

        /// <summary>Run an order only where orders run; otherwise say why (review focus 1).</summary>
        public static void Order(WmcContext c, Action act)
        {
            if (c == null || !c.CanOrder)
            {
                WingToast.Show(c != null && c.Client ? "WMC: orders are host only for now" : "Wing Command is not ready");
                return;
            }
            act();
        }
    }
}
