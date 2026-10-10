using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // Threat detail rows (2026-10-10): up to two ranked lines under the threat lamps, "MSL  SARH 2.4KM TTI 7S" or
    // "LOCK  SA-19 9.4KM". Missiles and locks read in the alert colour, a search-radar spike in caution; a contact's chip
    // flashes for its first four seconds. The ranking is ThreatRank's; the words are ThreatText's.
    internal sealed partial class StatusPanel
    {
        private readonly TextMeshProUGUI[] detailChip = new TextMeshProUGUI[ThreatRank.MaxRows];
        private readonly TextMeshProUGUI[] detailText = new TextMeshProUGUI[ThreatRank.MaxRows];

        private void BuildDetail()
        {
            for (int i = 0; i < detailText.Length; i++)
            {
                detailChip[i] = Text("DetailChip" + i, TextAlignmentOptions.Center, legendSize, TextOverflowModes.Overflow);
                detailText[i] = Text("DetailRow" + i, TextAlignmentOptions.MidlineLeft, legendSize, TextOverflowModes.Ellipsis);
                Shrink(detailText[i], legendSize);
                detailChip[i].gameObject.SetActive(false);
                detailText[i].gameObject.SetActive(false);
            }
        }

        private void DestroyDetail()
        {
            for (int i = 0; i < detailText.Length; i++) detailChip[i] = detailText[i] = null;
        }

        private static Color DetailTone(ThreatKind kind) =>
            kind == ThreatKind.Spike ? ThemeManager.Active.ColorTheme.Warning : Alert;

        private void PresentDetail()
        {
            float u = f.U, slotW = HudBoardLayout.SystemSlotWidth(f.G.Width, u), rowH = HudBoardLayout.DetailRowH * u;
            int shown = Mathf.Min(board.ContactCount, detailText.Length);
            for (int i = 0; i < detailText.Length; i++)
            {
                bool on = i < shown;
                Show(detailChip[i], on);
                Show(detailText[i], on);
                if (!on) continue;
                ThreatContact c = board.Contacts[i];
                string chip = ThreatText.Chip(c.Kind), row = ThreatText.Row(c, board.Units);
                if (detailChip[i].text != chip) detailChip[i].text = chip;
                if (detailText[i].text != row) detailText[i].text = row;
                Color tone = DetailTone(c.Kind);
                detailChip[i].color = tone;
                detailText[i].color = tone;
                float y = f.G.Detail.Y + i * rowH;
                float textX = HudBoardLayout.SystemSlotX(f.G.Width, u, 1);
                Place(detailChip[i].rectTransform, HudBoardLayout.SystemSlotX(f.G.Width, u, 0), y, slotW, rowH);
                Place(detailText[i].rectTransform, textX, y, f.G.Width - HudBoardLayout.Pad * u - textX, rowH);
                bool pulse = ThreatRank.IsNew(c);
                beat |= pulse;
                signature = signature * 19 + (int)c.Kind * 7 + (pulse ? 3 : 0) + (c.Locked ? 1 : 0);   // the mesh; the row words change every tick and are written on change
            }
        }

        private void PaintDetail(AvQuadBuffer b)
        {
            float u = f.U, slotW = HudBoardLayout.SystemSlotWidth(f.G.Width, u), rowH = HudBoardLayout.DetailRowH * u;
            int shown = Mathf.Min(board.ContactCount, detailText.Length);
            for (int i = 0; i < shown; i++)
            {
                ThreatContact c = board.Contacts[i];
                Color tone = DetailTone(c.Kind);
                float x0 = HudBoardLayout.SystemSlotX(f.G.Width, u, 0);
                float y1 = f.G.Height - (f.G.Detail.Y + i * rowH) - 1f * u, y0 = y1 - (rowH - 2f * u);
                bool pulse = ThreatRank.IsNew(c);
                AvStrokes.Fill(b, x0, y0, slotW, y1 - y0, tone.ToRgba().WithAlpha(pulse ? (f.FlashOn ? 0.45f : 0.12f) : 0.18f));
                Rect4(b, x0, y0, x0 + slotW, y1, 1.25f * u, tone.ToRgba());
            }
        }
    }
}
