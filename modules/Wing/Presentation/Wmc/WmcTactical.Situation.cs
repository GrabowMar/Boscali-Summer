using NOAvionics;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>TACTICAL's alerts (spec 2026-10-04 §4.2): the wing's alerts, worst first. The footer's ALERT carries the urgent one in
    /// full; the collapsed wing table's summary line carries the worst word ("MISSILE #3 +2"); the full table's columns show fuel, ammo
    /// and damage.</summary>
    internal sealed partial class WmcTactical
    {
        private readonly Alert[] alerts = new Alert[AlertList.Max];
        private int alertCount = -1, alertKey = int.MinValue;
        private string alertText;

        private string AlertLine(in Alert a, WmcContext c)
        {
            Unit u = WmcContext.UnitOf(a.Id);
            string callsign = u is Aircraft air && !c.Client ? WingPilotRoster.Of(air)?.Callsign : null;
            return AlertList.Word(a.Kind) + "  " + WingRows.Number(a.Slot) + (string.IsNullOrEmpty(callsign) ? "" : " " + callsign)
                + " · " + AlertList.Detail(a);
        }

        /// <summary>The worst alert as one word for the table's summary line ("ALL CLEAR", "MISSILE #3 +2").</summary>
        private string AlertWord(out AvState state)
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
    }
}
