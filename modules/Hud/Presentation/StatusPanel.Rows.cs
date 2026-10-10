using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Ui;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // Row writers (text, positions, the change signature) and painters (one vector mesh) of the status board.
    // Positions come from HudBoardLayout through this tick's f.G; nothing here computes a row's y by hand.
    internal sealed partial class StatusPanel
    {
        /// <summary>Create the text widgets of every row. Called once from Build.</summary>
        private void BuildRows(float valueSize, float labelSize, float detailSize)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].Value = Text("Value" + i, TextAlignmentOptions.TopLeft, valueSize, TextOverflowModes.Overflow);
                cells[i].Label = Text("Label" + i, TextAlignmentOptions.Bottom, labelSize, TextOverflowModes.Ellipsis);
                Shrink(cells[i].Label, labelSize);
                cells[i].Value.gameObject.SetActive(false);
                cells[i].Label.gameObject.SetActive(false);
            }
            for (int i = 0; i < legends.Length; i++)
            {
                legends[i] = Text("Lamp" + i, TextAlignmentOptions.Center, legendSize, TextOverflowModes.Overflow);
                legends[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < threatLegends.Length; i++)
            {
                threatLegends[i] = Text("Threat" + i, TextAlignmentOptions.Center, legendSize, TextOverflowModes.Overflow);
                threatLegends[i].gameObject.SetActive(false);
            }
            dialLabel = Text("HiLo", TextAlignmentOptions.MidlineLeft, legendSize, TextOverflowModes.Overflow);
            shotLegend = Text("ShotLegend", TextAlignmentOptions.MidlineLeft, legendSize, TextOverflowModes.Overflow);

            // Capacitor-style readout: a rounded bar with its fill, the words under it.
            var trackObject = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            barTrack = (RectTransform)trackObject.transform;
            barTrack.SetParent(root, false);
            Pin(barTrack);
            barTrackImage = trackObject.GetComponent<Image>();
            barTrackImage.raycastTarget = false;
            HudSprites.Slice(barTrackImage, HudSprites.Plate);
            barFill = HudSprites.Child(barTrack, "Fill", HudSprites.Plate, sliced: true);
            barFill.rectTransform.offsetMin = new Vector2(1.5f, 1.5f);
            stripLabel = Text("Detail", TextAlignmentOptions.Center, detailSize, TextOverflowModes.Ellipsis);
            stripLabel.characterSpacing = 4f;
            Shrink(stripLabel, detailSize);
            BuildDetail();
            BuildCm();
            BuildDamage();
            BuildFuel();
            BuildTicker();
        }

        private void DestroyRows()
        {
            DestroyDetail();
            DestroyCm();
            DestroyDamage();
            DestroyFuel();
            DestroyTicker();
            barTrack = null;
            barTrackImage = barFill = null;
            stripLabel = dialLabel = shotLegend = null;
            for (int i = 0; i < cells.Length; i++) cells[i] = default;
            for (int i = 0; i < legends.Length; i++) legends[i] = null;
            for (int i = 0; i < threatLegends.Length; i++) threatLegends[i] = null;
        }

        // ------------------------------------------------------------------------------------------ systems

        private void PresentSystems()
        {
            float u = f.U, slotW = HudBoardLayout.SystemSlotWidth(f.G.Width, u);
            int total = f.LampCount + HudThreats.Count;
            for (int i = 0; i < legends.Length; i++)
            {
                bool on = i < f.LampCount;
                Show(legends[i], on);
                if (!on) continue;
                Lamp lamp = lamps[lampOrder[i]];
                if (legends[i].text != lamp.Legend) legends[i].text = lamp.Legend;
                legends[i].color = LampInk(lamp, LampOn(f, lamp, i, total), f.Booting);
                beat |= Blinks(lamp);
                Place(legends[i].rectTransform, HudBoardLayout.SystemSlotX(f.G.Width, u, i), f.G.Systems.Y, slotW, HudBoardLayout.LampH * u);
                signature = signature * 13 + (int)lamp.State * 5 + lampOrder[i];
            }
        }

        private void PaintSystems(AvQuadBuffer b)
        {
            float u = f.U, slotW = HudBoardLayout.SystemSlotWidth(f.G.Width, u);
            int total = f.LampCount + HudThreats.Count;
            for (int i = 0; i < f.LampCount; i++)
                LampTile(b, lamps[lampOrder[i]], LampOn(f, lamps[lampOrder[i]], i, total), f.Booting,
                    HudBoardLayout.SystemSlotX(f.G.Width, u, i), f.G.Height - f.G.Systems.Y, slotW, HudBoardLayout.LampH * u);
        }

        // ------------------------------------------------------------------------------------------ threat row

        private void PresentThreat()
        {
            float u = f.U, slotW = HudBoardLayout.SystemSlotWidth(f.G.Width, u);
            int total = f.LampCount + HudThreats.Count;
            for (int i = 0; i < threatLegends.Length; i++)
            {
                Show(threatLegends[i], f.Threats);
                if (!f.Threats) continue;
                Lamp lamp = threatLamps[i];
                if (threatLegends[i].text != lamp.Legend) threatLegends[i].text = lamp.Legend;
                threatLegends[i].color = LampInk(lamp, LampOn(f, lamp, f.LampCount + i, total), f.Booting);
                beat |= Blinks(lamp);
                Place(threatLegends[i].rectTransform, HudBoardLayout.SystemSlotX(f.G.Width, u, i), f.G.Threat.Y, slotW, HudBoardLayout.LampH * u);
                signature = signature * 13 + (int)lamp.State * 5 + lamp.Legend.Length;
            }

            ThreatRowBox t = HudBoardLayout.ThreatRow(f.G.Width, u);
            ThreatPicture threat = board.Threat;
            string hiLo = f.Threats && threat.HasBearing ? HudThreats.HighLow(threat.ElevationDeg) : "";
            Show(dialLabel, hiLo.Length > 0);
            if (hiLo.Length > 0)
            {
                if (dialLabel.text != hiLo) dialLabel.text = hiLo;
                dialLabel.color = ThreatColor(threat);
                Place(dialLabel.rectTransform, t.HiLoX, f.G.Threat.Y, 26f * u, HudBoardLayout.LampH * u);
            }
            if (f.Threats && threat.HasBearing)
                signature = signature * 7 + Mathf.RoundToInt(threat.BearingDeg / 5f) * 3 + (threat.BearingIsMissile ? 1 : 0);

            // The word beside the pips: SHOT while any is in flight, then HIT or MISS for the latest outcome.
            string legend = f.Threats ? shots.Legend : "";
            Show(shotLegend, legend.Length > 0);
            if (legend.Length > 0)
            {
                if (shotLegend.text != legend) shotLegend.text = legend;
                Color c = legend == "MISS" ? Alert : Ink;
                shotLegend.color = new Color(c.r, c.g, c.b, legend == "SHOT" ? 0.7f : 1f);
                Place(shotLegend.rectTransform, t.LegendX, f.G.Threat.Y, 32f * u, HudBoardLayout.LampH * u);
                signature = signature * 11 + legend.Length;
            }
        }

        private void PaintThreat(AvQuadBuffer b)
        {
            float u = f.U, slotW = HudBoardLayout.SystemSlotWidth(f.G.Width, u);
            float rowTop = f.G.Height - f.G.Threat.Y, rowMid = rowTop - HudBoardLayout.LampH * u * 0.5f;
            int total = f.LampCount + HudThreats.Count;
            for (int i = 0; i < HudThreats.Count; i++)
                LampTile(b, threatLamps[i], LampOn(f, threatLamps[i], f.LampCount + i, total), f.Booting,
                    HudBoardLayout.SystemSlotX(f.G.Width, u, i), rowTop, slotW, HudBoardLayout.LampH * u);

            // Threat dial: own aircraft at the centre (a nose-up chevron, not a plus: a plus reads as an add button),
            // compass ticks on the rim, and a chevron riding the rim at the threat's bearing.
            ThreatRowBox t = HudBoardLayout.ThreatRow(f.G.Width, u);
            ThreatPicture threat = board.Threat;
            float cx = t.DialX, r = t.DialR;
            Rgba rim = Ink.ToRgba().WithAlpha(0.45f), dim = Ink.ToRgba().WithAlpha(0.6f);
            AvStrokes.Ring(b, cx, rowMid, r, 24, 1f * u, rim);
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f * Mathf.Deg2Rad, sin = Mathf.Sin(a), cos = Mathf.Cos(a);
                AvStrokes.Line(b, cx + sin * (r - 2.5f * u), rowMid + cos * (r - 2.5f * u), cx + sin * r, rowMid + cos * r, 1f * u, dim);
            }
            AvStrokes.Chevron(b, cx, rowMid - r * 0.1f, r * 0.55f, 90f, 1.25f * u, dim);
            if (threat.HasBearing)
            {
                float a = threat.BearingDeg * Mathf.Deg2Rad;
                float px = cx + Mathf.Sin(a) * r, py = rowMid + Mathf.Cos(a) * r;
                AvStrokes.Chevron(b, px, py, 7f * u, 270f - threat.BearingDeg, 1.75f * u, ThreatColor(threat).ToRgba()); // tip inward
            }

            // Shot pips: diamonds for missiles, circles for bombs; hollow in flight, filled hit, crossed miss.
            float s = t.PipSize * 0.5f;
            int slot = 0;
            for (int i = 0; i < ShotLedger.Capacity; i++)
            {
                ShotState state = shots.State(i);
                if (state == ShotState.Empty) continue;
                float x = t.PipX + slot * t.PipStep + t.PipStep * 0.5f;
                Rgba ink = (state == ShotState.Miss ? Alert : Ink).ToRgba();
                if (shots.IsBomb(i)) AvStrokes.Ring(b, x, rowMid, s * 0.8f, 12, (state == ShotState.Hit ? s * 0.8f : 1.25f * u), ink);
                else if (state == ShotState.Hit) AvStrokes.Fill(b, x - s * 0.55f, rowMid - s * 0.55f, s * 1.1f, s * 1.1f, ink);
                else AvStrokes.Diamond(b, x, rowMid, s, 1.25f * u, ink);
                if (state == ShotState.Miss)
                {
                    AvStrokes.Line(b, x - s * 0.7f, rowMid - s * 0.7f, x + s * 0.7f, rowMid + s * 0.7f, 1.5f * u, ink);
                    AvStrokes.Line(b, x - s * 0.7f, rowMid + s * 0.7f, x + s * 0.7f, rowMid - s * 0.7f, 1.5f * u, ink);
                }
                slot++;
            }
        }

        // ------------------------------------------------------------------------------------------ feed cells

        private void PresentCells(HudMessage[] snapshot)
        {
            float u = f.U, cw = HudBoardLayout.CellWidth(f.G.Width, u);
            for (int i = 0; i < cells.Length; i++)
            {
                bool on = i < f.Count;
                Show(cells[i].Value, on);
                Show(cells[i].Label, on);
                if (!on) continue;

                HudMessage m = snapshot[i];
                if (m.Tone == HudTone.Warning && (cells[i].LastTone != HudTone.Warning || cells[i].LastText != m.Text))
                    cells[i].FlashUntil = f.Now + FlashSeconds;
                // The figure follows the bar every tick: a constant text ("ILS", "LANCE") must not freeze it.
                int barPct = Mathf.RoundToInt(Mathf.Clamp01(m.Bar) * 100f);
                if (cells[i].LastText != m.Text || cells[i].LastBarPct != barPct)
                {
                    HudCellText.Resolve(m.Text, m.Bar, out string label, out string value);
                    cells[i].Value.text = value;
                    cells[i].Label.text = label;
                    cells[i].LastText = m.Text;
                    cells[i].LastBarPct = barPct;
                }
                cells[i].LastTone = m.Tone;
                Color tone = ToneColor(m.Tone);
                cells[i].Value.color = tone;
                cells[i].Label.color = tone;
                float x = HudBoardLayout.CellX(f.G.Width, u, i);
                // Boxes are at least one line of their own font tall: a box shorter than its line elides the whole text.
                float valueH = Mathf.Max(24f * u, cells[i].Value.fontSize * LineShare);
                float labelH = Mathf.Max(17f * u, cells[i].Label.fontSize * LineShare);
                Place(cells[i].Value.rectTransform, x + 5f * u, f.G.Cells.Y + 2f * u, cw - 8f * u, valueH);
                Place(cells[i].Label.rectTransform, x + 3f * u, f.G.Cells.Y + (HudBoardLayout.CellH - 2f) * u - labelH, cw - 6f * u, labelH);

                bool flashing = f.Now < cells[i].FlashUntil;
                beat |= flashing;
                signature = signature * 17 + (m.Text?.GetHashCode() ?? 0) + (int)m.Tone * 3
                    + (m.Channel?.GetHashCode() ?? 0) + (flashing ? 5 : 0);
            }
        }

        private void PaintCells(AvQuadBuffer b, HudMessage[] snapshot)
        {
            float u = f.U, cw = HudBoardLayout.CellWidth(f.G.Width, u);
            float top = f.G.Height - f.G.Cells.Y;
            for (int i = 0; i < f.Count; i++)
            {
                HudMessage m = snapshot[i];
                Color toneColor = ToneColor(m.Tone);
                bool flashing = f.Now < cells[i].FlashUntil;
                Rgba ink = toneColor.ToRgba().WithAlpha(flashing && !f.FlashOn ? 0.3f : 1f);
                float x0 = HudBoardLayout.CellX(f.G.Width, u, i), x1 = x0 + cw;
                float y1 = top, y0 = top - HudBoardLayout.CellH * u;

                if (m.Tone != HudTone.Info)
                    AvStrokes.Fill(b, x0, y0, cw, HudBoardLayout.CellH * u, toneColor.ToRgba().WithAlpha(flashing && f.FlashOn ? 0.28f : 0.12f));
                float line = (m.Tone == HudTone.Warning ? CellLine + 1f : CellLine) * u;
                Rect4(b, x0, y0, x1, y1, line, ink);
                // MFD card rail: 2 px tone strip across the cell top, the green-glass idiom.
                AvStrokes.Fill(b, x0, y1 - 2f * u, cw, 2f * u, ink);

                float size = Mathf.Min(cw - 26f * u, (HudBoardLayout.CellH - 38f) * u);
                float gx = x0 + (cw - size) * 0.5f + 6f * u, gy = y0 + 18f * u;
                foreach (float[] poly in HudGlyphs.For(m.Channel))
                    for (int p = 0; p + 3 < poly.Length; p += 2)
                        AvStrokes.Line(b, gx + poly[p] * size, gy + poly[p + 1] * size,
                            gx + poly[p + 2] * size, gy + poly[p + 3] * size, 1.75f * u, ink, 0.6f * u);
            }
        }

        // ------------------------------------------------------------------------------------------ cue strip

        /// <summary>The cue strip: the lamp test caption while it runs, else the top line's detail and progress.</summary>
        private void PresentStrip()
        {
            Show(barTrack, f.Strip);
            Show(stripLabel, f.Strip);
            if (!f.Strip) return;
            float u = f.U, pad = HudBoardLayout.Pad * u, w = f.G.Width - pad * 2f;
            Place(barTrack, pad, f.G.Strip.Y, w, HudBoardLayout.BarH * u);
            Place(stripLabel.rectTransform, pad, f.G.Strip.Y + HudBoardLayout.BarH * u, w, HudBoardLayout.StripTextH * u);
            Color tone = f.Caption != null ? Ink : ToneColor(f.Top.Tone);
            barTrackImage.color = new Color(tone.r * 0.12f, tone.g * 0.12f, tone.b * 0.12f, 0.9f);
            float fill = f.Caption != null ? Mathf.Clamp01(f.BootAge / LampTest.Online) : Mathf.Clamp01(f.Top.Bar);
            barFill.enabled = fill > 0f;
            barFill.rectTransform.anchorMax = new Vector2(Mathf.Max(fill, 0.02f), 1f);
            barFill.rectTransform.offsetMax = new Vector2(-1.5f, -1.5f);
            barFill.color = new Color(tone.r, tone.g, tone.b, 0.85f);
            string words = f.Caption ?? (string.IsNullOrEmpty(f.Top.Detail) ? "" : f.Top.Detail.ToUpperInvariant());
            if (stripLabel.text != words) stripLabel.text = words;
            stripLabel.color = tone;
        }

        // ------------------------------------------------------------------------------------------ mesh

        /// <summary>Lamp tiles, threat dial, shot pips, cell outlines and line-art in one mesh. Origin bottom-left, +Y up.</summary>
        private void Paint(HudMessage[] snapshot)
        {
            AvQuadBuffer b = vector.Buffer;
            b.Clear();
            PaintSystems(b);
            if (f.Threats) PaintThreat(b);
            PaintDetail(b);
            PaintCm(b);
            PaintDamage(b);
            PaintFuel(b);
            PaintCells(b, snapshot);
            PaintTicker(b);
            BufferGuard.Check(b, "status board");
            vector.Commit();
        }

        private void LampTile(AvQuadBuffer b, Lamp lamp, bool lit, bool booting, float x0, float y1, float w, float h)
        {
            float u = f.U, y0 = y1 - h;
            Color tone = booting ? Ink : LampTone(lamp.State);
            if (lit) AvStrokes.Fill(b, x0, y0, w, h, tone.ToRgba().WithAlpha(0.30f));
            Rect4(b, x0, y0, x0 + w, y1, 1f * u, (lit ? tone : Ink).ToRgba().WithAlpha(lit ? 0.95f : 0.3f));
        }

        /// <summary>Lit for the lamp test's pattern while it runs, else by its own state (blinking on the off beat when flashing).</summary>
        private static bool LampOn(in Frame f, Lamp lamp, int index, int total) =>
            f.Booting ? LampTest.Lit(index, total, f.BootAge)
            : lamp.State != LampState.Dark && !((lamp.Flash || lamp.State == LampState.Warning) && !f.FlashOn);

        private static void Rect4(AvQuadBuffer b, float x0, float y0, float x1, float y1, float w, Rgba c)
        {
            AvStrokes.Line(b, x0, y0, x1, y0, w, c);
            AvStrokes.Line(b, x1, y0, x1, y1, w, c);
            AvStrokes.Line(b, x1, y1, x0, y1, w, c);
            AvStrokes.Line(b, x0, y1, x0, y0, w, c);
        }
    }
}
