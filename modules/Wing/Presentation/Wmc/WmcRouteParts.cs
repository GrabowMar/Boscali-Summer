using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>What the Flight Plan lists at one refresh (spec 2026-10-04 §4.2): each route point's altitude, speed and arrival action as
    /// words, the point being flown and the one selected. The draft is the source while it has points, else the scope's own path task
    /// (read-only).</summary>
    internal sealed class RouteData
    {
        public const int Max = RouteDraft.MaxPoints;
        public int Count, Selected = -1, Current = -1;
        public bool ReadOnly;
        public readonly string[] Alt = new string[Max], Spd = new string[Max], At = new string[Max];

        public void Reset()
        {
            Count = 0;
            Selected = Current = -1;
            ReadOnly = false;
        }
    }

    /// <summary>The Flight Plan's leg table (mockup .leg): # · ALT · SPD · AT POINT for every point, the one being flown marked, flown
    /// legs dimmed, the selected point lit; a click selects a point (the steppers then edit it).</summary>
    internal sealed class WmcLegTable : AvPart
    {
        private const float RowH = 21f, HeadH = 18f, Gap = 1f, Pad = 8f;
        private const float NumW = 22f, AltW = 46f, SpdW = 42f;

        private sealed class Row
        {
            public AvFrame Frame;
            public Image Rail;
            public TMP_Text No, Alt, Spd, At;
            public bool Hover;
            public int State;
        }

        private readonly Row[] rows = new Row[RouteData.Max];
        private readonly TMP_Text[] heads = new TMP_Text[4];
        private int shown;
        private Action<int> picked;

        public WmcLegTable(RectTransform parent, Action<int> pick)
        {
            Rect = AvLay.Child(parent, "LegTable");
            picked = pick;
            string[] words = { "#", "ALT", "SPD", "AT POINT" };
            for (int i = 0; i < heads.Length; i++)
            {
                heads[i] = WmcSheet.Text(Rect, "Head" + i, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, words[i]);
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
                r.Alt = T("Alt" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
                r.Spd = T("Spd" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
                r.At = T("At" + i, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
                rows[i] = r;
                SetShown(r, false);
            }
            Restyle();
        }

        private TMP_Text T(string name, AvTextRole role, TextAlignmentOptions align) => WmcSheet.Text(Rect, name, role, align);

        private static void SetShown(Row r, bool on)
        {
            if (r.Frame.gameObject.activeSelf == on) return;
            foreach (Component c in new Component[] { r.Frame, r.Rail, r.No, r.Alt, r.Spd, r.At })
                c.gameObject.SetActive(on);
        }

        public override float Measure(float width) => shown == 0 ? 0f : HeadH + Gap + shown * (RowH + Gap) - Gap;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float altX = Pad + NumW + 4f, spdX = altX + AltW + 4f, atX = spdX + SpdW + 4f;
            float[] xs = { Pad, altX, spdX, atX };
            float[] ws = { NumW, AltW, SpdW, Math.Max(40f, s.W - atX - Pad) };
            for (int i = 0; i < heads.Length; i++)
            {
                heads[i].gameObject.SetActive(shown > 0);
                AvLay.Place(heads[i].rectTransform, xs[i], 0f, ws[i], HeadH);
            }
            for (int i = 0; i < shown; i++)
            {
                Row r = rows[i];
                float y = HeadH + Gap + i * (RowH + Gap);
                AvLay.Place(r.Frame.rectTransform, 0f, y, s.W, RowH);
                AvLay.Place(r.Rail.rectTransform, 0f, y, 2f, RowH);
                AvLay.Place(r.No.rectTransform, xs[0], y, ws[0], RowH);
                AvLay.Place(r.Alt.rectTransform, xs[1], y, ws[1], RowH);
                AvLay.Place(r.Spd.rectTransform, xs[2], y, ws[2], RowH);
                AvLay.Place(r.At.rectTransform, xs[3], y, ws[3], RowH);
            }
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
                AvText.Set(r.Alt, d.Alt[i]);
                AvText.Set(r.Spd, d.Spd[i]);
                AvText.Set(r.At, d.At[i]);
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
            foreach (TMP_Text t in new[] { r.No, r.Alt, r.Spd }) t.color = text;
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
