using NOAvionics;
using System.Collections.Generic;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>TACTICAL's alerts (spec 2026-10-04 §4.2): the wing's alerts, worst first. The wing table's summary line carries the worst
    /// word; ORDERS adds one compact strip under it — the worst alert with how many more stand — and a click centres the map on its
    /// aircraft and selects it. The ids <c>tac.alert0</c> to <c>tac.alert4</c> still answer (the strip, then the next alerts by number).
    /// The footer's ALERT still carries the urgent one.</summary>
    internal sealed partial class WmcTactical
    {
        private const int AlertListRows = 4;

        private readonly Alert[] alerts = new Alert[AlertList.Max];
        private readonly uint[] alertLineIds = new uint[AlertList.Max];
        private int alertCount = -1, alertKey = int.MinValue, alertViewKey = int.MinValue;
        private string alertText;
        private AvRow alertStrip;
        private WmcContext alertBinding;

        /// <summary>Alerts standing now (automation).</summary>
        public int AlertsShown => alertCount < 0 ? 0 : alertCount;

        /// <summary>The ids of the order-grid cells that cannot be pressed now (automation).</summary>
        public List<string> DisabledOrders()
        {
            var off = new List<string>();
            for (int k = 0; k < shownCells.Length; k++)
                if (!shownEnabled[k] && shownCells[k].Id != null) off.Add(shownCells[k].Id);
            return off;
        }

        private void BuildAlerts(AvFlow f)
        {
            alertStrip = f.Add(new AvRow(f.Content, () => AlertClick(0)));
            alertStrip.Help = "Centre the map on this aircraft and select it.";
            alertStrip.Rect.gameObject.SetActive(false);
            ids.Add("tac.alert0", alertStrip);
            for (int i = 1; i <= AlertListRows; i++)
            {
                int k = i;
                ids.Add("tac.alert" + i, () => AlertClick(k), () => alertCount > k);
            }
        }

        private string AlertLine(in Alert a, WmcContext c)
        {
            Unit u = WmcContext.UnitOf(a.Id);
            string callsign = u is Aircraft air && !c.Client ? WingPilotRoster.Of(air)?.Callsign : null;
            return AlertList.Word(a.Kind) + "  " + WingRows.Number(a.Slot) + (string.IsNullOrEmpty(callsign) ? "" : " " + callsign)
                + " · " + AlertList.Detail(a);
        }

        /// <summary>The aircraft's number and callsign (the alert word is the title's own).</summary>
        private static string AlertWho(in Alert a, WmcContext c)
        {
            Unit u = WmcContext.UnitOf(a.Id);
            string callsign = u is Aircraft air && !c.Client ? WingPilotRoster.Of(air)?.Callsign : null;
            return WingRows.Number(a.Slot) + (string.IsNullOrEmpty(callsign) ? "" : " " + callsign);
        }

        /// <summary>The worst alert as one word for the table's summary line ("ALL CLEAR", "MISSILE #3 +2").</summary>
        private string AlertWord(WmcContext c, out AvState state)
        {
            if (alertCount <= 0)
            {
                state = AvState.Ready;
                return "ALL CLEAR";
            }
            Alert a = alerts[0];
            state = a.Kind <= AlertKind.Damaged ? AvState.Danger : AvState.Caution;
            return AlertList.Word(a.Kind) + " " + WingRows.Number(a.Slot) + (alertCount > 1 ? " +" + AvNum.Fixed(alertCount - 1, 0) : "");
        }

        private void RefreshAlertList(WmcContext c)
        {
            alertCount = AlertList.Fill(c.Rows, c.Count, c.Client ? null : c.Wing?.Events, c.MissionTime, alerts);
            // The footer's ALERT line carries only the urgent one; the text is rebuilt only when the top alert changes.
            int key = alertCount > 0 && alerts[0].Kind <= AlertKind.Damaged ? AlertKeyOf(alerts[0]) : 0;
            if (key != alertKey)
            {
                alertKey = key;
                alertText = key != 0 ? AlertLine(alerts[0], c) : null;
            }
        }

        private static int AlertKeyOf(in Alert a) => (int)a.Kind * 1000003 + (int)(a.Id % 1000003u) + a.Slot * 7 + (int)a.Why * 131 + 1;

        private void RefreshAlertStrip(WmcContext c)
        {
            alertBinding = c;
            for (int r = 0; r < alertLineIds.Length; r++) alertLineIds[r] = r < alertCount ? alerts[r].Id : 0u;
            // The strip only shows with the full table: a collapsed table's summary line says the same word.
            bool on = alertCount > 0 && !table.Collapsed;
            if (alertStrip.Rect.gameObject.activeSelf != on)
            {
                alertStrip.Rect.gameObject.SetActive(on);
                relayout = true;
            }
            int key = alertCount * 7919;
            for (int r = 0; r < alertCount && r <= AlertListRows; r++) key = key * 31 + AlertKeyOf(alerts[r]);
            if (key == alertViewKey) return;
            alertViewKey = key;
            if (alertCount == 0) return;
            Alert a = alerts[0];
            AvState state = a.Kind <= AlertKind.Damaged ? AvState.Danger : AvState.Caution;
            int more = alertCount - 1;
            alertStrip.Set(AvStates.Glyph(state) + AlertList.Word(a.Kind) + "  " + AlertWho(a, c),
                AlertList.Detail(a) + (more > 0 ? " · +" + AvNum.Fixed(more, 0) + " MORE" : ""), "", state);
            relayout = true;
        }

        private void AlertClick(int r)
        {
            if (last == null || r < 0 || r >= alertLineIds.Length || alertLineIds[r] == 0u) return;
            FocusAircraft(alertLineIds[r]);
        }
    }
}
