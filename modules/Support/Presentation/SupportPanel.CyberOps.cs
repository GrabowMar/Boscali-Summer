using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Features.Support.Runtime.Actions;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// CYBER › OPERATIONS — strike back. The offensive operations (doctrine-gated, jammer-backed
    /// where they reach through the air) and the flare barrage, as the shared support-action rows.
    /// The heading carries the count the page shows; the hint says what the network currently
    /// gives or takes: a foothold discount, a breached Cyber Command, or the CRYPTO discount.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private TMP_Text operationsNote;
        private TMP_Text operationsHint;

        private void ResetCyberOpsPage() => operationsNote = operationsHint = null;

        private void BuildCyberOpsPage(RectTransform root, Rect body)
        {
            int operations = CountActions(TabCyber);
            float height = HeaderHeight + 38f + Mathf.Max(1, operations) * RowHeight + SectionGap;
            float rowHeight = RowHeight;
            RectTransform parent = BeginSub(root, body, height, out float x, out float y, out float width);
            operationsNote = Header(parent, x, ref y, width, "OFFENSIVE OPERATIONS", "");
            operationsHint = Wrapped(AvStyled.Label(parent, new Rect(x, y, width, 30f), "", "hint"));
            operationsHint.color = AvTheme.Dim;
            y -= 38f;
            if (operations == 0)
            {
                // The catalogue resolved nothing; say so in the row the page would have used.
                AvStyled.Box(parent, new Rect(x, y, width, rowHeight), "card inert");
                AvStyled.Rail(parent, new Rect(x, y, 3f, rowHeight), "locked");
                AvStyled.Label(parent, new Rect(x + 12f, y - 9f, width - 24f, 16f),
                               "NO OFFENSIVE OPERATIONS", "row-name").color = AvTheme.Dim;
                AvStyled.Label(parent, new Rect(x + 12f, y - 29f, width - 24f, 14f),
                               "This server resolves none of the catalogue.", "row-sub").color = AvTheme.Disabled;
            }
            else
            {
                BuildActionRows(parent, TabCyber, x, y, width, "ARM", rowHeight);
            }
        }

        private void RefreshCyberOpsPage(bool bypass, CyberNetwork network, double now)
        {
            if (operationsNote == null) return;
            InfoNetwork info = support.LocalInfo;
            int discount = Mathf.RoundToInt((1f - (info != null ? info.Powers.CostScale : 1f)) * 100f);
            int operations = CountActions(TabCyber);
            operationsNote.text = operations + (operations == 1 ? " OPERATION" : " OPERATIONS");
            if (network != null && network.CommandCompromised)
                operationsHint.text = "C2 BREACHED · OPERATIONS LOCKED";
            else if (network != null && network.AnyFoothold(now))
                operationsHint.text = "FOOTHOLD -" + Mathf.RoundToInt((1f - HackAction.FootholdDiscount) * 100f) + "% COST" +
                                      (discount > 0 ? " · CRYPTO -" + discount + "%" : "");
            else
                operationsHint.text = discount > 0 ? "CRYPTO -" + discount + "% COST" : "ARM, THEN RIGHT-CLICK MAP";
        }
    }
}
