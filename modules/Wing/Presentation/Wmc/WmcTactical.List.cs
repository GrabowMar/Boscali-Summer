using NOAvionics;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Networking;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>TACTICAL's wing table and its scope (spec 2026-10-04 §4.2): ALL, an element row and a member row set who orders go to
    /// (shift adds a member); a single selected wingman gets its own row on ORDERS with RTB (press twice), RDR, EJ (press twice) and a
    /// tap to INSPECT. The old FLIGHT list's per-wingman buttons live there now, so the table stays dense. The control ids of the old
    /// scope bar and flight list still answer: <c>tac.scope.*</c> and <c>tac.list.*</c>.</summary>
    internal sealed partial class WmcTactical
    {
        private const float FlashSeconds = 0.35f;

        private readonly ConfirmGate rtbGate = new ConfirmGate();
        // Its own gate (eject.md): RTB then EJ on one row must not confirm the ejection on the first EJ press.
        private readonly ConfirmGate ejGate = new ConfirmGate();
        private readonly List<uint> pickMembers = new List<uint>(WcSnapshot.MaxMembers);
        private float flashUntil;

        private void BuildTable(AvFlow f)
        {
            table = f.Add(new WmcWingTable(f.Content));
            table.AllPicked = PickAll;
            table.ElementPicked = PickElement;
            table.RowPicked = PickRow;
            table.SummaryPicked = ToggleTable;
            table.SupplyPicked = OpenSupply;
            ids.Add("tac.scope.all", table.AllTarget);
            ids.Add("tac.list.summary", table.SummaryTarget);
            for (int e = 0; e < WmcWingTable.MaxElements; e++)
            {
                ids.Add("tac.scope.el" + e, table.HeadTarget(e));
                ids.Add("tac.list.el" + e, table.HeadTarget(e));
            }
            for (int r = 0; r < WmcWingTable.MaxMembers; r++)
            {
                int k = r;
                string id = "tac.list.row" + r;
                ids.Add(id, table.RowTarget(r));
                ids.Add(id + ".rtb", () => RowAct(k, 0), () => table.IdAt(k) != 0u);
                ids.Add(id + ".rdr", () => RowAct(k, 1), () => table.IdAt(k) != 0u);
                ids.Add(id + ".ej", () => RowAct(k, 2), () => table.IdAt(k) != 0u);
                ids.Add(id + ".inspect", () => RowAct(k, 3), () => table.IdAt(k) != 0u);
            }
            ids.Add("tac.list.open", OpenSupply, () => table.SeatsOpen);
            ids.Add("tac.list.supply", OpenSupply, () => table.SeatsEmpty);
        }

        private void RefreshTableIds(WmcContext c)
        {
        }

        private void ToggleTable()
        {
            tableOpen = !tableOpen;
            table.SetMode(sub != SubOrders, !tableOpen);
            if (last != null) Refresh(last);
            flow.RequestRelayout();
        }

        private void PickAll()
        {
            if (last == null) return;
            last.Selection.Clear();
            last.Rescope();
        }

        /// <summary>Orders go to element <paramref name="e"/> (its row in the table).</summary>
        private void PickElement(int e)
        {
            if (last == null) return;
            pickMembers.Clear();
            for (int i = 0; i < last.Count; i++)
                if (last.Rows[i].Element == e) pickMembers.Add(last.Rows[i].Id);
            if (pickMembers.Count > 0) last.Selection.SelectElement(e, pickMembers);
            last.Rescope();
        }

        private void PickRow(int index, bool shift)
        {
            uint id = table.IdAt(index);
            if (last == null || id == 0u) return;
            if (shift) last.Selection.Toggle(id);
            else last.Selection.SelectOnly(id);
            // Review P3 I3: an order pressed right after the click goes to what the click chose.
            last.Rescope();
        }

        /// <summary>A table row's old buttons by id: select the aircraft, then 0 RTB, 1 RDR, 2 EJ, 3 INSPECT.</summary>
        private void RowAct(int index, int act)
        {
            uint id = table.IdAt(index);
            if (last == null || id == 0u) return;
            last.Selection.SelectOnly(id);
            last.Rescope();
            if (act == 0) AskRtb(id);
            else if (act == 1) ToggleRadar(id);
            else if (act == 2) AskEject(id);
            else Inspect(id);
        }

        private string CallsignOf(uint id)
        {
            Unit u = WmcContext.UnitOf(id);
            string cs = u is Aircraft air && last != null && !last.Client ? WingPilotRoster.Of(air)?.Callsign : null;
            if (!string.IsNullOrEmpty(cs)) return cs;
            int i = last != null ? WingRows.IndexOf(last.Rows, last.Count, id) : -1;
            return i >= 0 ? WingRows.Number(last.Rows[i].Slot) : "wingman";
        }

        private void AskRtb(uint id)
        {
            if (last == null || id == 0u) return;
            string key = AvNum.Fixed(id, 0);
            WmcUi.Order(last, () =>
            {
                if (!rtbGate.Press(key, Time.unscaledTime))
                {
                    WingToast.Show("Send " + CallsignOf(id) + " home? Press RTB? again");
                    return;
                }
                if (memberRtb != null) WmcMotion.Punch(memberRtb);
                WingOrders.Run(WingOrder.Of(OrderKind.Rtb, WingScope.OfMembers(id)));
            });
        }

        private void ToggleRadar(uint id)
        {
            WingMember m = last?.MemberOf(id);
            if (m == null) return;
            bool on = last.Wing.DoctrineFor(m).Radar == RadarPolicy.On;
            if (memberRdr != null) WmcMotion.Punch(memberRdr);
            WmcUi.Order(last, () => WingOrders.Run(new WingOrder
            {
                Kind = OrderKind.SetOverride, Number = (int)DoctrineAxis.Radar, Text = on ? "Off" : "On", Scope = WingScope.OfMembers(id),
            }));
        }

        private void AskEject(uint id)
        {
            if (last == null || id == 0u) return;
            string key = AvNum.Fixed(id, 0);
            WmcUi.Order(last, () =>
            {
                if (!ejGate.Press(key, Time.unscaledTime))
                {
                    WingToast.Show("Eject " + CallsignOf(id) + "? The aircraft is lost. Press EJ? again");
                    return;
                }
                WingOrders.Run(new WingOrder { Kind = OrderKind.Eject, Scope = WingScope.OfMembers(id), Flag = true });
            });
        }

        private void Inspect(uint id)
        {
            if (last == null || id == 0u) return;
            WmcPanel.Instance?.Inspect(id);
        }

        private void FocusAircraft(uint id)
        {
            WmcMap.Center(WmcContext.UnitOf(id));
            last.Selection.SelectOnly(id);
            last.Rescope();
        }

        /// <summary>The aircraft in scope flash once (an order went to them).</summary>
        private void FlashScope() => flashUntil = WmcMotion.Reduced ? 0f : Time.unscaledTime + FlashSeconds;

        private void OpenSupply() => WmcPanel.Instance?.Show(WmcTabs.Supply);
    }
}
