using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    // Read-only NATION blocks of the FAC ECONOMY page. Each part owns a pool of rows it grows on demand and
    // hides when spare; a height change goes through Changed() so the flow re-lays only when it really moved.

    /// <summary>Stock: one row per airframe or vehicle line, a bar of Count against Target, and c/t.</summary>
    internal sealed class FacStockPart : AvPart
    {
        public const int MaxRows = 10;
        private const float RowH = 22f;

        private sealed class Row
        {
            public TMP_Text Icon, Name, Count;
            public Image Track, Fill;
        }

        private readonly List<Row> rows = new List<Row>(MaxRows);
        private int shown;

        public FacStockPart(RectTransform parent) { Rect = AvLay.Child(parent, "FacStock"); }

        public override float Measure(float width) => shown == 0 ? RowH : shown * RowH;

        public override void Place(AvSlot s) { base.Place(s); Layout(s.W); }

        private void Layout(float width)
        {
            float nameW = width * .42f, barW = width * .34f, countW = width - 20f - nameW - barW - 12f;
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                float y = i * RowH;
                AvLay.Place(r.Icon.rectTransform, 0f, y + 4f, 14f, 14f);
                AvLay.Place(r.Name.rectTransform, 20f, y, nameW, RowH);
                AvLay.Place(r.Track.rectTransform, 20f + nameW + 4f, y + 8f, barW, 6f);
                AvLay.Place(r.Fill.rectTransform, 20f + nameW + 4f, y + 8f, barW * Mathf.Clamp01(r.Fill.fillAmount), 6f);
                AvLay.Place(r.Count.rectTransform, 20f + nameW + barW + 8f, y, countW, RowH);
            }
        }

        public override void Restyle() { }

        private Row AddRow()
        {
            var r = new Row
            {
                Icon = AvIcons.Make(Rect, AvIcon.Plane, AvGridTokens.IconInline, Color.white),
                Name = AvText.Make(Rect, "Name", AvTextRole.DataSmall),
                Track = AvLay.Solid(Rect, "Track", AvTheme.SurfaceInert),
                Fill = AvLay.Solid(Rect, "Fill", AvTheme.RailInfo),
                Count = AvText.Make(Rect, "Count", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight),
            };
            AvText.Fit(r.Name, false);
            rows.Add(r);
            return r;
        }

        public void Set(NationView v)
        {
            int n = Mathf.Min(MaxRows, v.Stock.Count);
            bool placed = false;
            while (rows.Count < n) { AddRow(); placed = true; }
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                bool on = i < n;
                if (r.Name.gameObject.activeSelf != on)
                {
                    r.Icon.gameObject.SetActive(on); r.Name.gameObject.SetActive(on); r.Track.gameObject.SetActive(on);
                    r.Fill.gameObject.SetActive(on); r.Count.gameObject.SetActive(on);
                }
                if (!on) continue;
                StockView st = v.Stock[i];
                AvIcons.Set(r.Icon, st.Aircraft ? AvIcon.Plane : AvIcon.Tank, AvGridTokens.IconInline);
                float share = st.Target > 0 ? Mathf.Clamp01(st.Count / (float)st.Target) : 1f;
                Color tone = st.Count * 2 < st.Target ? AvTheme.RailDanger : st.Count >= st.Target ? AvTheme.RailReady : AvTheme.RailInfo;
                r.Icon.color = tone; r.Fill.color = tone; r.Count.color = st.Count * 2 < st.Target ? AvTheme.RailDanger : AvInk.Ink;
                r.Name.color = st.Count * 2 < st.Target ? AvTheme.RailDanger : AvInk.Ink;
                AvText.Set(r.Name, (st.Name ?? "").ToUpperInvariant());
                AvText.Set(r.Count, st.Count + "/" + st.Target);
                if (!Mathf.Approximately(r.Fill.fillAmount, share))
                {
                    r.Fill.fillAmount = share;
                    placed = true;
                }
            }
            if (n != shown) { shown = n; Changed(); placed = true; }
            if (placed && Rect != null) Layout(Rect.rect.width);
        }
    }

    /// <summary>Plants: a four-wide grid of tiles, one per factory, then the building site and the free slots.</summary>
    internal sealed class FacPlantsPart : AvPart
    {
        private const int Columns = 4, MaxTiles = 12;
        private const float TileH = 58f, Gap = 5f;

        private sealed class Tile
        {
            public AvFrame Frame;
            public TMP_Text Top, Glyph, Line, Next;
            public Image Track, Fill;
        }

        private readonly List<Tile> tiles = new List<Tile>(MaxTiles);
        private int shown;

        public FacPlantsPart(RectTransform parent) { Rect = AvLay.Child(parent, "FacPlants"); }

        public override float Measure(float width)
        {
            int rowsUsed = Mathf.Max(1, (shown + Columns - 1) / Columns);
            return rowsUsed * TileH + (rowsUsed - 1) * Gap;
        }

        public override void Place(AvSlot s) { base.Place(s); Layout(s.W); }

        private void Layout(float width)
        {
            float w = (width - (Columns - 1) * Gap) / Columns;
            for (int i = 0; i < tiles.Count; i++)
            {
                Tile t = tiles[i];
                float x = (i % Columns) * (w + Gap), y = (i / Columns) * (TileH + Gap);
                AvLay.Place(t.Frame.rectTransform, x, y, w, TileH);
                AvLay.Place(t.Top.rectTransform, x + 6f, y + 3f, w - 28f, 16f);
                AvLay.Place(t.Glyph.rectTransform, x + w - 20f, y + 4f, 14f, 14f);
                AvLay.Place(t.Line.rectTransform, x + 6f, y + 20f, w - 10f, 13f);
                AvLay.Place(t.Next.rectTransform, x + 6f, y + 33f, w - 10f, 12f);
                AvLay.Place(t.Track.rectTransform, x + 6f, y + TileH - 9f, w - 12f, 4f);
                AvLay.Place(t.Fill.rectTransform, x + 6f, y + TileH - 9f, (w - 12f) * Mathf.Clamp01(t.Fill.fillAmount), 4f);
            }
        }

        public override void Restyle() { }

        private Tile AddTile()
        {
            var t = new Tile
            {
                Frame = AvFrame.Add(Rect, "Tile", AvChamfer.Diagonal(4f)),
                Top = AvText.Make(Rect, "Top", AvTextRole.DataSmall),
                Glyph = AvIcons.Make(Rect, AvIcon.Circle, AvGridTokens.IconInline, Color.white),
                Line = AvText.Make(Rect, "Line", AvTextRole.Micro),
                Next = AvText.Make(Rect, "Next", AvTextRole.Micro),
                Track = AvLay.Solid(Rect, "Track", AvTheme.SurfaceInert),
                Fill = AvLay.Solid(Rect, "Fill", AvTheme.RailInfo),
            };
            AvText.Fit(t.Top, false); AvText.Fit(t.Line, false); AvText.Fit(t.Next, false);
            tiles.Add(t);
            return t;
        }

        private static void Paint(Tile t, Color accent, bool dim) =>
            t.Frame.Paint(dim ? AvTheme.SurfaceInert : AvTheme.Surface, accent);

        public void Set(NationView v)
        {
            int plants = Mathf.Min(v.Plants.Count, MaxTiles);
            int building = v.Building && plants < MaxTiles ? 1 : 0;
            int free = Mathf.Clamp(v.Slots - plants - building, 0, MaxTiles - plants - building);
            int n = plants + building + free;
            while (tiles.Count < n) AddTile();
            for (int i = 0; i < tiles.Count; i++)
            {
                Tile t = tiles[i];
                bool on = i < n;
                if (t.Frame.gameObject.activeSelf != on)
                {
                    t.Frame.gameObject.SetActive(on); t.Top.gameObject.SetActive(on); t.Glyph.gameObject.SetActive(on);
                    t.Line.gameObject.SetActive(on); t.Next.gameObject.SetActive(on); t.Track.gameObject.SetActive(on); t.Fill.gameObject.SetActive(on);
                }
                if (!on) continue;
                if (i < plants) SetPlant(t, v, v.Plants[i], i + 1);
                else if (i < plants + building) SetBuilding(t, v, i + 1);
                else SetFree(t);
            }
            if (n != shown) { shown = n; Changed(); }
            if (Rect != null) Layout(Rect.rect.width);
        }

        private static void SetPlant(Tile t, NationView v, PlantView p, int number)
        {
            bool hit = p.Health01 < .6f;
            Color tone = hit ? AvTheme.RailDanger : AvTheme.RailInfo;
            string line = p.Kind == LineKind.Construction ? "CONSTRUCTION"
                : p.Kind == LineKind.Idle || p.Row < 0 || p.Row >= v.Stock.Count ? "IDLE"
                : (v.Stock[p.Row].Name ?? "").ToUpperInvariant();
            Paint(t, hit ? AvTheme.RailDanger.WithAlpha(.8f) : AvTheme.Hairline, p.Kind == LineKind.Idle);
            AvText.Set(t.Top, hit ? number + " HIT" : number.ToString());
            t.Top.color = hit ? AvTheme.RailDanger : AvInk.Ink;
            AvIcons.Set(t.Glyph, p.Kind == LineKind.Aircraft ? AvIcon.Plane : p.Kind == LineKind.Vehicle ? AvIcon.Tank
                : p.Kind == LineKind.Construction ? AvIcon.BuildingFactory : AvIcon.Circle, AvGridTokens.IconInline);
            t.Glyph.color = p.Kind == LineKind.Construction ? AvTheme.RailReady : tone;
            AvText.Set(t.Line, line); t.Line.color = AvInk.Ink;
            AvText.Set(t.Next, p.Kind == LineKind.Idle ? "" : p.NextIn > 0f ? "NEXT " + AvNum.Clock(Mathf.Ceil(p.NextIn)) : "RESTING");
            t.Next.color = AvInk.Dim;
            SetBar(t, Mathf.Clamp01(p.Health01), tone);
        }

        private static void SetBuilding(Tile t, NationView v, int number)
        {
            Paint(t, AvTheme.RailReady, false);
            AvText.Set(t.Top, number + " · " + AvNum.Clock(Mathf.Ceil(v.BuildLeft)));
            t.Top.color = AvTheme.RailReady;
            AvIcons.Set(t.Glyph, AvIcon.BuildingFactory, AvGridTokens.IconInline);
            t.Glyph.color = AvTheme.RailReady;
            AvText.Set(t.Line, "BUILDING"); t.Line.color = AvInk.Ink;
            AvText.Set(t.Next, "");
            SetBar(t, v.BuildTotal > 0f ? Mathf.Clamp01(1f - v.BuildLeft / v.BuildTotal) : 0f, AvTheme.RailReady);
        }

        private static void SetFree(Tile t)
        {
            Paint(t, AvTheme.Hairline, true);
            AvText.Set(t.Top, "");
            AvIcons.Set(t.Glyph, AvIcon.Plus, AvGridTokens.IconInline);
            t.Glyph.color = AvTheme.Disabled;
            AvText.Set(t.Line, "FREE SLOT"); t.Line.color = AvTheme.Disabled;
            AvText.Set(t.Next, "");
            SetBar(t, 0f, AvTheme.Disabled);
        }

        private static void SetBar(Tile t, float share, Color tone)
        {
            t.Fill.color = tone;
            t.Fill.fillAmount = share;   // read back by Place for the width
        }
    }

    /// <summary>In force: the economy and priority policies, stability, and the total output (politics' SURGE PRODUCTION included).</summary>
    internal sealed class FacForcePart : AvPart
    {
        private const float RowH = 20f;
        private const int MaxRows = 4;

        private readonly TMP_Text[] left = new TMP_Text[MaxRows], right = new TMP_Text[MaxRows];
        private int shown = 3;

        public FacForcePart(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "FacForce");
            for (int i = 0; i < MaxRows; i++)
            {
                left[i] = AvText.Make(Rect, "L" + i, AvTextRole.DataSmall); AvText.Fit(left[i], false);
                right[i] = AvText.Make(Rect, "R" + i, AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight); AvText.Fit(right[i], false);
            }
        }

        public override float Measure(float width) => shown * RowH;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            for (int i = 0; i < MaxRows; i++)
            {
                AvLay.Place(left[i].rectTransform, 0f, i * RowH, s.W * .58f, RowH);
                AvLay.Place(right[i].rectTransform, s.W * .58f, i * RowH, s.W * .42f, RowH);
            }
        }

        public override void Restyle() { }

        private int n;

        private void Line(string name, string value, Color tone)
        {
            AvText.Set(left[n], name); left[n].color = tone;
            AvText.Set(right[n], value); right[n].color = AvInk.Ink;
            n++;
        }

        public void Set(NationView v)
        {
            n = 0;
            string eco = NationWords.EconomyWord(v.Economy) + (v.EconomyLock > 0f ? "  ·  " + AvNum.Clock(Mathf.Ceil(v.EconomyLock)) : "");
            string pri = NationWords.PriorityWord(v.Priority) + (v.PriorityLock > 0f ? "  ·  " + AvNum.Clock(Mathf.Ceil(v.PriorityLock)) : "");
            Line("ECONOMY", eco, AvTheme.RailInfo);
            Line("PRIORITY", pri, AvTheme.RailInfo);
            Line("STABILITY " + Mathf.RoundToInt(v.Stability), "", AvInk.Dim);
            Line("TOTAL OUTPUT", "x" + AvNum.Fixed(v.Output, 2), AvTheme.RailCaution);
            for (int i = n; i < MaxRows; i++) { AvText.Set(left[i], ""); AvText.Set(right[i], ""); }
            if (n != shown) { shown = n; Changed(); }
        }
    }
}
