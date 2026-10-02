using NOAvionics;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Presentation;
using BoscaliSummer.Modules.Wing.Patches;
using BoscaliSummer.Modules.Wing.Networking;
using BoscaliSummer.Modules.Wing.Configuration;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Core.Storage;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>TACTICAL's alerts (spec FUI §TACTICAL) on kit v2: the wing's alerts, worst first — the worst as an <see cref="AvAlert"/>
    /// card, the rest as <see cref="AvList"/> rows, each with the alert's word, the aircraft and the detail; a click centres the map on
    /// the aircraft and selects it. A new MISSILE pulses on the card (kit motion tier; Wmc/ReduceMotion is the kit's reduced-motion
    /// switch). With nothing standing the list says ALL CLEAR. The footer's ALERT still carries the urgent one.</summary>
    internal sealed partial class WmcTactical
    {
        private const int AlertListRows = 4;

        private readonly Alert[] alerts = new Alert[AlertList.Max];
        private readonly uint[] alertLineIds = new uint[AlertList.Max];
        private int alertCount = -1, alertKey = int.MinValue, alertViewKey = int.MinValue;
        private string alertText;
        private AvSection alertSection;
        private AvAlert alertCard;
        private AvList alertList;
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
            alertSection = f.Section(AvIcon.AlertTriangle, "ALERTS", "ALL CLEAR");
            alertCard = f.Add(new AvAlert(f.Content));
            AvFrame cardFrame = alertCard.Rect.GetComponentInChildren<AvFrame>(true);
            AvHit hit = AvHit.On(cardFrame);
            hit.Click = e => AlertClick(0);
            AvHelpTip.Attach(cardFrame.gameObject, "Centre the map on this aircraft and select it.");
            ids.Add("tac.alert0", alertCard);
            alertList = f.Add(new AvList(f.Content, ticker, AlertListRows, BindAlertRow));
            alertList.RowClicked = item => AlertClick(alertCount > 0 ? item + 1 : -1);
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

        private static Glyph AlertGlyph(AlertKind k)
        {
            switch (k)
            {
                case AlertKind.Missile: return Glyph.Missile;
                case AlertKind.Lost: return Glyph.Lost;
                case AlertKind.Damaged: return Glyph.Warn;
                case AlertKind.Bingo:
                case AlertKind.Joker: return Glyph.Fuel;
                case AlertKind.Winchester: return Glyph.Ammo;
                default: return Glyph.Behind;
            }
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

        private void RefreshAlertCard(WmcContext c)
        {
            alertBinding = c;
            int key = alertCount * 7919;
            for (int r = 0; r < alertCount && r <= AlertListRows; r++) key = key * 31 + AlertKeyOf(alerts[r]);
            if (alertCount == 0) key += c.ScopeLabel?.GetHashCode() ?? 0;
            for (int r = 0; r < alertLineIds.Length; r++) alertLineIds[r] = r < alertCount ? alerts[r].Id : 0u;
            if (key == alertViewKey) return;
            alertViewKey = key;
            if (alertCount > 0)
            {
                Alert a = alerts[0];
                bool urgent = a.Kind <= AlertKind.Damaged;
                alertCard.Show(WmcIcons.Of(AlertGlyph(a.Kind)), AlertList.Word(a.Kind) + "  " + AlertWho(a, c), AlertList.Detail(a),
                    urgent ? AvState.Danger : AvState.Caution);
            }
            else alertCard.Hide();
            int listed = alertCount == 0 ? 1 : Mathf.Min(alertCount - 1, AlertListRows);
            alertList.SetCount(listed);
            int more = alertCount - 1 - AlertListRows;
            alertSection.SetCaption(alertCount == 0 ? "ALL CLEAR"
                : AvNum.Fixed(alertCount, 0) + " STANDING" + (more > 0 ? " · +" + AvNum.Fixed(more, 0) + " MORE" : ""));
            relayout = true;
        }

        private void BindAlertRow(int item, AvRow row)
        {
            WmcContext c = alertBinding;
            ids.Add("tac.alert" + (item + 1), row);
            if (alertCount == 0)
            {
                row.Set("ALL CLEAR", "orders go to " + (c?.ScopeLabel ?? "WING"), "", AvState.Ready);
                row.Help = null;
                return;
            }
            Alert a = alerts[item + 1];
            bool urgent = a.Kind <= AlertKind.Damaged;
            AvState state = urgent ? AvState.Danger : AvState.Caution;
            row.Set(AvStates.Glyph(state) + AlertList.Word(a.Kind) + "  " + (c != null ? AlertWho(a, c) : ""), AlertList.Detail(a), "", state);
            row.Help = "Centre the map on this aircraft and select it.";
        }

        private void AlertClick(int r)
        {
            if (last == null || r < 0 || r >= alertLineIds.Length || alertLineIds[r] == 0u) return;
            FocusAircraft(alertLineIds[r]);
        }
    }
}
