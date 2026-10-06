using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Modules.Wing.Networking;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>What the Station Board draws and lists for the scope's element at one refresh, as plain values (host or client mirror feed
    /// the same fields; the offline preview fills it by hand).</summary>
    internal sealed class StationData
    {
        public const int Max = WcSnapshot.MaxMembers;
        public FormationDefinition Shape;
        /// <summary>Metres between slots (the shape's range already applied).</summary>
        public float Spacing = 80f;
        public float HeadingDeg;
        /// <summary>Members are listed (rows); HavePos: their positions are known too (the host's), so the plan view can draw them.</summary>
        public bool HaveLive, HavePos;
        public int Count;
        /// <summary>Per member: the seat's number ("#2"), its slot index in the shape, its offset from the element's lead in the lead's
        /// frame (right and aft, metres), its phase, slot error (m), closure (m/s) and whether it is in the scope.</summary>
        public readonly string[] Who = new string[Max];
        public readonly int[] Slot = new int[Max];
        public readonly float[] Right = new float[Max], Aft = new float[Max], ErrorM = new float[Max];
        public readonly byte[] Phase = new byte[Max];
        public readonly sbyte[] Closure = new sbyte[Max];
        public readonly bool[] Picked = new bool[Max];

        public void Reset()
        {
            Shape = null;
            HaveLive = false;
            HavePos = false;
            Count = 0;
        }
    }

    /// <summary>A shape's slots as a small drawn glyph (the leader a filled square, each slot a chevron): the favourites strip and the
    /// shape cards. Canvas units, +Y up, inside a <paramref name="w"/> × <paramref name="h"/> box.</summary>
    internal static class ShapeGlyph
    {
        public static void Draw(AvQuadBuffer b, FormationDefinition shape, float w, float h, float pad, Rgba lead, Rgba slot)
        {
            if (shape == null) return;
            float right = 0.2f, aft = 0.2f, ahead = 0f;
            foreach (SlotDef s in shape.Slots)
            {
                right = Math.Max(right, Math.Abs(s.Right));
                if (s.Aft >= 0f) aft = Math.Max(aft, s.Aft);
                else ahead = Math.Max(ahead, -s.Aft);
            }
            float span = aft + ahead, avail = h - 2f * pad;
            float k = Math.Min((w * 0.5f - pad) / right, span > 0.05f ? avail / span : 1000f);
            k = Math.Min(k, 40f);
            float cx = w * 0.5f, top = pad + ahead * k + Math.Max(0f, (avail - span * k) * 0.5f);
            Mark(b, cx, h - top, 3.5f, lead, true);
            foreach (SlotDef s in shape.Slots) Mark(b, cx + s.Right * k, h - (top + s.Aft * k), 3.5f, slot, false);
        }

        private static void Mark(AvQuadBuffer b, float x, float y, float r, Rgba c, bool leader)
        {
            if (leader) AvStrokes.Fill(b, x - r, y - r, 2f * r, 2f * r, c);
            else AvStrokes.Chevron(b, x, y, 2.6f * r, 90f, 1.4f, c);
        }
    }

    /// <summary>FORMATION's plan view (mockup board/formation.html #planA): a bracketed card with a grid, the shape's slots as dashed boxes
    /// with an error ring, each member's live position with its closure to the slot (dashed, with its number and error when it is out of
    /// the slot), the leader, a scale bar and the heading. The drawing is domain data in a kit <see cref="AvVector"/>; the words are kit
    /// text.</summary>
    internal sealed class WmcPlanCard : AvPart
    {
        private const float CardH = 248f, SlotBox = 11f, Ring = 16f;
        private readonly AvFrame card;
        private readonly AvVector vector;
        private readonly TMP_Text caption, scaleWord, leaderWord, empty;
        private readonly TMP_Text[] slotWords = new TMP_Text[StationData.Max], liveWords = new TMP_Text[StationData.Max];
        private float width = 464f;
        private StationData data;

        public WmcPlanCard(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "PlanCard");
            card = AvFrame.Add(Rect, "Card", AvChamfer.Diagonal(8f));
            card.Bracket = 8f;
            AvLay.Fill(card.rectTransform);
            vector = AvVector.Create(Rect, "Plan", 720);
            caption = Word("Caption", TextAlignmentOptions.TopLeft);
            scaleWord = Word("Scale", TextAlignmentOptions.BottomRight);
            leaderWord = Word("Leader", TextAlignmentOptions.TopLeft);
            empty = Word("Empty", TextAlignmentOptions.Center);
            for (int i = 0; i < StationData.Max; i++)
            {
                slotWords[i] = Word("Slot" + i, TextAlignmentOptions.TopLeft);
                liveWords[i] = Word("Live" + i, TextAlignmentOptions.TopLeft);
                slotWords[i].gameObject.SetActive(false);
                liveWords[i].gameObject.SetActive(false);
            }
            Restyle();
        }

        private TMP_Text Word(string name, TextAlignmentOptions align)
        {
            TMP_Text t = AvText.Make(Rect, name, AvTextRole.Micro, "", align);
            AvText.Fit(t, false);
            return t;
        }

        public override float Measure(float w) => CardH;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            AvLay.Place(card.rectTransform, 0f, 0f, s.W, s.H);
            AvLay.Place(caption.rectTransform, 8f, 3f, s.W - 16f, 18f);
            AvLay.Place(scaleWord.rectTransform, s.W - 86f, s.H - 21f, 78f, 18f);
            AvLay.Place(empty.rectTransform, 12f, s.H * 0.5f - 10f, s.W - 24f, 20f);
            if (data != null) Draw();
        }

        /// <summary>The card shows <paramref name="d"/>; call after the data changes.</summary>
        public void Show(StationData d, string note)
        {
            data = d;
            AvText.Set(empty, d == null || d.Shape == null ? note ?? "" : "");
            Draw();
        }

        private static Rgba Rg(Color c) => c.ToRgba();

        private void Draw()
        {
            AvQuadBuffer b = vector.Buffer;
            b.Clear();
            StationData d = data;
            float w = width, h = CardH;
            Color hair = AvStyleHost.FuiColor("hairline", AvTheme.Hairline), frame = AvStyleHost.FuiColor("frame", AvTheme.Frame);
            Color key = AvStyleHost.FuiColor("info", AvTheme.RailInfo), caution = AvStyleHost.FuiColor("caution", AvTheme.RailCaution);
            Color ink = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary), dim = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            for (int i = 0; i < StationData.Max; i++)
            {
                if (slotWords[i].gameObject.activeSelf) slotWords[i].gameObject.SetActive(false);
                if (liveWords[i].gameObject.activeSelf) liveWords[i].gameObject.SetActive(false);
            }
            if (d == null || d.Shape == null)
            {
                leaderWord.gameObject.SetActive(false);
                AvText.Set(caption, "");
                AvText.Set(scaleWord, "");
                vector.Commit();
                return;
            }
            // Scale: the farthest slot, and the live aircraft, inside the card with a margin; the leader a third of the way down.
            const float mx = 46f, mt = 34f, mb = 30f;
            float up = h * 0.3f - mt, down = h * 0.7f - mb, half = w * 0.5f - mx;
            float mRight = 1f, mAft = 1f, mAhead = 0f;
            foreach (SlotDef s in d.Shape.Slots)
            {
                mRight = Math.Max(mRight, Math.Abs(s.Right * d.Spacing));
                if (s.Aft >= 0f) mAft = Math.Max(mAft, s.Aft * d.Spacing);
                else mAhead = Math.Max(mAhead, -s.Aft * d.Spacing);
            }
            float mpp = Math.Max(Math.Max(mRight / half, mAft / down), Math.Max(mAhead / up, d.Spacing / 70f));
            if (!(mpp > 0f)) mpp = 1f;
            float cx = w * 0.5f, cy = h * 0.3f;
            // Grid at a round step that keeps its lines at least 28 px apart.
            float stepM = 20f;
            foreach (float s in new[] { 20f, 40f, 80f, 160f, 320f, 640f }) { stepM = s; if (s / mpp >= 28f) break; }
            float step = stepM / mpp;
            Rgba gridC = Rg(hair).WithAlpha(0.4f);
            for (float x = cx % step; x < w; x += step) AvStrokes.Line(b, x, 0f, x, h, 0.7f, gridC);
            for (float y = (h - cy) % step; y < h; y += step) AvStrokes.Line(b, 0f, y, w, y, 0.7f, gridC);
            AvText.Set(caption, "HDG " + AvNum.Fixed(Mathf.Repeat(d.HeadingDeg, 360f), 0).PadLeft(3, '0') + "   GRID " + AvNum.Fixed(stepM, 0) + " M");
            float bar = Math.Max(d.Spacing, stepM);
            float barPx = bar / mpp;
            AvStrokes.Line(b, w - 10f - barPx, 22f, w - 10f, 22f, 1.2f, Rg(dim));
            AvText.Set(scaleWord, AvNum.Fixed(bar, 0) + " m");
            scaleWord.rectTransform.anchoredPosition = new Vector2(w - 10f - 78f, -(h - 21f - 8f));
            // Slots.
            int slots = d.Shape.Slots.Length;
            for (int i = 0; i < slots; i++)
            {
                SlotDef s = d.Shape.Slots[i];
                float sx = cx + s.Right * d.Spacing / mpp, sy = cy + s.Aft * d.Spacing / mpp;
                int mi = Member(d, i);
                bool out_ = mi >= 0 && d.Phase[mi] != (byte)StationPhase.InSlot;
                Rgba boxC = Rg(out_ ? caution : frame);
                Dashed(b, sx - SlotBox, h - (sy - SlotBox), sx + SlotBox, h - (sy - SlotBox), boxC);
                Dashed(b, sx + SlotBox, h - (sy - SlotBox), sx + SlotBox, h - (sy + SlotBox), boxC);
                Dashed(b, sx + SlotBox, h - (sy + SlotBox), sx - SlotBox, h - (sy + SlotBox), boxC);
                Dashed(b, sx - SlotBox, h - (sy + SlotBox), sx - SlotBox, h - (sy - SlotBox), boxC);
                if (mi >= 0) AvStrokes.Ring(b, sx, h - sy, Ring, 28, 0.9f, Rg(out_ ? caution : key).WithAlpha(0.55f));
                TMP_Text sw = slotWords[i];
                sw.gameObject.SetActive(true);
                AvText.Set(sw, "S" + (i + 1));
                sw.color = dim;
                AvLay.Place(sw.rectTransform, Mathf.Min(sx + 15f, w - 34f), Mathf.Max(0f, sy - 22f), 30f, 18f);
            }
            // Live members: a chevron at the offset from the lead, a dashed line to its slot while it is out of it.
            if (d.HavePos)
                for (int m = 0; m < d.Count; m++)
                {
                    // Aft is positive behind the lead, so a larger Aft is lower on the card (larger y down).
                    float lx = Mathf.Clamp(cx + d.Right[m] / mpp, 8f, w - 8f), ly = Mathf.Clamp(cy + d.Aft[m] / mpp, 8f, h - 8f);
                    bool outSlot = d.Phase[m] != (byte)StationPhase.InSlot;
                    Color c = d.Picked[m] ? AvStyleHost.FuiColor("select", AvTheme.Accent) : outSlot ? caution : key;
                    int si = d.Slot[m];
                    if (outSlot && si >= 0 && si < slots)
                    {
                        SlotDef s = d.Shape.Slots[si];
                        float sx = cx + s.Right * d.Spacing / mpp, sy = cy + s.Aft * d.Spacing / mpp;
                        AvStrokes.DashedLine(b, lx, h - ly, sx, h - sy, 4f, 3f, 1.1f, Rg(caution));
                    }
                    AvStrokes.Chevron(b, lx, h - ly, 12f, 90f, 1.6f, Rg(c));
                    TMP_Text lw = liveWords[m];
                    lw.gameObject.SetActive(true);
                    AvText.Set(lw, d.Who[m] + (outSlot ? " " + StationBoard.ErrorText(d.ErrorM[m]) : ""));
                    lw.color = outSlot ? caution : ink;
                    AvLay.Place(lw.rectTransform, Mathf.Clamp(lx + 9f, 0f, w - 112f), Mathf.Clamp(ly + 4f, 0f, h - 18f), 110f, 18f);
                }
            // The leader.
            AvStrokes.Fill(b, cx - 4f, h - cy - 4f, 8f, 8f, Rg(ink));
            leaderWord.gameObject.SetActive(true);
            AvText.Set(leaderWord, "YOU");
            leaderWord.color = ink;
            AvLay.Place(leaderWord.rectTransform, cx + 8f, cy - 20f, 40f, 18f);
            vector.Commit();
        }

        private static int Member(StationData d, int slot)
        {
            if (!d.HaveLive) return -1;
            for (int m = 0; m < d.Count; m++)
                if (d.Slot[m] == slot) return m;
            return -1;
        }

        private static void Dashed(AvQuadBuffer b, float x0, float y0, float x1, float y1, Rgba c) => AvStrokes.DashedLine(b, x0, y0, x1, y1, 3f, 2f, 0.9f, c);

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card");
            card.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            card.BracketColor = AvStyleHost.FuiFill("card-bracket", AvTheme.Frame);
            card.SetVerticesDirty();
            Color dim = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            caption.color = scaleWord.color = empty.color = dim;
            if (data != null) Draw();
        }
    }

    /// <summary>FORMATION's member rows (mockup .row.ok): number, slot, error, a bar of how far off, closure and the phase word. The rail
    /// is ready in the slot, caution while joining and danger behind.</summary>
    internal sealed class WmcStationRows : AvPart
    {
        private const float RowH = 26f, Gap = 2f, Pad = 8f;
        private const float WhoW = 26f, SlotW = 52f, ErrW = 52f, ClosureW = 56f, WordW = 62f;
        private sealed class Row
        {
            public AvFrame Frame;
            public Image Rail, Track, Fill;
            public TMP_Text Who, Slot, Err, Closure, Word;
        }

        private readonly Row[] rows = new Row[StationData.Max];
        private int shown;

        public WmcStationRows(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "StationRows");
            for (int i = 0; i < rows.Length; i++)
            {
                var r = new Row();
                r.Frame = AvFrame.Add(Rect, "Row" + i, default(AvChamfer));
                r.Frame.raycastTarget = false;
                r.Rail = AvLay.Solid(Rect, "Rail" + i, Color.clear);
                r.Track = AvLay.Solid(Rect, "Track" + i, Color.clear);
                r.Fill = AvLay.Solid(Rect, "Fill" + i, Color.clear);
                r.Who = T("Who" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
                r.Slot = T("Slot" + i, AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
                r.Err = T("Err" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight);
                r.Closure = T("Closure" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight);
                r.Word = T("Word" + i, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
                rows[i] = r;
                SetShown(r, false);
            }
            Restyle();
        }

        private TMP_Text T(string name, AvTextRole role, TextAlignmentOptions align)
        {
            TMP_Text t = AvText.Make(Rect, name, role, "", align);
            AvText.Fit(t, false);
            return t;
        }

        private static void SetShown(Row r, bool on)
        {
            if (r.Frame.gameObject.activeSelf == on) return;
            r.Frame.gameObject.SetActive(on);
            r.Rail.gameObject.SetActive(on);
            r.Track.gameObject.SetActive(on);
            r.Fill.gameObject.SetActive(on);
            r.Who.gameObject.SetActive(on);
            r.Slot.gameObject.SetActive(on);
            r.Err.gameObject.SetActive(on);
            r.Closure.gameObject.SetActive(on);
            r.Word.gameObject.SetActive(on);
        }

        public override float Measure(float width) => shown == 0 ? 0f : shown * (RowH + Gap) - Gap;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            for (int i = 0; i < shown; i++)
            {
                Row r = rows[i];
                float y = i * (RowH + Gap), x = Pad;
                AvLay.Place(r.Frame.rectTransform, 0f, y, s.W, RowH);
                AvLay.Place(r.Rail.rectTransform, 0f, y, 2f, RowH);
                AvLay.Place(r.Who.rectTransform, x, y, WhoW, RowH);
                x += WhoW + 4f;
                AvLay.Place(r.Slot.rectTransform, x, y, SlotW, RowH);
                x += SlotW + 4f;
                AvLay.Place(r.Err.rectTransform, x, y, ErrW, RowH);
                x += ErrW + 8f;
                float tail = ClosureW + 6f + WordW + Pad;
                float barW = Math.Max(20f, s.W - x - tail);
                AvLay.Place(r.Track.rectTransform, x, y + (RowH - 5f) * 0.5f, barW, 5f);
                AvLay.Place(r.Fill.rectTransform, x, y + (RowH - 5f) * 0.5f, barW * fill[i], 5f);
                x += barW + 6f;
                AvLay.Place(r.Closure.rectTransform, x, y, ClosureW, RowH);
                x += ClosureW + 6f;
                AvLay.Place(r.Word.rectTransform, x, y, s.W - x - Pad + 2f, RowH);
            }
        }

        private readonly float[] fill = new float[StationData.Max];

        /// <summary>One row per member in the scope's element, in slot order.</summary>
        public void Show(StationData d)
        {
            int n = d != null && d.HaveLive ? d.Count : 0;
            Color key = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
            for (int i = 0; i < rows.Length; i++)
            {
                SetShown(rows[i], i < n);
                if (i >= n) continue;
                Row r = rows[i];
                var phase = (StationPhase)d.Phase[i];
                string rail = phase == StationPhase.InSlot ? "ready" : phase == StationPhase.Behind ? "danger" : "caution";
                Color c = WmcState.Color(rail);
                AvText.Set(r.Who, d.Who[i]);
                AvText.Set(r.Slot, "SLOT " + (d.Slot[i] + 1));
                AvText.Set(r.Err, StationBoard.ErrorText(d.ErrorM[i]));
                AvText.Set(r.Closure, StationBoard.Signed(d.Closure[i]) + " m/s");
                AvText.Set(r.Word, StationMath.Word(phase));
                r.Rail.color = c;
                r.Fill.color = c;
                r.Word.color = phase == StationPhase.InSlot ? key : c;
                r.Closure.color = d.Closure[i] < 0 && phase != StationPhase.InSlot ? WmcState.Color("caution") : AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
                r.Err.color = phase == StationPhase.InSlot ? AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary) : c;
                // The bar is the slot error against a 150 m scale (past it, full).
                fill[i] = Mathf.Clamp01(d.ErrorM[i] / 150f);
            }
            if (n != shown)
            {
                shown = n;
                Changed();
            }
            else if (Rect != null && shown > 0)
            {
                Place(new AvSlot(0f, 0f, Rect.rect.width, Rect.rect.height));
            }
        }

        public override void Restyle()
        {
            AvStyle row = AvStyleHost.FuiStyle("row");
            Color back = AvStyleHost.Resolve(row.Background, AvTheme.SurfaceInert);
            Color track = AvStyleHost.FuiColor("frame", AvTheme.Frame).WithAlpha(0.8f);
            Color name = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
            foreach (Row r in rows)
            {
                r.Frame.Paint(back, Color.clear);
                r.Track.color = track;
                r.Who.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
                r.Slot.color = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
                r.Closure.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
                r.Err.color = name;
            }
        }
    }

    /// <summary>A row of shape cells (the favourites strip, the shape cards): each a kit button with the shape drawn above its name.</summary>
    internal sealed class WmcShapeCells : AvPart
    {
        private readonly int columns;
        private readonly float cellH, gap;
        private readonly AvControl[] cells;
        private readonly AvVector[] glyphs;
        private readonly TMP_Text[] names;
        private readonly FormationDefinition[] shapes;
        private int shown;

        public WmcShapeCells(RectTransform parent, int columnCount, int count, float height, Func<int, Action> press, float gapPx = 4f)
        {
            Rect = AvLay.Child(parent, "ShapeCells");
            columns = columnCount;
            cellH = height;
            gap = gapPx;
            cells = new AvControl[count];
            glyphs = new AvVector[count];
            names = new TMP_Text[count];
            shapes = new FormationDefinition[count];
            for (int i = 0; i < count; i++)
            {
                int k = i;
                cells[i] = AvControl.Make(Rect, new AvControl.Spec("", () => press(k)()));
                glyphs[i] = AvVector.Create(cells[i].Rect, "Glyph", 24);
                names[i] = AvText.Make(cells[i].Rect, "Name", AvTextRole.Micro, "", TextAlignmentOptions.Bottom);
                AvText.Fit(names[i], false);
                cells[i].gameObject.SetActive(false);
            }
            Restyle();
        }

        public AvControl this[int i] => cells[i];

        public int Count => cells.Length;

        public int Shown => shown;

        public bool Set(int i, FormationDefinition shape, string name, bool latched)
        {
            shapes[i] = shape;
            AvText.Set(names[i], name);
            cells[i].Latched = latched;
            Draw(i);
            return true;
        }

        public void SetShown(int n)
        {
            n = Mathf.Clamp(n, 0, cells.Length);
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].gameObject.activeSelf != (i < n)) cells[i].gameObject.SetActive(i < n);
            if (n == shown) return;
            shown = n;
            Changed();
        }

        private void Draw(int i)
        {
            if (shapes[i] == null) return;
            AvQuadBuffer b = glyphs[i].Buffer;
            b.Clear();
            float w = cells[i].Rect.rect.width, h = cellH - 20f;
            if (w <= 1f) w = 80f;
            Color key = AvStyleHost.FuiColor("info", AvTheme.RailInfo), ink = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            // The glyph sits above the name: shift its box up by the name's height.
            ShapeGlyph.Draw(b, shapes[i], w, h, 8f, ink.ToRgba(), key.ToRgba());
            glyphs[i].Commit();
        }

        public override float Measure(float width)
        {
            int rowsUsed = (shown + columns - 1) / Math.Max(1, columns);
            return rowsUsed == 0 ? 0f : rowsUsed * cellH + (rowsUsed - 1) * gap;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = AvFlowMath.ColumnWidth(s.W, columns, gap);
            for (int i = 0; i < cells.Length; i++)
            {
                float x = i % columns * (w + gap), y = i / columns * (cellH + gap);
                AvLay.Place(cells[i].Rect, x, y, w, cellH);
                AvLay.Place(names[i].rectTransform, 2f, cellH - 19f, w - 4f, 18f);
                glyphs[i].rectTransform.offsetMin = new Vector2(0f, 18f);
                glyphs[i].rectTransform.offsetMax = Vector2.zero;
                if (shapes[i] != null) Draw(i);
            }
        }

        public override void Restyle()
        {
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].Restyle();
                names[i].color = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
                if (shapes[i] != null) Draw(i);
            }
        }
    }

    /// <summary>FORMATION's spacing row (mockup .slider): − and +, and between them a track over the shape's own range with a mark at each
    /// preset that fits it and a thumb at the one flown. The presets (40, 80, 160, 350 m) are what the wing's SetSpacing order takes; one
    /// outside the shape's range is the range's end, so it is left off the track.</summary>
    internal sealed class WmcSpacingRow : AvPart
    {
        private const float RowH = 46f, ButtonW = 40f, TrackY = 6f, TrackH = 20f;
        private readonly AvControl minus, plus;
        private readonly AvFrame track;
        private readonly Image thumb;
        private readonly Image[] ticks = new Image[4];
        private readonly TMP_Text[] words = new TMP_Text[4];
        private readonly TMP_Text value;
        private float min = 40f, max = 160f, now = 80f;
        private readonly bool[] valid = new bool[4];
        private readonly float[] metres = new float[4];

        public AvControl Minus => minus;
        public AvControl Plus => plus;

        public WmcSpacingRow(RectTransform parent, Action onMinus, Action onPlus)
        {
            Rect = AvLay.Child(parent, "SpacingRow");
            minus = AvControl.Make(Rect, new AvControl.Spec("", onMinus, AvButtonStyle.Quiet, AvIcon.Minus));
            plus = AvControl.Make(Rect, new AvControl.Spec("", onPlus, AvButtonStyle.Quiet, AvIcon.Plus));
            track = AvFrame.Add(Rect, "Track", default(AvChamfer));
            track.raycastTarget = false;
            thumb = AvLay.Solid(Rect, "Thumb", Color.clear);
            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i] = AvLay.Solid(Rect, "Tick" + i, Color.clear);
                words[i] = AvText.Make(Rect, "Word" + i, AvTextRole.Micro, "", TextAlignmentOptions.Top);
                AvText.Fit(words[i], false);
            }
            value = AvText.Make(Rect, "Value", AvTextRole.DataStrong, "", TextAlignmentOptions.Center);
            AvText.Fit(value, false);
            Restyle();
        }

        public override float Measure(float width) => RowH;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(minus.Rect, 0f, TrackY - 2f, ButtonW, TrackH + 4f);
            AvLay.Place(plus.Rect, s.W - ButtonW, TrackY - 2f, ButtonW, TrackH + 4f);
            Layout();
        }

        /// <summary>The shape's range, the metres now and the preset metres (and which fit the range).</summary>
        public void Show(float rangeMin, float rangeMax, float metresNow, float[] presetMetres)
        {
            min = rangeMin;
            max = Math.Max(rangeMax, rangeMin + 1f);
            now = metresNow;
            for (int i = 0; i < 4; i++)
            {
                metres[i] = presetMetres[i];
                valid[i] = presetMetres[i] >= min - 0.5f && presetMetres[i] <= max + 0.5f;
            }
            Layout();
        }

        private void Layout()
        {
            if (Rect == null || Rect.rect.width <= 1f) return;
            float x0 = ButtonW + 8f, x1 = Rect.rect.width - ButtonW - 8f, w = x1 - x0;
            AvLay.Place(track.rectTransform, x0, TrackY, w, TrackH);
            for (int i = 0; i < 4; i++)
            {
                bool on = valid[i];
                ticks[i].gameObject.SetActive(on);
                words[i].gameObject.SetActive(on);
                if (!on) continue;
                float x = x0 + w * Mathf.Clamp01((metres[i] - min) / (max - min));
                AvLay.Place(ticks[i].rectTransform, x - 0.5f, TrackY + 6f, 1f, TrackH - 12f);
                AvText.Set(words[i], AvNum.Fixed(metres[i], 0));
                AvLay.Place(words[i].rectTransform, Mathf.Clamp(x - 20f, x0 - 8f, x1 - 32f), TrackY + TrackH + 1f, 40f, 18f);
            }
            float tx = x0 + w * Mathf.Clamp01((now - min) / (max - min));
            AvLay.Place(thumb.rectTransform, tx - 2f, TrackY - 3f, 4f, TrackH + 6f);
            AvText.Set(value, AvNum.Fixed(now, 0) + " M");
            AvLay.Place(value.rectTransform, x0, TrackY, w, TrackH);
            // The value sits on the track's middle; move it off the thumb by putting it on the side with more room.
            value.alignment = tx < x0 + w * 0.5f ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
            AvLay.Place(value.rectTransform, tx < x0 + w * 0.5f ? tx + 10f : x0 + 8f, TrackY, tx < x0 + w * 0.5f ? x1 - tx - 14f : tx - x0 - 18f, TrackH);
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("row");
            track.Paint(AvStyleHost.FuiColor("ground", AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            thumb.color = AvStyleHost.FuiColor("select", AvTheme.Accent);
            Color frame = AvStyleHost.FuiColor("frame", AvTheme.Frame), dim = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i].color = frame;
                words[i].color = dim;
            }
            value.color = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
            minus.Restyle();
            plus.Restyle();
        }
    }
}
