using NOAvionics;
using System.Collections.Generic;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Runtime;
using BoscaliSummer.Modules.Wing.Networking;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>TACTICAL's wing table and its scope (spec 2026-10-04 §4.2): ALL, an element row and a member row set who orders go to
    /// (shift adds a member).</summary>
    internal sealed partial class WmcTactical
    {
        private const float FlashSeconds = 0.35f;

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

        /// <summary>The aircraft in scope flash once (an order went to them).</summary>
        private void FlashScope() => flashUntil = WmcMotion.Reduced ? 0f : Time.unscaledTime + FlashSeconds;

        private void OpenSupply() => WmcPanel.Instance?.Show(WmcTabs.Supply);
    }
}
