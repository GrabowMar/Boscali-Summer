using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>What the Flight Plan draws and lists at one refresh (spec 2026-10-04 §4.2): the route's points in world metres (x east, z
    /// north), where the flight is, and each point's leg as words. The draft is the source while it has points, else the scope's own
    /// path task (read-only).</summary>
    internal sealed class RouteData
    {
        public const int Max = RouteDraft.MaxPoints;
        public int Count, Selected = -1, Current = -1;
        public readonly float[] X = new float[Max], Z = new float[Max];
        public bool HaveFrom, HaveLead, Closing, ReadOnly;
        public float FromX, FromZ, LeadX, LeadZ, LeadHeading;
        public readonly string[] Brg = new string[Max], Dist = new string[Max], Alt = new string[Max], Spd = new string[Max],
            At = new string[Max], Eta = new string[Max];

        public void Reset()
        {
            Count = 0;
            Selected = Current = -1;
            HaveFrom = HaveLead = Closing = ReadOnly = false;
        }
    }

    /// <summary>The Flight Plan's mini map (mockup board/route.html #mapR1): a bracketed card with a grid, the route as numbered legs, the
    /// selected point ringed, legs already flown dimmed, a loop's closing leg dashed and the flight's own position as a chevron. North is
    /// up; the scale fits the route. The drawing is a kit <see cref="AvVector"/>, the numbers are kit text.</summary>
    internal sealed class WmcRouteMap : AvPart
    {
        private const float CardH = 200f, Margin = 26f;
        private readonly AvFrame card;
        private readonly AvVector vector;
        private readonly TMP_Text caption, empty;
        private readonly TMP_Text[] numbers = new TMP_Text[RouteData.Max];
        private float width = 464f;
        private RouteData data;

        public WmcRouteMap(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "RouteMap");
            card = AvFrame.Add(Rect, "Card", AvChamfer.Diagonal(8f));
            card.Bracket = 8f;
            AvLay.Fill(card.rectTransform);
            vector = AvVector.Create(Rect, "Route", 400);
            caption = AvText.Make(Rect, "Caption", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft);
            AvText.Fit(caption, false);
            empty = AvText.Make(Rect, "Empty", AvTextRole.Label, "", TextAlignmentOptions.Center);
            AvText.Fit(empty, false);
            for (int i = 0; i < numbers.Length; i++)
            {
                numbers[i] = AvText.Make(Rect, "No" + i, AvTextRole.Micro, (i + 1).ToString(), TextAlignmentOptions.TopLeft);
                AvText.Fit(numbers[i], false);
                numbers[i].gameObject.SetActive(false);
            }
            Restyle();
        }

        public override float Measure(float w) => CardH;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            AvLay.Place(card.rectTransform, 0f, 0f, s.W, s.H);
            AvLay.Place(caption.rectTransform, 8f, 3f, s.W - 16f, 18f);
            AvLay.Place(empty.rectTransform, 12f, s.H * 0.5f - 12f, s.W - 24f, 24f);
            if (data != null) Draw();
        }

        public void Show(RouteData d, string emptyWord)
        {
            data = d;
            AvText.Set(empty, d == null || d.Count == 0 ? emptyWord ?? "" : "");
            Draw();
        }

        private static Rgba Rg(Color c) => c.ToRgba();

        private void Draw()
        {
            AvQuadBuffer b = vector.Buffer;
            b.Clear();
            RouteData d = data;
            float w = width, h = CardH;
            Color hair = AvStyleHost.FuiColor("hairline", AvTheme.Hairline), key = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
            Color ink = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary), dim = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            Color caution = AvStyleHost.FuiColor("caution", AvTheme.RailCaution), select = AvStyleHost.FuiColor("select", AvTheme.Accent);
            foreach (TMP_Text t in numbers)
                if (t.gameObject.activeSelf) t.gameObject.SetActive(false);
            if (d == null || d.Count == 0)
            {
                AvText.Set(caption, "");
                vector.Commit();
                return;
            }
            // Fit the points, where the flight starts from and where it is.
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < d.Count; i++) Grow(d.X[i], d.Z[i], ref minX, ref maxX, ref minZ, ref maxZ);
            if (d.HaveFrom) Grow(d.FromX, d.FromZ, ref minX, ref maxX, ref minZ, ref maxZ);
            if (d.HaveLead) Grow(d.LeadX, d.LeadZ, ref minX, ref maxX, ref minZ, ref maxZ);
            float spanX = Math.Max(maxX - minX, 2000f), spanZ = Math.Max(maxZ - minZ, 2000f);
            float scale = Math.Min((w - 2f * Margin) / spanX, (h - 2f * Margin - 8f) / spanZ);
            float ox = w * 0.5f - (minX + maxX) * 0.5f * scale, oz = h * 0.5f - 4f - (minZ + maxZ) * 0.5f * scale;
            // Grid: a round number of kilometres at least 30 px apart.
            float stepKm = 1f;
            foreach (float s in new[] { 1f, 2f, 5f, 10f, 20f, 50f, 100f }) { stepKm = s; if (s * 1000f * scale >= 30f) break; }
            float step = stepKm * 1000f * scale;
            Rgba gridC = Rg(hair).WithAlpha(0.4f);
            float gx0 = ox % step;
            if (gx0 < 0f) gx0 += step;
            float gz0 = oz % step;
            if (gz0 < 0f) gz0 += step;
            for (float x = gx0; x < w; x += step) AvStrokes.Line(b, x, 0f, x, h, 0.7f, gridC);
            for (float y = gz0; y < h; y += step) AvStrokes.Line(b, 0f, y, w, y, 0.7f, gridC);
            AvText.Set(caption, "N ↑   " + AvNum.Fixed(stepKm, 0) + " KM GRID");
            float prevX = d.HaveFrom ? d.FromX * scale + ox : 0f, prevY = d.HaveFrom ? d.FromZ * scale + oz : 0f;
            bool havePrev = d.HaveFrom;
            for (int i = 0; i < d.Count; i++)
            {
                float x = d.X[i] * scale + ox, y = d.Z[i] * scale + oz;
                bool done = d.Current >= 0 && i < d.Current, now = i == d.Current;
                Color c = done ? dim : now ? ink : key;
                float lw = now ? 2f : 1.4f;
                if (havePrev)
                {
                    if (done) AvStrokes.DashedLine(b, prevX, prevY, x, y, 4f, 3f, 1.1f, Rg(c));
                    else AvStrokes.Line(b, prevX, prevY, x, y, lw, Rg(c));
                }
                prevX = x;
                prevY = y;
                havePrev = true;
            }
            if (d.Closing && d.Count > 2)
                AvStrokes.DashedLine(b, prevX, prevY, d.X[0] * scale + ox, d.Z[0] * scale + oz, 4f, 3f, 1.1f, Rg(key).WithAlpha(0.7f));
            for (int i = 0; i < d.Count; i++)
            {
                float x = d.X[i] * scale + ox, y = d.Z[i] * scale + oz;
                bool done = d.Current >= 0 && i < d.Current, sel = i == d.Selected;
                Color c = sel ? select : done ? dim : key;
                AvStrokes.Fill(b, x - 3f, y - 3f, 6f, 6f, Rg(c));
                if (sel) AvStrokes.Ring(b, x, y, 9f, 20, 1.2f, Rg(select));
                TMP_Text n = numbers[i];
                n.gameObject.SetActive(true);
                n.color = sel ? select : done ? dim : ink;
                AvLay.Place(n.rectTransform, Mathf.Clamp(x + 8f, 0f, w - 24f), Mathf.Clamp(h - y - 19f, 0f, h - 18f), 24f, 18f);
            }
            if (d.HaveLead)
            {
                float lx = d.LeadX * scale + ox, ly = d.LeadZ * scale + oz;
                // Heading is clockwise from north; the chevron's angle counter-clockwise from east.
                AvStrokes.Chevron(b, lx, ly, 14f, 90f - d.LeadHeading, 1.8f, Rg(ink));
            }
            vector.Commit();
        }

        private static void Grow(float x, float z, ref float minX, ref float maxX, ref float minZ, ref float maxZ)
        {
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minZ = Math.Min(minZ, z);
            maxZ = Math.Max(maxZ, z);
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card");
            card.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            card.BracketColor = AvStyleHost.FuiFill("card-bracket", AvTheme.Frame);
            card.SetVerticesDirty();
            caption.color = empty.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            if (data != null) Draw();
        }
    }

    /// <summary>The Flight Plan's leg table (mockup .leg): # · BRG · DIST · ALT · SPD · AT POINT · ETA for every point, the one being
    /// flown marked, flown legs dimmed, the selected point lit; a click selects a point (the steppers then edit it).</summary>
    internal sealed class WmcLegTable : AvPart
    {
        private const float RowH = 21f, HeadH = 18f, Gap = 1f, Pad = 8f;
        private const float NumW = 22f, BrgW = 44f, DistW = 64f, AltW = 46f, SpdW = 42f, EtaW = 40f;

        private sealed class Row
        {
            public AvFrame Frame;
            public Image Rail;
            public TMP_Text No, Brg, Dist, Alt, Spd, At, Eta;
            public bool Hover;
            public int State;
        }

        private readonly Row[] rows = new Row[RouteData.Max];
        private readonly TMP_Text[] heads = new TMP_Text[7];
        private int shown;
        private Action<int> picked;

        public WmcLegTable(RectTransform parent, Action<int> pick)
        {
            Rect = AvLay.Child(parent, "LegTable");
            picked = pick;
            string[] words = { "#", "BRG", "DIST", "ALT", "SPD", "AT POINT", "ETA" };
            for (int i = 0; i < heads.Length; i++)
            {
                heads[i] = AvText.Make(Rect, "Head" + i, AvTextRole.Micro, words[i], TextAlignmentOptions.MidlineLeft);
                AvText.Fit(heads[i], false);
            }
            for (int i = 0; i < rows.Length; i++)
            {
                int k = i;
                var r = new Row();
                r.Frame = AvFrame.Add(Rect, "Row" + i, default(AvChamfer));
                AvHit hit = AvHit.On(r.Frame);
                hit.Hover = h => { r.Hover = h; Paint(r); };
                hit.Click = e => picked(k);
                AvHelpTip.Attach(r.Frame.gameObject, "Select this point to change its altitude, speed or arrival action.");
                r.Rail = AvLay.Solid(Rect, "Rail" + i, Color.clear);
                r.No = T("No" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
                r.Brg = T("Brg" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
                r.Dist = T("Dist" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
                r.Alt = T("Alt" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
                r.Spd = T("Spd" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
                r.At = T("At" + i, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
                r.Eta = T("Eta" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight);
                rows[i] = r;
                SetShown(r, false);
            }
            Restyle();
        }

        public Transform RowTarget(int i) => rows[i].Frame.transform;

        private TMP_Text T(string name, AvTextRole role, TextAlignmentOptions align)
        {
            TMP_Text t = AvText.Make(Rect, name, role, "", align);
            AvText.Fit(t, false);
            return t;
        }

        private static void SetShown(Row r, bool on)
        {
            if (r.Frame.gameObject.activeSelf == on) return;
            foreach (Component c in new Component[] { r.Frame, r.Rail, r.No, r.Brg, r.Dist, r.Alt, r.Spd, r.At, r.Eta })
                c.gameObject.SetActive(on);
        }

        public override float Measure(float width) => shown == 0 ? 0f : HeadH + Gap + shown * (RowH + Gap) - Gap;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float[] xs = ColumnX(s.W, out float atW);
            float[] ws = { NumW, BrgW, DistW, AltW, SpdW, atW, EtaW };
            for (int i = 0; i < heads.Length; i++)
            {
                heads[i].gameObject.SetActive(shown > 0);
                AvLay.Place(heads[i].rectTransform, xs[i], 0f, ws[i], HeadH);
            }
            heads[6].alignment = TextAlignmentOptions.MidlineRight;
            for (int i = 0; i < shown; i++)
            {
                Row r = rows[i];
                float y = HeadH + Gap + i * (RowH + Gap);
                AvLay.Place(r.Frame.rectTransform, 0f, y, s.W, RowH);
                AvLay.Place(r.Rail.rectTransform, 0f, y, 2f, RowH);
                AvLay.Place(r.No.rectTransform, xs[0], y, ws[0], RowH);
                AvLay.Place(r.Brg.rectTransform, xs[1], y, ws[1], RowH);
                AvLay.Place(r.Dist.rectTransform, xs[2], y, ws[2], RowH);
                AvLay.Place(r.Alt.rectTransform, xs[3], y, ws[3], RowH);
                AvLay.Place(r.Spd.rectTransform, xs[4], y, ws[4], RowH);
                AvLay.Place(r.At.rectTransform, xs[5], y, ws[5], RowH);
                AvLay.Place(r.Eta.rectTransform, xs[6], y, ws[6], RowH);
            }
        }

        private static float[] ColumnX(float w, out float atW)
        {
            var xs = new float[7];
            float x = Pad;
            float fixedW = NumW + BrgW + DistW + AltW + SpdW + EtaW + 6f * 4f + 2f * Pad;
            atW = Math.Max(40f, w - fixedW);
            float[] ws = { NumW, BrgW, DistW, AltW, SpdW, atW, EtaW };
            for (int i = 0; i < 7; i++)
            {
                xs[i] = x;
                x += ws[i] + 4f;
            }
            return xs;
        }

        /// <summary>The table shows <paramref name="d"/>'s rows.</summary>
        public void Show(RouteData d)
        {
            int n = d != null ? d.Count : 0;
            for (int i = 0; i < rows.Length; i++)
            {
                SetShown(rows[i], i < n);
                if (i >= n) continue;
                Row r = rows[i];
                AvText.Set(r.No, (i + 1).ToString());
                AvText.Set(r.Brg, d.Brg[i]);
                AvText.Set(r.Dist, d.Dist[i]);
                AvText.Set(r.Alt, d.Alt[i]);
                AvText.Set(r.Spd, d.Spd[i]);
                AvText.Set(r.At, d.At[i]);
                AvText.Set(r.Eta, d.Eta[i]);
                r.State = i == d.Selected ? 3 : d.Current >= 0 && i == d.Current ? 2 : d.Current >= 0 && i < d.Current ? 1 : 0;
                Paint(r);
            }
            if (n != shown)
            {
                shown = n;
                Changed();
            }
        }

        private void Paint(Row r)
        {
            Color inert = AvStyleHost.FuiFill("row", AvTheme.SurfaceInert);
            Color select = AvStyleHost.FuiColor("select", AvTheme.Accent), ink = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            Color hair = AvStyleHost.FuiColor("hairline", AvTheme.Hairline), key = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
            Color dim = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            Color back = r.State == 3 ? Color.Lerp(inert, select, 0.18f) : r.Hover ? Color.Lerp(inert, Color.white, 0.07f) : inert;
            r.Frame.Paint(back, Color.clear);
            r.Rail.color = r.State == 3 ? select : r.State == 2 ? ink : hair;
            Color text = r.State == 1 ? AvStyleHost.FuiColor("muted", AvTheme.Disabled) : ink;
            foreach (TMP_Text t in new[] { r.No, r.Brg, r.Dist, r.Alt, r.Spd, r.Eta }) t.color = text;
            r.At.color = r.State == 1 ? text : key;
            r.No.color = r.State == 1 ? text : dim;
        }

        public override void Restyle()
        {
            Color key = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
            foreach (TMP_Text h in heads) h.color = key;
            foreach (Row r in rows) Paint(r);
        }
    }
}
