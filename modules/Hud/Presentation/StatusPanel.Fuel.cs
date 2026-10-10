using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Core.Contracts;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The fuel row (2026-10-10): drop-tank glyph, the level and what it buys ("38%  END 42MIN"), the way home ("RTB
    // HIGHWAYSTRIP2 23KM") and, when it applies, a BINGO or JOKER tile at the right end. It replaces the old "fuel" feed cell
    // and its divert strip, so the cue strip is free for ILS, SKYWELL and combat words. Amber and red follow FuelEndurance's
    // tone (the same 25 % / 10 % thresholds as before); BINGO flashes. The rules are FuelEndurance's and FuelText's.
    internal sealed partial class StatusPanel
    {
        private TextMeshProUGUI fuelLevel, fuelHome, fuelState;
        private string fuelLevelWords, fuelHomeWords, fuelHomeField;
        private int fuelLevelKey = -1, fuelHomeKey = -1;

        private void BuildFuel()
        {
            fuelLevel = Text("FuelLevel", TextAlignmentOptions.MidlineLeft, legendSize, TextOverflowModes.Overflow);
            fuelHome = Text("FuelHome", TextAlignmentOptions.MidlineLeft, legendSize, TextOverflowModes.Ellipsis);
            Shrink(fuelHome, legendSize);
            fuelState = Text("FuelState", TextAlignmentOptions.Center, legendSize, TextOverflowModes.Overflow);
            fuelLevel.gameObject.SetActive(false);
            fuelHome.gameObject.SetActive(false);
            fuelState.gameObject.SetActive(false);
            fuelLevelKey = fuelHomeKey = -1;
        }

        private void DestroyFuel()
        {
            fuelLevel = fuelHome = fuelState = null;
            fuelLevelWords = fuelHomeWords = fuelHomeField = null;
        }

        private float FuelLevelWidth(float u) => 132f * u;

        private float FuelStateWidth(float u) => HudBoardLayout.SystemSlotWidth(f.G.Width, u) * 1.5f;

        private void PresentFuel()
        {
            FuelState s = board.Fuel;
            bool on = s.Valid;
            string state = on ? FuelText.State(s.Bingo, s.Joker) : "";
            Show(fuelLevel, on);
            Show(fuelHome, on);
            Show(fuelState, state.Length > 0);
            if (!on) return;
            float u = f.U, pad = HudBoardLayout.Pad * u, h = HudBoardLayout.TileH * u;
            Color tone = ToneColor(s.Tone);
            Color ink = s.Tone == HudTone.Info ? Ink : tone;

            // Words are rebuilt only when what they show changed: the percent and minutes, the field and its range in 100 m steps.
            int levelKey = Mathf.RoundToInt(s.Level * 100f) * 10000 + (float.IsInfinity(s.EnduranceS) || float.IsNaN(s.EnduranceS) ? 9999 : Mathf.Min(9998, (int)(s.EnduranceS / 60f)));
            if (fuelLevelKey != levelKey) { fuelLevelKey = levelKey; fuelLevelWords = FuelText.Level(s.Level, s.EnduranceS); }
            int homeKey = float.IsNaN(s.FieldM) ? -1 : (int)(s.FieldM / 100f) * 2 + (board.Units == AvUnits.Metric ? 0 : 1);
            if (fuelHomeKey != homeKey || !ReferenceEquals(fuelHomeField, s.Field))
            {
                fuelHomeKey = homeKey;
                fuelHomeField = s.Field;
                fuelHomeWords = FuelText.Home(s.Field, s.FieldM, board.Units);
            }
            if (fuelLevel.text != fuelLevelWords) fuelLevel.text = fuelLevelWords;
            if (fuelHome.text != fuelHomeWords) fuelHome.text = fuelHomeWords;
            fuelLevel.color = ink;
            fuelHome.color = new Color(ink.r, ink.g, ink.b, s.Tone == HudTone.Info ? 0.75f : 1f);

            float levelX = pad + h + 4f * u, levelW = FuelLevelWidth(u);
            float right = f.G.Width - pad - (state.Length > 0 ? FuelStateWidth(u) + 4f * u : 0f);
            Place(fuelLevel.rectTransform, levelX, f.G.Fuel.Y, levelW, h);
            Place(fuelHome.rectTransform, levelX + levelW + 6f * u, f.G.Fuel.Y, Mathf.Max(8f * u, right - (levelX + levelW + 6f * u)), h);
            if (state.Length > 0)
            {
                if (fuelState.text != state) fuelState.text = state;
                fuelState.color = tone;
                Place(fuelState.rectTransform, f.G.Width - pad - FuelStateWidth(u), f.G.Fuel.Y, FuelStateWidth(u), h);
            }
            beat |= s.Bingo;
            signature = signature * 37 + levelKey + homeKey * 3 + (int)s.Tone * 11 + state.Length;
        }

        private void PaintFuel(AvQuadBuffer b)
        {
            FuelState s = board.Fuel;
            if (!s.Valid) return;
            float u = f.U, pad = HudBoardLayout.Pad * u, h = HudBoardLayout.TileH * u;
            Color tone = ToneColor(s.Tone);
            Color ink = s.Tone == HudTone.Info ? Ink : tone;
            float x0 = pad, x1 = f.G.Width - pad, y1 = f.G.Height - f.G.Fuel.Y, y0 = y1 - h;
            if (s.Tone != HudTone.Info) AvStrokes.Fill(b, x0, y0, x1 - x0, h, tone.ToRgba().WithAlpha(0.12f));
            Rect4(b, x0, y0, x1, y1, 1f * u, ink.ToRgba().WithAlpha(s.Tone == HudTone.Info ? 0.3f : 1f));

            // The drop tank, in a square at the left end.
            float size = h - 6f * u, gx = x0 + 3f * u, gy = y0 + 3f * u;
            foreach (float[] poly in HudGlyphs.For("fuel"))
                for (int p = 0; p + 3 < poly.Length; p += 2)
                    AvStrokes.Line(b, gx + poly[p] * size, gy + poly[p + 1] * size, gx + poly[p + 2] * size, gy + poly[p + 3] * size,
                        1.5f * u, ink.ToRgba(), 0.5f * u);

            string state = FuelText.State(s.Bingo, s.Joker);
            if (state.Length == 0) return;
            float w = FuelStateWidth(u), sx0 = x1 - w, sy0 = y0 + 2f * u, sy1 = y1 - 2f * u;
            bool flashOff = s.Bingo && !f.FlashOn;
            AvStrokes.Fill(b, sx0, sy0, w - 2f * u, sy1 - sy0, tone.ToRgba().WithAlpha(flashOff ? 0.12f : 0.40f));
            Rect4(b, sx0, sy0, x1 - 2f * u, sy1, 1.5f * u, tone.ToRgba());
        }
    }
}
