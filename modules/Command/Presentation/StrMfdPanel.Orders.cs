using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    // STR BOARD's ORDERS IN FORCE: what the doctrine, the decisions, the cabinet and the war support thresholds are doing to the war right
    // now, ours then theirs, then the next threshold. Read from the nation view (StrNation); hidden while the layer is off. Each row opens FAC POLITICS.
    internal sealed partial class StrMfdPanel
    {
        private readonly AvRow[] orders = new AvRow[PoliticsText.MaxOrders];
        private readonly OrderLine[] orderLines = new OrderLine[PoliticsText.MaxOrders];
        private AvSection ordersSection;

        private void BuildOrders(AvFlow p)
        {
            ordersSection = p.Section(AvIcon.Scale, "ORDERS IN FORCE", "DECIDED ON FAC · WHAT THE CABINET DOES NOW");
            for (int i = 0; i < orders.Length; i++)
            {
                orders[i] = p.Add(new AvRow(p.Content, () => AvConsole.Open("FAC", "POLITICS")));
                orders[i].Help = "An order in force: a decision of ours or theirs with its clock, the cabinet's hold on the staff, or a war support threshold. Opens FAC POLITICS.";
            }
        }

        private void BindOrders()
        {
            if (ordersSection == null) return;
            NationView v = StrNation.View;
            ordersSection.SetShown(v != null);
            int n = v != null ? PoliticsText.Orders(v, orderLines) : 0;
            for (int i = 0; i < orders.Length; i++)
            {
                bool on = i < n;
                orders[i].SetShown(on);
                if (!on) continue;
                OrderLine line = orderLines[i];
                orders[i].Set((line.Side == 0 ? "OURS · " : line.Side == 1 ? "THEIRS · " : "NEXT · ") + line.Text, null, line.Value,
                    line.Bad ? AvState.Danger : line.Side == 2 ? AvState.Inert : AvState.Ready);
            }
        }

        /// <summary>The orders' share of the nation key: what they print, by whole second.</summary>
        private static int OrdersKey(NationView v)
        {
            unchecked
            {
                int h = Mathf.RoundToInt(v.WarSupport) * 7 + v.PostureFloor * 3 + v.PostureCeiling + v.JetsDelta * 101 + v.EnemyDecided * 31 +
                        Mathf.CeilToInt(v.EnemyDecidedLeft) * 17 + (v.EnemyIntel ?? "").Length;
                for (int i = 0; i < Decisions.Count; i++) h = h * 31 + Mathf.CeilToInt(v.InForce[i]);
                return h;
            }
        }
    }
}
