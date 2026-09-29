using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    /// <summary>
    /// SPACE's hero: a planet limb and an orbit track with the station drawn as its 5 x 3 truss
    /// (one cell per module, coloured by state) at the theatre column it holds, a nine-sector
    /// position board and the status in words. With no station it is the empty-state card: the same
    /// schematic with a dashed empty truss and a LAUNCH CORE call to action inside it.
    /// Height follows the mode; both are fixed, so nothing in the page reflows on a refresh.
    /// </summary>
    internal sealed class OrbitHero : AvPart
    {
        public const float StationH = 176f, EmptyH = 196f;
        public const byte CellNone = 0, CellOnline = 1, CellOffline = 2, CellPending = 3, CellCore = 4, CellHot = 5;
        public const int Cells = 15;

        private const float PlanetR = 700f, StationApex = 156f, EmptyApex = 190f, OrbitLift = 106f;
        private const float CellW = 12f, CellHt = 9f, CellGap = 2f;

        private readonly AvFrame frame;
        private readonly OpsCanvas art;
        private readonly TMP_Text tag, callsign, status, place, mission, title, prose, big, bigCaption;
        private AvControl cta;
        private readonly byte[] cells = new byte[Cells];
        private bool exists;
        private AvState tone = AvState.Inert;
        private int sector = 4, target = -1;
        private float width;
        private bool dirty = true;

        public OrbitHero(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "OrbitHero");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            frame.Bracket = 8f;
            art = OpsCanvas.Add(Rect, "Art");
            tag = OpsText.Line(Rect, "Tag", AvTextRole.Micro, TextAlignmentOptions.TopLeft);
            callsign = OpsText.Line(Rect, "Callsign", AvTextRole.Micro, TextAlignmentOptions.Center);
            status = OpsText.Line(Rect, "Status", AvTextRole.Title, TextAlignmentOptions.MidlineLeft);
            place = OpsText.Line(Rect, "Place", AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
            mission = OpsText.Line(Rect, "Mission", AvTextRole.ProseSmall, TextAlignmentOptions.MidlineLeft);
            title = OpsText.Line(Rect, "Title", AvTextRole.Title, TextAlignmentOptions.MidlineLeft);
            prose = OpsText.Block(Rect, "Prose", AvTextRole.Prose);
            big = OpsText.Line(Rect, "Big", AvTextRole.Display, TextAlignmentOptions.MidlineRight);
            bigCaption = OpsText.Line(Rect, "BigCaption", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            Restyle();
        }

        public AvControl AddCta(AvControl.Spec spec)
        {
            cta = AvControl.Make(Rect, spec);
            cta.Rect.gameObject.SetActive(!exists);
            return cta;
        }

        /// <summary>The live station: status word, the tag, the place line, the loadout line, the sector
        /// it holds, the sector it is moving to (or -1) and one state per truss cell.</summary>
        public void ShowStation(string word, AvState state, string tagText, string placeText, string missionText,
            string callsignText, int heldSector, int movingTo, byte[] cellStates, string bigText = "", string bigCaptionText = "")
        {
            bool modeChanged = !exists;
            exists = true;
            bool restyle = tone != state;
            tone = state;
            OpsText.Set(status, AvStates.Glyph(state) + (word ?? ""));
            OpsText.Set(tag, tagText);
            OpsText.Set(place, placeText);
            OpsText.Set(mission, missionText);
            OpsText.Set(callsign, callsignText);
            OpsText.Set(big, bigText);
            OpsText.Set(bigCaption, bigCaptionText);
            if (heldSector != sector || movingTo != target) { sector = heldSector; target = movingTo; dirty = true; }
            for (int i = 0; i < Cells; i++)
            {
                byte v = cellStates != null && i < cellStates.Length ? cellStates[i] : CellNone;
                if (cells[i] != v) { cells[i] = v; dirty = true; }
            }
            if (modeChanged) EnterMode();
            if (restyle || modeChanged) Restyle(); else Paint();
        }

        public void ShowEmpty(string headline, string text, string tagText)
        {
            bool modeChanged = exists || title.text.Length == 0;
            exists = false;
            OpsText.Set(title, headline);
            OpsText.Set(prose, text);
            OpsText.Set(tag, tagText);
            OpsText.Set(callsign, "");
            if (modeChanged) EnterMode();
            if (modeChanged) Restyle(); else Paint();
        }

        private void EnterMode()
        {
            status.gameObject.SetActive(exists);
            place.gameObject.SetActive(exists);
            mission.gameObject.SetActive(exists);
            callsign.gameObject.SetActive(exists);
            big.gameObject.SetActive(exists);
            bigCaption.gameObject.SetActive(exists);
            title.gameObject.SetActive(!exists);
            prose.gameObject.SetActive(!exists);
            if (cta != null) cta.Rect.gameObject.SetActive(!exists);
            dirty = true;
            Changed();
        }

        public override float Measure(float w) => exists ? StationH : EmptyH;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            AvLay.Place((RectTransform)art.transform, 0f, 0f, s.W, s.H);
            OpsText.Place(tag, 14f, 10f, 92f, 15f);
            OpsText.Place(status, 14f, 90f, s.W - 28f, 26f);
            OpsText.Place(place, 14f, 118f, s.W - 28f - 130f, 18f);
            OpsText.Place(mission, 14f, 136f, s.W - 28f - 130f, 16f);
            OpsText.Place(big, s.W - 14f - 124f, 118f, 124f, 30f);
            OpsText.Place(bigCaption, s.W - 14f - 124f, 96f, 124f, 15f);
            OpsText.Place(title, 14f, 90f, s.W - 28f, 26f);
            OpsText.Place(prose, 14f, 118f, s.W - 28f, AvText.Height(prose, s.W - 28f));
            if (cta != null) AvLay.Place(cta.Rect, 14f, 158f, 176f, 30f);
            dirty = true;
            Paint();
        }

        public override void Restyle()
        {
            frame.Paint(OpsInk.Sunken, OpsInk.Hairline);
            frame.BracketColor = OpsInk.Frame;
            frame.SetVerticesDirty();
            status.color = OpsInk.Word(tone);
            place.color = OpsInk.Ink;
            mission.color = OpsInk.Dim;
            tag.color = OpsInk.Muted;
            callsign.color = OpsInk.Key;
            big.color = OpsInk.Ink;
            bigCaption.color = OpsInk.Muted;
            title.color = OpsInk.Ink;
            prose.color = OpsInk.Dim;
            cta?.Restyle();
            dirty = true;
            Paint();
        }

        private static float ArcY(float x, float cx, float cy, float r)
        {
            float dx = x - cx;
            return dx * dx >= r * r ? float.NaN : cy - Mathf.Sqrt(r * r - dx * dx);
        }

        private void Paint()
        {
            if (!dirty || width <= 0f) return;
            dirty = false;
            float w = width, h = exists ? StationH : EmptyH;
            float cx = w * 0.5f, cy = StationApex + PlanetR, planetCy = (exists ? StationApex : EmptyApex) + PlanetR;
            Color info = OpsInk.Rail(AvState.Info), ready = OpsInk.Rail(AvState.Ready), ink = OpsInk.Ink;
            Color hair = OpsInk.Hairline;

            art.Begin();
            // Star field: fixed pseudo-random points in the space above the orbit.
            for (int i = 0; i < 26; i++)
            {
                float sx = (i * 137.5f) % w, sy = 6f + (i * 61.8f) % 62f;
                art.Box(sx, sy, 1f, 1f, OpsInk.A(ink, 0.12f + (i % 3) * 0.06f));
            }
            // Planet: limb fill in 6 px columns, the limb line and a soft atmosphere.
            for (float x = 0f; x < w; x += 6f)
            {
                float y = ArcY(x + 3f, cx, planetCy, PlanetR);
                if (float.IsNaN(y) || y >= h) continue;
                art.BoxV(x, y, 6f, h - y, OpsInk.A(info, 0.34f), OpsInk.A(info, 0.05f));
            }
            for (float x = 0f; x < w; x += 10f)
            {
                float y0 = ArcY(x, cx, planetCy, PlanetR), y1 = ArcY(x + 10f, cx, planetCy, PlanetR);
                if (float.IsNaN(y0) || float.IsNaN(y1) || (y0 >= h && y1 >= h)) continue;
                art.Line(x, y0 - 3f, x + 10f, y1 - 3f, 5f, OpsInk.A(info, 0.10f));
                art.Line(x, y0, x + 10f, y1, 1.6f, OpsInk.A(info, 0.95f));
            }
            // Orbit track: dashed, with a tick at each of the three columns a station can hold.
            float orbitR = PlanetR + OrbitLift;
            for (float x = 0f; x < w; x += 16f)
            {
                float y0 = ArcY(x, cx, cy, orbitR), y1 = ArcY(x + 9f, cx, cy, orbitR);
                if (float.IsNaN(y0) || float.IsNaN(y1)) continue;
                art.Line(x, y0, x + 9f, y1, 1.4f, OpsInk.A(info, 0.5f));
            }
            for (int c = 0; c < 3; c++)
            {
                float x = exists ? ColumnX(c, w) : cx;
                if (!exists && c != 1) continue;
                float y = ArcY(x, cx, cy, orbitR);
                if (!exists || c != sector % 3) art.Diamond(x, y, 3f, OpsInk.A(info, 0.55f));
            }

            if (exists)
            {
                int col = Mathf.Clamp(sector, 0, 8) % 3;
                if (target >= 0 && target != sector)
                {
                    float tx = ColumnX(Mathf.Clamp(target, 0, 8) % 3, w);
                    art.DiamondOutline(tx, ArcY(tx, cx, cy, orbitR), 7f, 1.4f, info);
                }
                float mx = ColumnX(col, w), my = ArcY(mx, cx, cy, orbitR);
                DrawTruss(mx, my, false);
                DrawBoard(w);
                OpsText.Place(callsign, mx - 60f, my - (3 * CellHt + 2 * CellGap) * 0.5f - 6f - 16f, 120f, 15f);
            }
            else
            {
                DrawTruss(cx, ArcY(cx, cx, cy, orbitR), true);
            }
            art.End();
        }

        private static float ColumnX(int column, float w) => w * (0.36f + 0.2f * column);

        private void DrawTruss(float mx, float my, bool ghost)
        {
            float tw = 5 * CellW + 4 * CellGap, th = 3 * CellHt + 2 * CellGap;
            float x0 = mx - tw * 0.5f, y0 = my - th * 0.5f;
            Color hair = OpsInk.Hairline;
            art.Chamfer(x0 - 5f, y0 - 5f, tw + 10f, th + 10f, 5f, OpsInk.A(OpsInk.Sunken, 0.95f));
            art.Outline(x0 - 5f, y0 - 5f, tw + 10f, th + 10f, 1f, OpsInk.A(ghost ? hair : OpsInk.Frame, ghost ? 0.7f : 0.8f));
            for (int i = 0; i < Cells; i++)
            {
                float x = x0 + (i % 5) * (CellW + CellGap), y = y0 + (i / 5) * (CellHt + CellGap);
                byte v = ghost ? (i == 7 ? CellPending : CellNone) : cells[i];
                switch (v)
                {
                    case CellOnline: art.Box(x, y, CellW, CellHt, OpsInk.A(OpsInk.Rail(AvState.Ready), 0.9f)); break;
                    case CellCore: art.Box(x, y, CellW, CellHt, OpsInk.Ink); break;
                    case CellOffline: art.Box(x, y, CellW, CellHt, OpsInk.Rail(AvState.Danger)); break;
                    case CellHot: art.Box(x, y, CellW, CellHt, OpsInk.Rail(AvState.Caution)); break;
                    case CellPending:
                        art.Box(x, y, CellW, CellHt, OpsInk.A(OpsInk.Rail(AvState.Info), 0.30f));
                        art.Outline(x, y, CellW, CellHt, 1f, OpsInk.Rail(AvState.Info));
                        break;
                    default: art.Outline(x, y, CellW, CellHt, 1f, OpsInk.A(hair, ghost ? 0.55f : 0.35f)); break;
                }
            }
        }

        private void DrawBoard(float w)
        {
            const float c = 9f, g = 3f;
            float bx = w - 14f - (3 * c + 2 * g), by = 14f;
            for (int s = 0; s < 9; s++)
            {
                float x = bx + (s % 3) * (c + g), y = by + (s / 3) * (c + g);
                if (s == sector) art.Box(x, y, c, c, OpsInk.Rail(AvState.Ready));
                else
                {
                    art.Box(x, y, c, c, OpsInk.A(OpsInk.Sunken, 0.9f));
                    art.Outline(x, y, c, c, 1f, s == target ? OpsInk.Rail(AvState.Info) : OpsInk.A(OpsInk.Hairline, 0.7f));
                }
            }
        }
    }
}
