using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using NOAvionics;

namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>
    /// <see cref="IMapWingMenu"/>: the wing's lines on the map's right-click card. A wingman gets its badge, callsign and
    /// state on top of the card and its own four orders; an enemy track gets ATTACK; bare ground gets GO HERE / ORBIT HERE.
    /// Orders go to the WMC selection while WMC is open and has one, else the whole wing, through <see cref="WingOrders"/>
    /// like every other caller (acks, refusals and networking behave the same).
    /// </summary>
    internal sealed class WingMapMenu : IMapWingMenu
    {
        public void Fill(Unit clicked, GlobalPosition ground, List<MapMenuRow> rows, List<MapMenuAction> actions)
        {
            WingService w = WingService.Instance;
            if (w == null || w.Selection == null || !AnyFlying(w)) return;

            WingMember member = clicked is Aircraft aircraft ? MemberOf(w, aircraft) : null;
            if (member != null)
            {
                int element = w.ElementOf(member);
                string badge = ElementRoster.Letter(element) + member.Number;
                string call = WingPilotRoster.Of(member)?.Callsign;
                rows.Add(new MapMenuRow
                {
                    Key = "WINGMAN", Tone = WmcMapOverlay.ElementColor(element),
                    Value = string.IsNullOrEmpty(call) ? badge : badge + "  ·  " + call.ToUpperInvariant(),
                });
                bool trouble = member.Bingo.Bingo || member.Damage.Damaged;
                rows.Add(new MapMenuRow
                {
                    Key = "STATE", Value = State(member),
                    Tone = trouble ? AvStyleHost.FuiColor("caution", AvTheme.Warning) : AvStyleHost.FuiColor("ink", AvTheme.TextPrimary),
                });
                if (!member.Sendable) return;
                WingScope one = WingScope.OfMembers(member.Aircraft.persistentID.Id);
                Add(actions, badge + " FORM UP", AvIcon.Formation, () => WingCommands.FormUp(one), true);
                Add(actions, badge + " ENGAGE", AvIcon.Swords, () => WingCommands.Engage(one), true);
                Add(actions, badge + " BREAK", AvIcon.ArrowBackUp, () => WingCommands.Disengage(one), true);
                Add(actions, badge + " RTB", AvIcon.Runway, () => WingCommands.Rtb(one), true);
                return;
            }

            Scope(out WingScope scope, out string who);
            if (clicked != null)
            {
                if (!Hostile(clicked)) return;
                uint id = clicked.persistentID.Id;
                Add(actions, "ATTACK  /  " + who, AvIcon.Target,
                    () => WingOrders.Run(new WingOrder { Kind = OrderKind.Attack, Units = new[] { id }, Scope = scope }));
                return;
            }
            // The wing keeps its own height (NaN): a map click says where, not how high.
            Waypoint at = Waypoint.At(ground.x, ground.z);
            Add(actions, "GO HERE  /  " + who, AvIcon.MapPin, () => WingOrders.Run(WingOrder.Tasked(WingTask.Move(at), scope)));
            Add(actions, "ORBIT HERE  /  " + who, AvIcon.Refresh, () => WingOrders.Run(WingOrder.Tasked(WingTask.Orbit(at), scope)));
        }

        private static void Add(List<MapMenuAction> actions, string label, AvIcon icon, System.Action invoke, bool half = false) =>
            actions.Add(new MapMenuAction { Label = label, Icon = icon, Invoke = invoke, Half = half });

        /// <summary>The WMC selection while WMC is open and has one, else the whole wing.</summary>
        private static void Scope(out WingScope scope, out string who)
        {
            WmcPanel panel = WmcPanel.Instance;
            if (panel != null && panel.Visible && panel.Context.Selection.Count > 0)
            {
                panel.Context.Rescope();
                scope = panel.Context.Scope;
                who = panel.Context.ScopeLabel;
                return;
            }
            scope = WingScope.Wing;
            who = "WING";
        }

        private static string State(WingMember m)
        {
            string doing = m.OnGround ? "ON GROUND" : m.Recovery != null ? "RECOVERING" : m.RescueTarget != null ? "ON RESCUE"
                : m.Engaged ? "ENGAGED" : WingMarkers.TargetOf(m) != null ? "ON TARGET" : "WITH FLIGHT";
            if (m.Bingo.Bingo) doing += "  ·  BINGO";
            else if (m.Bingo.Joker) doing += "  ·  JOKER";
            if (m.Damage.Damaged) doing += "  ·  DAMAGED";
            return doing;
        }

        private static bool AnyFlying(WingService w)
        {
            foreach (WingMember m in w.Members)
                if (m.Sendable) return true;
            return false;
        }

        private static WingMember MemberOf(WingService w, Aircraft aircraft)
        {
            foreach (WingMember m in w.Members)
                if (ReferenceEquals(m.Aircraft, aircraft) && m.Alive && !m.Released) return m;
            return null;
        }

        /// <summary>Another faction's track; the clicked icon is already one the local faction sees.</summary>
        private static bool Hostile(Unit unit) =>
            unit != null && !unit.disabled && unit.NetworkHQ != null &&
            GameManager.GetLocalHQ(out FactionHQ hq) && hq != null && unit.NetworkHQ != hq;
    }
}
