using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using NuclearOption.UIStyleSystem;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // Countermeasure tiles (2026-10-10): every station at once, "IR 72", "RDR 30", "JAM". LOW (a quarter or less) wears the
    // caution colour and the word, EMPTY the alert colour and the word; while something inbound would fall to a station's
    // countermeasure type, that tile is highlighted (heavier outline, corner brackets). Read-only: never selects a station.
    internal sealed partial class StatusPanel
    {
        private readonly TextMeshProUGUI[] cmText = new TextMeshProUGUI[HudCm.MaxStations];
        private readonly string[] cmWords = new string[HudCm.MaxStations];
        private readonly string[] cmTags = new string[HudCm.MaxStations];
        private readonly int[] cmAmmo = new int[HudCm.MaxStations];

        private void BuildCm()
        {
            for (int i = 0; i < cmText.Length; i++)
            {
                cmText[i] = Text("Cm" + i, TextAlignmentOptions.Center, legendSize, TextOverflowModes.Overflow);
                Shrink(cmText[i], legendSize);
                cmText[i].gameObject.SetActive(false);
                cmWords[i] = null;
            }
        }

        private void DestroyCm()
        {
            for (int i = 0; i < cmText.Length; i++) cmText[i] = null;
        }

        private static Color CmTone(CmLevel level) =>
            level == CmLevel.Empty ? Alert : level == CmLevel.Low ? ThemeManager.Active.ColorTheme.Warning : Ink;

        private bool CmCounters(in CmStation s) => board.InboundMask != 0 && (s.Mask & board.InboundMask) != 0;

        private void PresentCm()
        {
            float u = f.U, tileW = HudBoardLayout.TileWidth(f.G.Width, u);
            for (int i = 0; i < cmText.Length; i++)
            {
                bool on = i < board.CmCount;
                Show(cmText[i], on);
                if (!on) continue;
                CmStation s = board.Cm[i];
                CmLevel level = HudCm.Level(s);
                // The words are rebuilt only when the count (or the station) changed.
                if (cmWords[i] == null || cmAmmo[i] != s.Ammo || cmTags[i] != s.Tag)
                {
                    cmAmmo[i] = s.Ammo;
                    cmTags[i] = s.Tag;
                    cmWords[i] = HudCm.Words(s);
                }
                if (cmText[i].text != cmWords[i]) cmText[i].text = cmWords[i];
                bool counters = CmCounters(s);
                Color tone = CmTone(level);
                cmText[i].color = new Color(tone.r, tone.g, tone.b, level == CmLevel.Ok && !counters ? 0.85f : 1f);
                Place(cmText[i].rectTransform, HudBoardLayout.TileX(f.G.Width, u, i), f.G.Cm.Y, tileW, HudBoardLayout.TileH * u);
                signature = signature * 23 + (int)level * 5 + (counters ? 1 : 0) + s.Ammo * 31 + s.Tag.GetHashCode();
            }
        }

        private void PaintCm(AvQuadBuffer b)
        {
            float u = f.U, tileW = HudBoardLayout.TileWidth(f.G.Width, u), h = HudBoardLayout.TileH * u;
            float y1 = f.G.Height - f.G.Cm.Y, y0 = y1 - h;
            int shown = Mathf.Min(board.CmCount, cmText.Length);
            for (int i = 0; i < shown; i++)
            {
                CmStation s = board.Cm[i];
                CmLevel level = HudCm.Level(s);
                bool counters = CmCounters(s);
                Color tone = CmTone(level);
                float x0 = HudBoardLayout.TileX(f.G.Width, u, i), x1 = x0 + tileW;
                if (level != CmLevel.Ok || counters)
                    AvStrokes.Fill(b, x0, y0, tileW, h, tone.ToRgba().WithAlpha(counters ? 0.20f : 0.14f));
                Rect4(b, x0, y0, x1, y1, (counters ? 2f : 1f) * u,
                    tone.ToRgba().WithAlpha(counters || level != CmLevel.Ok ? 1f : 0.3f));
                if (counters) AvStrokes.Bracket(b, x0 + 1.5f * u, y0 + 1.5f * u, tileW - 3f * u, h - 3f * u, 5f * u, 1.5f * u, tone.ToRgba());
            }
        }
    }
}
