using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Weather.Domain;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Weather.Presentation
{
    /// <summary>Colour tokens from the live kit sheet. Never literals.</summary>
    internal static class EnvInk
    {
        public static Color Role(string role) => AvStyleHost.FuiColor(role, Color.white);
        public static Color State(AvState s) => Role(AvStates.Class(s));
        public static Color Alpha(Color c, float a) { c.a = a; return c; }
        public static Color Track() => AvStyleHost.Resolve(AvStyleHost.FuiStyle("gauge-track").Background, AvTheme.Hairline);
    }

    /// <summary>Top-down drawing surface over a <see cref="VertexHelper"/>: (0,0) is the rect's top-left, y grows downward.</summary>
    internal readonly struct EnvCanvas
    {
        private readonly VertexHelper vh;
        private readonly Rect r;
        public EnvCanvas(VertexHelper vh, Rect r) { this.vh = vh; this.r = r; }

        private Vector3 P(float x, float y) => new Vector3(r.xMin + x, r.yMax - y, 0f);

        public void Quad(float x, float y, float w, float h, Color c) => QuadV(x, y, w, h, c, c);

        public void QuadV(float x, float y, float w, float h, Color top, Color bottom)
        {
            if (w <= 0f || h <= 0f) return;
            int i = vh.currentVertCount;
            vh.AddVert(P(x, y), top, Vector4.zero);
            vh.AddVert(P(x + w, y), top, Vector4.zero);
            vh.AddVert(P(x + w, y + h), bottom, Vector4.zero);
            vh.AddVert(P(x, y + h), bottom, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        public void Line(float x0, float y0, float x1, float y1, float width, Color c)
        {
            Vector2 a = new Vector2(x0, y0), b = new Vector2(x1, y1), d = b - a;
            if (d.sqrMagnitude < 0.0001f) return;
            d.Normalize();
            Vector2 n = new Vector2(-d.y, d.x) * (width * 0.5f);
            int i = vh.currentVertCount;
            vh.AddVert(P(a.x - n.x, a.y - n.y), c, Vector4.zero);
            vh.AddVert(P(a.x + n.x, a.y + n.y), c, Vector4.zero);
            vh.AddVert(P(b.x + n.x, b.y + n.y), c, Vector4.zero);
            vh.AddVert(P(b.x - n.x, b.y - n.y), c, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        public void Dashed(float x0, float y0, float x1, float y1, float width, float dash, float gap, Color c)
        {
            float len = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            if (len < 0.5f) return;
            float step = dash + gap;
            for (float t = 0f; t < len; t += step)
            {
                float e = Mathf.Min(t + dash, len);
                Line(Mathf.Lerp(x0, x1, t / len), Mathf.Lerp(y0, y1, t / len),
                    Mathf.Lerp(x0, x1, e / len), Mathf.Lerp(y0, y1, e / len), width, c);
            }
        }

        /// <summary>Elliptical arc, degrees counter-clockwise from +x (90 is up). Every <paramref name="skip"/>th step is left out for a dashed look.</summary>
        public void Arc(float cx, float cy, float rx, float ry, float fromDeg, float toDeg, float width, Color c, int skip = 0)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(toDeg - fromDeg) / 4f), 4, 96);
            float px = 0f, py = 0f;
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, i / (float)steps) * Mathf.Deg2Rad;
                float x = cx + Mathf.Cos(a) * rx, y = cy - Mathf.Sin(a) * ry;
                if (i > 0 && (skip <= 0 || i % (skip + 1) != 0)) Line(px, py, x, y, width, c);
                px = x; py = y;
            }
        }

        public void Ring(float cx, float cy, float rad, float width, Color c) => Arc(cx, cy, rad, rad, 0f, 360f, width, c);

        public void Disc(float cx, float cy, float rad, Color c, int segments = 24)
        {
            int center = vh.currentVertCount;
            vh.AddVert(P(cx, cy), c, Vector4.zero);
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                vh.AddVert(P(cx + Mathf.Cos(a) * rad, cy - Mathf.Sin(a) * rad), c, Vector4.zero);
                if (i > 0) vh.AddTriangle(center, center + i, center + i + 1);
            }
        }

        public void Tri(float x0, float y0, float x1, float y1, float x2, float y2, Color c)
        {
            int i = vh.currentVertCount;
            vh.AddVert(P(x0, y0), c, Vector4.zero);
            vh.AddVert(P(x1, y1), c, Vector4.zero);
            vh.AddVert(P(x2, y2), c, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        /// <summary>Track plus fill, with an optional tick (<paramref name="mark01"/> below 0 hides it).</summary>
        public void Bar(float x, float y, float w, float h, float fill01, Color track, Color fill, float mark01, Color markColor)
        {
            Quad(x, y, w, h, track);
            Quad(x, y, w * Mathf.Clamp01(fill01), h, fill);
            if (mark01 >= 0f) Quad(x + w * Mathf.Clamp01(mark01) - 1f, y - 3f, 2f, h + 6f, markColor);
        }
    }

    /// <summary>A mesh graphic that paints itself through a callback; parts own the state it reads.</summary>
    internal sealed class EnvGraphic : MaskableGraphic
    {
        public Action<EnvCanvas> Draw;

        public static EnvGraphic Create(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(EnvGraphic));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<EnvGraphic>();
            g.raycastTarget = false;
            return g;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Draw?.Invoke(new EnvCanvas(vh, GetPixelAdjustedRect()));
        }
    }

    /// <summary>
    /// Base of the ENV parts: a chamfered card frame, an optional mesh layer under the text, text
    /// factories that remember their ink role, and one Restyle. Height is always measured from the
    /// real content; parts call <see cref="AvPart.Changed"/> when a setter moves it.
    /// </summary>
    internal abstract class EnvPart : AvPart
    {
        protected const float Pad = 12f;
        private readonly AvFrame frame;
        private readonly string variant;
        private readonly bool brackets;
        private readonly List<KeyValuePair<TMP_Text, string>> tints = new List<KeyValuePair<TMP_Text, string>>(16);
        protected EnvGraphic Art;
        protected float PlacedW;

        protected EnvPart(RectTransform parent, string name, string cardVariant = "inert", bool hasBrackets = false)
        {
            Rect = AvLay.Child(parent, name);
            variant = cardVariant;
            brackets = hasBrackets;
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            frame.Bracket = hasBrackets ? 8f : 0f;
        }

        protected EnvGraphic MakeArt(Action<EnvCanvas> draw)
        {
            Art = EnvGraphic.Create(Rect, "Art");
            AvLay.Fill(Art.rectTransform);
            Art.Draw = draw;
            return Art;
        }

        protected TMP_Text Txt(string name, AvTextRole role, TextAlignmentOptions align, string ink = "ink",
            bool wrap = false, bool fit = false)
        {
            TMP_Text t = AvText.Make(Rect, name, role, "", align, wrap);
            if (fit) AvText.Fit(t, wrap);
            tints.Add(new KeyValuePair<TMP_Text, string>(t, ink));
            return t;
        }

        protected static void Box(TMP_Text t, float x, float y, float w, float h) => AvLay.Place(t.rectTransform, x, y, Mathf.Max(1f, w), h);

        protected static void SetText(TMP_Text t, string s)
        {
            s = s ?? "";
            if (t.text != s) t.text = s;
        }

        protected void Redraw() { if (Art != null) Art.SetVerticesDirty(); }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            PlacedW = s.W;
            Layout(s.W, s.H);
        }

        protected abstract void Layout(float w, float h);

        protected virtual void OnRestyle() { }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card " + variant);
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            frame.BracketColor = AvStyleHost.Resolve(AvStyleHost.FuiStyle("card-bracket").Background, AvTheme.Frame);
            frame.SetVerticesDirty();
            for (int i = 0; i < tints.Count; i++)
                if (tints[i].Value.Length > 0) tints[i].Key.color = EnvInk.Role(tints[i].Value);
            OnRestyle();
            Redraw();
        }
    }

    /// <summary>N equal cells (label / mono value / status note) inside a parent part; hairlines between them.</summary>
    internal sealed class EnvCells
    {
        private readonly TMP_Text[] label, value, note;
        private readonly Image[] rule;
        private readonly AvState[] state;
        private readonly bool notes;

        public EnvCells(RectTransform parent, string[] labels, bool withNotes,
            Func<string, AvTextRole, TextAlignmentOptions, string, bool, TMP_Text> make)
        {
            int n = labels.Length;
            notes = withNotes;
            label = new TMP_Text[n]; value = new TMP_Text[n]; note = new TMP_Text[n];
            rule = new Image[Mathf.Max(0, n - 1)];
            state = new AvState[n];
            for (int i = 0; i < n; i++)
            {
                label[i] = make("Key" + i, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "key", true);
                label[i].text = labels[i];
                value[i] = make("Val" + i, AvTextRole.DataStrong, TextAlignmentOptions.MidlineLeft, "", true);
                note[i] = make("Note" + i, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "", true);
                state[i] = AvState.Info;
            }
            for (int i = 0; i < rule.Length; i++) rule[i] = AvLay.Solid(parent, "Rule" + i, Color.clear);
        }

        public float Height => notes ? 58f : 44f;

        public void Set(int i, string v, string n, AvState s)
        {
            if (value[i].text != (v ?? "")) value[i].text = v ?? "";
            string full = n == null ? "" : AvStates.Glyph(s) + n;
            if (note[i].text != full) note[i].text = full;
            state[i] = s;
            Tint(i);
        }

        private void Tint(int i)
        {
            bool alarm = state[i] == AvState.Caution || state[i] == AvState.Danger;
            value[i].color = alarm ? EnvInk.State(state[i]) : EnvInk.Role("ink");
            note[i].color = alarm ? EnvInk.State(state[i]) : EnvInk.Role("ink-dim");
        }

        public void Restyle()
        {
            for (int i = 0; i < label.Length; i++) Tint(i);
            for (int i = 0; i < rule.Length; i++) rule[i].color = EnvInk.Alpha(EnvInk.Role("hairline"), 0.9f);
        }

        public void Place(float x, float y, float w)
        {
            int n = label.Length;
            float cw = w / n;
            for (int i = 0; i < n; i++)
            {
                float cx = x + i * cw + 8f, iw = cw - 12f;
                AvLay.Place(label[i].rectTransform, cx, y + 5f, iw, 15f);
                AvLay.Place(value[i].rectTransform, cx, y + 20f, iw, 18f);
                if (notes) AvLay.Place(note[i].rectTransform, cx, y + 39f, iw, 15f);
                note[i].gameObject.SetActive(notes);
            }
            for (int i = 0; i < rule.Length; i++)
                AvLay.Place(rule[i].rectTransform, x + (i + 1) * cw - 1f, y + 6f, 1f, Height - 12f);
        }
    }

    /// <summary>A framed strip of mono readouts, each with an optional status word.</summary>
    internal sealed class EnvStrip : EnvPart
    {
        private readonly EnvCells cells;

        public EnvStrip(RectTransform parent, string name, bool withNotes, params string[] labels)
            : base(parent, name)
        {
            cells = new EnvCells(Rect, labels, withNotes, (n, r, a, ink, fit) => Txt(n, r, a, ink, false, fit));
            Restyle();
        }

        public void Set(int i, string value, string note = null, AvState state = AvState.Info) => cells.Set(i, value, note, state);

        public override float Measure(float width) => cells.Height;
        protected override void Layout(float w, float h) => cells.Place(0f, 0f, w);
        protected override void OnRestyle() => cells?.Restyle();
    }

    /// <summary>One labelled bar: name and word/number on a line, a track beneath, optional reference tick.</summary>
    internal sealed class EnvMeter : EnvPart
    {
        private readonly TMP_Text name, value, tickLabel;
        private float fill, mark = -1f;
        private AvState state = AvState.Ready;

        public EnvMeter(RectTransform parent, string title) : base(parent, "Meter " + title)
        {
            MakeArt(Paint);
            name = Txt("Name", AvTextRole.Label, TextAlignmentOptions.MidlineLeft, "ink-dim");
            name.text = title;
            value = Txt("Value", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight, "ink", false, true);
            tickLabel = Txt("Tick", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "ink-dim");
            Restyle();
        }

        private bool HasTick => mark >= 0f;

        public void Set(string text, float fill01, AvState s, float mark01 = -1f, string markText = null)
        {
            bool tickChanged = HasTick != (mark01 >= 0f);
            SetText(value, (s == AvState.Caution || s == AvState.Danger ? AvStates.Glyph(s) : "") + text);
            fill = Mathf.Clamp01(fill01);
            mark = mark01;
            SetText(tickLabel, markText);
            state = s;
            value.color = s == AvState.Caution || s == AvState.Danger ? EnvInk.State(s) : EnvInk.Role("ink");
            Redraw();
            if (tickChanged) Changed();
            if (PlacedW > 0f) Layout(PlacedW, Measure(PlacedW));
        }

        public override float Measure(float width) => HasTick ? 62f : 46f;

        protected override void Layout(float w, float h)
        {
            Box(name, Pad, 6f, w * 0.5f - Pad, 18f);
            Box(value, w * 0.5f, 6f, w * 0.5f - Pad, 18f);
            Box(tickLabel, Pad, 42f, w - 2f * Pad, 15f);
            tickLabel.gameObject.SetActive(HasTick);
        }

        private void Paint(EnvCanvas c)
        {
            float w = PlacedW - 2f * Pad;
            if (w <= 0f) return;
            Color fillColor = EnvInk.State(state == AvState.Info ? AvState.Ready : state);
            c.Bar(Pad, 28f, w, 8f, fill, EnvInk.Track(), fillColor, mark, EnvInk.Role("ink"));
        }

        protected override void OnRestyle()
        {
            if (value != null) value.color = state == AvState.Caution || state == AvState.Danger ? EnvInk.State(state) : EnvInk.Role("ink");
        }
    }

    /// <summary>Everything the hero condition card shows.</summary>
    internal sealed class EnvSky
    {
        public string Code = "", Word = "", Briefing = "", Category = "";
        public WeatherRegimeType Regime;
        public float Cover;
        public AvState State = AvState.Info, CategoryState = AvState.Info;
        public string Base = "", Top = "", Wind = "", Visibility = "", Precip = "";
        public AvState PrecipState = AvState.Info;
    }

    /// <summary>
    /// METAR-style hero: the sky-cover code at display size with its pictogram and word, a cover bar,
    /// the flight category, the tactical line and a mono strip (base, top, wind, visibility, precipitation).
    /// </summary>
    internal sealed class EnvConditionCard : EnvPart
    {
        private const float TopRow = 62f, StripH = 44f;
        private readonly TMP_Text code, word, cover, catKey, catValue, brief;
        private readonly WeatherGlyph glyph;
        private readonly Image rail, divider;
        private readonly EnvCells cells;
        private float coverFrac;
        private AvState state = AvState.Info, catState = AvState.Info;

        public EnvConditionCard(RectTransform parent) : base(parent, "Condition card", "raised", true)
        {
            MakeArt(Paint);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            divider = AvLay.Solid(Rect, "Divider", Color.clear);
            code = Txt("Code", AvTextRole.Display, TextAlignmentOptions.MidlineLeft, "ink");
            glyph = WeatherGlyph.Create(Rect, new Rect(0f, 0f, 44f, 44f));
            word = Txt("Word", AvTextRole.Title, TextAlignmentOptions.MidlineLeft, "ink", false, true);
            cover = Txt("Cover", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "ink-dim");
            catKey = Txt("CatKey", AvTextRole.Micro, TextAlignmentOptions.MidlineRight, "key");
            catKey.text = "FLT CAT";
            catValue = Txt("CatValue", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight, "ink", false, true);
            brief = Txt("Brief", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, "ink-dim", true);
            cells = new EnvCells(Rect, new[] { "BASE", "TOP", "WIND", "VIS", "PRECIP" }, false,
                (n, r, a, ink, fit) => Txt(n, r, a, ink, false, fit));
            Restyle();
        }

        public void Set(EnvSky d)
        {
            SetText(code, d.Code);
            SetText(word, (d.State == AvState.Caution || d.State == AvState.Danger ? AvStates.Glyph(d.State) : "") + d.Word);
            SetText(cover, AvNum.Percent(d.Cover) + " COVER");
            SetText(catValue, (d.CategoryState == AvState.Caution || d.CategoryState == AvState.Danger ? AvStates.Glyph(d.CategoryState) : "") + d.Category);
            glyph.SetKind(d.Regime);
            coverFrac = Mathf.Clamp01(d.Cover);
            bool briefChanged = brief.text != (d.Briefing ?? "");
            SetText(brief, d.Briefing);
            state = d.State; catState = d.CategoryState;
            cells.Set(0, d.Base, null, AvState.Info);
            cells.Set(1, d.Top, null, AvState.Info);
            cells.Set(2, d.Wind, null, AvState.Info);
            cells.Set(3, d.Visibility, null, AvState.Info);
            cells.Set(4, d.Precip, null, d.PrecipState);
            Tint();
            Redraw();
            if (briefChanged) Changed();
            if (PlacedW > 0f) Layout(PlacedW, Measure(PlacedW));
        }

        private float BriefH(float width) => brief.text.Length == 0 ? 0f : AvText.Height(brief, width - 2f * Pad);

        public override float Measure(float width)
        {
            float b = BriefH(width);
            return 76f + (b > 0f ? b + 6f : 0f) + StripH + 2f;
        }

        protected override void Layout(float w, float h)
        {
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, h);
            AvLay.Place(glyph.rectTransform, Pad + 4f, 12f, 44f, 44f);
            Box(code, Pad + 58f, 12f, 64f, 44f);
            float wx = Pad + 128f, catW = 84f, ww = w - wx - catW - Pad;
            Box(word, wx, 12f, ww, 26f);
            Box(cover, wx, 40f, ww, 16f);
            Box(catKey, w - Pad - catW, 14f, catW, 15f);
            Box(catValue, w - Pad - catW, 30f, catW, 20f);
            AvLay.Place(divider.rectTransform, Pad, 71f, w - 2f * Pad, 1f);
            float y = 76f;
            float b = BriefH(w);
            if (b > 0f) { Box(brief, Pad, y, w - 2f * Pad, b); y += b + 6f; }
            brief.gameObject.SetActive(b > 0f);
            cells.Place(Pad, y, w - 2f * Pad);
        }

        private void Paint(EnvCanvas c)
        {
            float wx = Pad + 128f, catW = 84f, bw = PlacedW - wx - catW - Pad;
            if (bw <= 0f) return;
            c.Bar(wx, 58f, bw, 4f, coverFrac, EnvInk.Track(), EnvInk.State(state == AvState.Info ? AvState.Ready : state), -1f, Color.clear);
        }

        private void Tint()
        {
            bool alarm = state == AvState.Caution || state == AvState.Danger;
            Color s = EnvInk.State(state);
            rail.color = s;
            glyph.color = alarm ? s : EnvInk.Role("ink");
            word.color = alarm ? s : EnvInk.Role("ink");
            code.color = alarm ? s : EnvInk.Role("ink");
            bool catAlarm = catState == AvState.Caution || catState == AvState.Danger;
            catValue.color = catAlarm ? EnvInk.State(catState) : EnvInk.Role("ink");
            divider.color = EnvInk.Alpha(EnvInk.Role("hairline"), 0.9f);
        }

        protected override void OnRestyle()
        {
            if (cells == null) return;
            cells.Restyle();
            Tint();
        }
    }

    /// <summary>Everything the vertical profile draws.</summary>
    internal sealed class EnvProfileData
    {
        public float Base, Top, CameraAlt;
        public bool HasCamera;
        public AvState State = AvState.Info;
        public string Status = "";
    }

    /// <summary>
    /// Altitude profile: the cloud layer as a slab between base and top on a metre scale, the camera
    /// height as a dashed level with an aircraft mark, and the three readings labelled at the right.
    /// </summary>
    internal sealed class EnvProfile : EnvPart
    {
        private const float PlotX = 50f, LabelW = 122f, PlotTop = 14f, PlotBottom = 150f, Height = 164f;
        private const int MaxTicks = 7;
        private readonly TMP_Text[] ticks = new TMP_Text[MaxTicks];
        private readonly TMP_Text topLabel, baseLabel, camLabel, empty;
        private EnvProfileData d = new EnvProfileData();
        private float minAlt, maxAlt;
        private int tickCount;
        private readonly float[] tickAlt = new float[MaxTicks];

        public EnvProfile(RectTransform parent) : base(parent, "Vertical profile")
        {
            MakeArt(Paint);
            for (int i = 0; i < MaxTicks; i++)
                ticks[i] = Txt("Tick" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight, "ink-muted");
            topLabel = Txt("Top", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "ink", false, true);
            baseLabel = Txt("Base", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "ink", false, true);
            camLabel = Txt("Cam", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "ink", false, true);
            empty = Txt("NoCam", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "ink-dim");
            Restyle();
        }

        public void Set(EnvProfileData data)
        {
            d = data;
            minAlt = data.HasCamera ? Mathf.Min(0f, data.CameraAlt - 150f) : 0f;
            float need = Mathf.Max(data.Top * 1.08f, data.HasCamera ? data.CameraAlt + 400f : 0f, 1500f);
            float step = need > 9000f ? 2500f : need > 4500f ? 1000f : 500f;
            maxAlt = Mathf.Ceil(need / step) * step;
            float tickStep = (maxAlt - minAlt) / 4f > 1250f ? 2500f : (maxAlt - minAlt) / 4f > 600f ? 1000f : 500f;
            tickCount = 0;
            for (float a = Mathf.Ceil(minAlt / tickStep) * tickStep; a <= maxAlt + 0.1f && tickCount < MaxTicks; a += tickStep)
                tickAlt[tickCount++] = a;

            SetText(topLabel, "TOP " + AvNum.Fixed(data.Top, 0) + " M");
            SetText(baseLabel, "BASE " + AvNum.Fixed(data.Base, 0) + " M");
            SetText(camLabel, data.HasCamera ? "CAM " + AvNum.Signed(data.CameraAlt, 0) + " M" : "");
            SetText(empty, data.HasCamera ? "" : "CAMERA UNAVAILABLE");
            for (int i = 0; i < MaxTicks; i++)
            {
                SetText(ticks[i], i < tickCount ? AvNum.Fixed(tickAlt[i], 0) : "");
                ticks[i].gameObject.SetActive(i < tickCount);
            }
            Tint();
            Redraw();
            if (PlacedW > 0f) Layout(PlacedW, Height);
        }

        public override float Measure(float width) => Height;

        private float Y(float alt) => Mathf.Lerp(PlotBottom, PlotTop, Mathf.InverseLerp(minAlt, maxAlt, alt));

        protected override void Layout(float w, float h)
        {
            for (int i = 0; i < tickCount; i++) Box(ticks[i], 2f, Y(tickAlt[i]) - 8f, PlotX - 8f, 16f);
            // Three labels at the right, nudged apart so they never overlap; each keeps its own level.
            float x = w - LabelW + 10f, lw = LabelW - Pad - 6f;
            float yTop = Y(d.Top), yBase = Y(d.Base), yCam = d.HasCamera ? Y(d.CameraAlt) : -100f;
            float[] want = { yTop, yBase, yCam };
            TMP_Text[] who = { topLabel, baseLabel, camLabel };
            int[] order = { 0, 1, 2 };
            Array.Sort(order, (a, b) => want[a].CompareTo(want[b]));
            float last = PlotTop - 8f - 17f;
            for (int k = 0; k < 3; k++)
            {
                int i = order[k];
                if (i == 2 && !d.HasCamera) continue;
                float y = Mathf.Max(want[i] - 8f, last + 17f);
                y = Mathf.Min(y, PlotBottom - 8f - (2 - k) * 17f);
                Box(who[i], x, y, lw, 16f);
                last = y;
            }
            camLabel.gameObject.SetActive(d.HasCamera);
            Box(empty, PlotX + 8f, PlotBottom - 22f, w - PlotX - LabelW - 16f, 15f);
            empty.gameObject.SetActive(!d.HasCamera);
        }

        private void Paint(EnvCanvas c)
        {
            float w = PlacedW;
            if (w <= 0f) return;
            float x0 = PlotX, x1 = w - LabelW;
            Color grid = EnvInk.Alpha(EnvInk.Role("hairline"), 0.8f);
            for (int i = 0; i < tickCount; i++) c.Line(x0, Y(tickAlt[i]), x1, Y(tickAlt[i]), 1f, grid);
            c.Line(x0, PlotTop - 4f, x0, PlotBottom + 4f, 1f, EnvInk.Role("frame"));

            float yTop = Y(d.Top), yBase = Y(d.Base);
            Color cloud = EnvInk.Role("ink-dim");
            c.QuadV(x0 + 1f, yTop, x1 - x0 - 1f, Mathf.Max(2f, yBase - yTop), EnvInk.Alpha(cloud, 0.10f), EnvInk.Alpha(cloud, 0.34f));
            for (float x = x0 + 8f; x < x1 - 2f; x += 12f)
                c.Line(x, yTop + 3f, x, Mathf.Max(yTop + 3f, yBase - 3f), 1f, EnvInk.Alpha(cloud, 0.10f));
            c.Line(x0, yTop, x1, yTop, 1.5f, EnvInk.Alpha(cloud, 0.85f));
            c.Line(x0, yBase, x1, yBase, 1.5f, EnvInk.Alpha(cloud, 0.85f));
            // Short leaders from the slab edges out to their labels.
            c.Line(x1, yTop, x1 + 6f, yTop, 1f, EnvInk.Alpha(cloud, 0.6f));
            c.Line(x1, yBase, x1 + 6f, yBase, 1f, EnvInk.Alpha(cloud, 0.6f));

            if (!d.HasCamera) return;
            Color s = EnvInk.State(d.State == AvState.Info ? AvState.Ready : d.State);
            float yc = Y(d.CameraAlt);
            c.Dashed(x0, yc, x1 - 16f, yc, 1.5f, 5f, 4f, s);
            float mx = x1 - 8f;
            c.Tri(mx - 8f, yc, mx + 6f, yc - 6f, mx + 6f, yc + 6f, s);
            c.Line(x1, yc, x1 + 6f, yc, 1f, EnvInk.Alpha(s, 0.8f));
        }

        private void Tint()
        {
            Color s = EnvInk.State(d.State == AvState.Info ? AvState.Ready : d.State);
            camLabel.color = s;
        }

        protected override void OnRestyle() => Tint();
    }

    /// <summary>Sun and moon readings for the sky card.</summary>
    internal sealed class EnvSun
    {
        public float Elevation, Azimuth, TimeOfDay, Sunrise, Sunset;
        public bool PolarDay, PolarNight;
        public string State = "", Light = "", Event = "";
        public AvState LightState = AvState.Info;
    }

    /// <summary>
    /// Daylight: a horizon-and-arc diagram with the sun marker (dimmed hollow ring below the horizon),
    /// sunrise and sunset times, the elevation at display size and a light word.
    /// </summary>
    internal sealed class EnvSunCard : EnvPart
    {
        private const float ArcW = 262f, DiagramH = 104f;
        private readonly TMP_Text elevKey, elev, azimuth, light, riseText, setText, evt;
        private EnvSun d = new EnvSun();

        public EnvSunCard(RectTransform parent) : base(parent, "Sun card")
        {
            MakeArt(Paint);
            elevKey = Txt("ElevKey", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "key");
            elevKey.text = "SUN ELEVATION";
            elev = Txt("Elev", AvTextRole.Display, TextAlignmentOptions.MidlineLeft, "ink", false, true);
            azimuth = Txt("Az", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "ink-dim", false, true);
            light = Txt("Light", AvTextRole.Label, TextAlignmentOptions.MidlineLeft, "ink", false, true);
            riseText = Txt("Rise", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "ink-dim");
            setText = Txt("Set", AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight, "ink-dim");
            evt = Txt("Event", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, "ink-dim", true);
            Restyle();
        }

        public static string Hhmm(float hour)
        {
            hour = ((hour % 24f) + 24f) % 24f;
            int h = (int)hour;
            int m = Mathf.Min(59, (int)((hour - h) * 60f));
            return AvNum.Fixed(h, 0).PadLeft(2, '0') + ":" + AvNum.Fixed(m, 0).PadLeft(2, '0');
        }

        public void Set(EnvSun data)
        {
            d = data;
            SetText(elev, AvNum.Signed(data.Elevation, 1) + "°");
            SetText(azimuth, "AZ " + AvNum.Fixed(data.Azimuth, 0).PadLeft(3, '0') + "°   T " + AvNum.Fixed(data.TimeOfDay, 1) + "H");
            SetText(light, AvStates.Glyph(data.LightState) + data.Light);
            bool polar = data.PolarDay || data.PolarNight;
            SetText(riseText, polar ? "" : "RISE " + Hhmm(data.Sunrise));
            SetText(setText, polar ? "" : "SET " + Hhmm(data.Sunset));
            bool evChanged = evt.text != (data.Event ?? "");
            SetText(evt, data.Event);
            light.color = data.LightState == AvState.Caution || data.LightState == AvState.Danger
                ? EnvInk.State(data.LightState) : EnvInk.Role("ink");
            Redraw();
            if (evChanged) Changed();
            if (PlacedW > 0f) Layout(PlacedW, Measure(PlacedW));
        }

        private float EventH(float width) => evt.text.Length == 0 ? 0f : AvText.Height(evt, width - 2f * Pad);

        public override float Measure(float width)
        {
            float e = EventH(width);
            return DiagramH + (e > 0f ? e + 8f : 0f) + 6f;
        }

        protected override void Layout(float w, float h)
        {
            float rx = ArcW + 16f, rw = w - rx - Pad;
            Box(elevKey, rx, 10f, rw, 15f);
            Box(elev, rx, 25f, rw, 32f);
            Box(azimuth, rx, 59f, rw, 16f);
            Box(light, rx, 76f, rw, 16f);
            Box(riseText, Pad, 84f, 118f, 16f);
            Box(setText, Pad + ArcW - 118f, 84f, 118f, 16f);
            float e = EventH(w);
            if (e > 0f) Box(evt, Pad, DiagramH + 2f, w - 2f * Pad, e);
            evt.gameObject.SetActive(e > 0f);
        }

        private void Paint(EnvCanvas c)
        {
            float cx = Pad + ArcW * 0.5f, hy = 62f, rx = ArcW * 0.5f - 6f, ry = 46f;
            Color dim = EnvInk.Alpha(EnvInk.Role("ink-muted"), 0.9f);
            Color frameC = EnvInk.Role("frame");
            c.Line(Pad, hy, Pad + ArcW, hy, 1.5f, frameC);
            c.Arc(cx, hy, rx, ry, 0f, 180f, 1.5f, dim, 1);
            c.Arc(cx, hy, rx, 16f, 180f, 360f, 1.2f, EnvInk.Alpha(dim, 0.5f), 1);
            c.Line(cx, hy - ry - 4f, cx, hy - ry + 2f, 1f, dim); // zenith tick
            float t = ((d.TimeOfDay % 24f) + 24f) % 24f;
            float day = d.Sunset - d.Sunrise;
            bool above = d.Elevation > 0f;
            float f;
            if (d.PolarDay || d.PolarNight || day <= 0.1f) f = t / 24f;
            else if (t >= d.Sunrise && t <= d.Sunset) f = (t - d.Sunrise) / day;
            else f = ((t < d.Sunrise ? t + 24f : t) - d.Sunset) / Mathf.Max(0.1f, 24f - day);
            f = Mathf.Clamp01(f);
            // Day: sunrise at the left end, sunset at the right. Night: sunset at the right, back to sunrise at the left.
            float a = above ? Mathf.PI * (1f - f) : Mathf.PI * f;
            float px = cx + Mathf.Cos(a) * rx;
            float py = above ? hy - Mathf.Sin(a) * ry : hy + Mathf.Sin(Mathf.PI * f) * 16f;
            Color sun = EnvInk.Role("ink");
            if (above)
            {
                c.Disc(px, py, 7f, sun);
                c.Ring(px, py, 11f, 1.2f, EnvInk.Alpha(sun, 0.5f));
            }
            else
            {
                c.Ring(px, py, 6f, 1.5f, dim);
                c.Disc(px, py, 2f, dim);
            }
        }
    }

    /// <summary>Moonlight readings.</summary>
    internal sealed class EnvMoon
    {
        public string Phase = "", Guidance = "";
        public float Lit, Glow;
        public bool Waxing = true, Moonless;
    }

    /// <summary>Moon: a phase disc drawn from the lit fraction, the phase name, a lit bar and the guidance line.</summary>
    internal sealed class EnvMoonCard : EnvPart
    {
        private const float DiscR = 27f, DiagramH = 78f;
        private readonly TMP_Text key, phase, lit, glow, guidance;
        private EnvMoon d = new EnvMoon();

        public EnvMoonCard(RectTransform parent) : base(parent, "Moon card")
        {
            MakeArt(Paint);
            key = Txt("Key", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "key");
            key.text = "PHASE";
            phase = Txt("Phase", AvTextRole.Title, TextAlignmentOptions.MidlineLeft, "ink", false, true);
            lit = Txt("Lit", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight, "ink", false, true);
            glow = Txt("Glow", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "ink-dim", false, true);
            guidance = Txt("Guidance", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, "ink-dim", true);
            Restyle();
        }

        public void Set(EnvMoon data)
        {
            d = data;
            SetText(phase, data.Phase);
            SetText(lit, AvNum.Percent(data.Lit) + " LIT");
            SetText(glow, "MOONLIGHT " + AvNum.Percent(data.Glow) + (data.Moonless ? "  ·  LOW NATURAL LIGHT" : ""));
            bool changed = guidance.text != (data.Guidance ?? "");
            SetText(guidance, data.Guidance);
            Redraw();
            if (changed) Changed();
            if (PlacedW > 0f) Layout(PlacedW, Measure(PlacedW));
        }

        private float GuideH(float width) => guidance.text.Length == 0 ? 0f : AvText.Height(guidance, width - 2f * Pad);

        public override float Measure(float width)
        {
            float g = GuideH(width);
            return DiagramH + (g > 0f ? g + 6f : 0f) + 6f;
        }

        protected override void Layout(float w, float h)
        {
            float x = Pad + 2f * DiscR + 18f, rw = w - x - Pad;
            Box(key, x, 8f, rw, 15f);
            Box(phase, x, 22f, rw, 26f);
            Box(lit, w - Pad - 92f, 48f, 92f, 18f);
            Box(glow, x, 48f, rw - 96f, 18f);
            float g = GuideH(w);
            if (g > 0f) Box(guidance, Pad, DiagramH + 2f, w - 2f * Pad, g);
            guidance.gameObject.SetActive(g > 0f);
        }

        private void Paint(EnvCanvas c)
        {
            float cx = Pad + DiscR + 2f, cy = 8f + DiscR + 2f;
            Color ink = EnvInk.Role("ink");
            Color dark = EnvInk.Alpha(EnvInk.Role("ink-muted"), 0.30f);
            c.Disc(cx, cy, DiscR, dark, 32);
            float t = 1f - 2f * Mathf.Clamp01(d.Lit);
            const int slices = 36;
            float sh = DiscR * 2f / slices;
            for (int i = 0; i < slices; i++)
            {
                float dy = -DiscR + (i + 0.5f) * sh;
                float half = Mathf.Sqrt(Mathf.Max(0f, DiscR * DiscR - dy * dy));
                float from = d.Waxing ? t * half : -half;
                float to = d.Waxing ? half : -t * half;
                if (to - from > 0.3f) c.Quad(cx + from, cy + dy - sh * 0.5f, to - from, sh + 0.4f, EnvInk.Alpha(ink, 0.92f));
            }
            c.Ring(cx, cy, DiscR, 1.2f, EnvInk.Alpha(ink, 0.55f));
            // Track under the lit bar.
            float bx = Pad + 2f * DiscR + 18f, bw = PlacedW - bx - Pad;
            if (bw > 0f) c.Bar(bx, 71f, bw, 3f, d.Lit, EnvInk.Track(), EnvInk.Role("ink-dim"), -1f, Color.clear);
        }
    }

    /// <summary>Wind readings.</summary>
    internal sealed class EnvWind
    {
        public float Kts, Turbulence;
        public int From, To;
        public string Cardinal = "", TurbWord = "", Advisory = "";
        public AvState State = AvState.Info, TurbState = AvState.Info;
    }

    /// <summary>
    /// Wind and turbulence: a compass dial whose arrow points where the air is going, the speed at
    /// display size with FROM / TO bearings, a turbulence bar with its word, and the advisory line.
    /// </summary>
    internal sealed class EnvWindCard : EnvPart
    {
        private const float DialR = 34f, DialTop = 16f, DiagramH = 112f;
        private readonly TMP_Text north, speedKey, speed, bearings, turbKey, turbValue, advisory;
        private EnvWind d = new EnvWind();

        public EnvWindCard(RectTransform parent) : base(parent, "Wind card")
        {
            MakeArt(Paint);
            north = Txt("North", AvTextRole.Micro, TextAlignmentOptions.Center, "key");
            north.text = "N";
            speedKey = Txt("SpeedKey", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "key");
            speedKey.text = "WIND";
            speed = Txt("Speed", AvTextRole.Display, TextAlignmentOptions.MidlineLeft, "ink", false, true);
            bearings = Txt("Bearings", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "ink-dim", false, true);
            turbKey = Txt("TurbKey", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "key");
            turbKey.text = "TURBULENCE";
            turbValue = Txt("TurbValue", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight, "ink", false, true);
            advisory = Txt("Advisory", AvTextRole.ProseSmall, TextAlignmentOptions.TopLeft, "ink-dim", true);
            Restyle();
        }

        public void Set(EnvWind data)
        {
            d = data;
            SetText(speed, AvNum.Fixed(data.Kts, 0) + " KT");
            SetText(bearings, "FROM " + AvNum.Fixed(data.From, 0).PadLeft(3, '0') + "° " + data.Cardinal + "   TO " + AvNum.Fixed(data.To, 0).PadLeft(3, '0') + "°");
            SetText(turbValue, AvStates.Glyph(data.TurbState) + data.TurbWord + "  " + AvNum.Fixed(data.Turbulence, 2));
            bool changed = advisory.text != AdvisoryText();
            SetText(advisory, AdvisoryText());
            Tint();
            Redraw();
            if (changed) Changed();
            if (PlacedW > 0f) Layout(PlacedW, Measure(PlacedW));
        }

        private string AdvisoryText() => d.Advisory == null || d.Advisory.Length == 0 ? "" : AvStates.Glyph(d.State) + d.Advisory;

        private float AdvH(float width) => advisory.text.Length == 0 ? 0f : AvText.Height(advisory, width - 2f * Pad);

        public override float Measure(float width)
        {
            float a = AdvH(width);
            return DiagramH + (a > 0f ? a + 6f : 0f) + 6f;
        }

        protected override void Layout(float w, float h)
        {
            float cx = Pad + DialR + 4f;
            Box(north, cx - 12f, 0f, 24f, 15f);
            float x = Pad + 2f * DialR + 24f, rw = w - x - Pad;
            Box(speedKey, x, 8f, rw, 15f);
            Box(speed, x, 22f, rw, 32f);
            Box(bearings, x, 56f, rw, 16f);
            Box(turbKey, x, 80f, rw * 0.45f, 15f);
            Box(turbValue, x + rw * 0.45f, 78f, rw * 0.55f, 18f);
            float a = AdvH(w);
            if (a > 0f) Box(advisory, Pad, DiagramH + 2f, w - 2f * Pad, a);
            advisory.gameObject.SetActive(a > 0f);
        }

        private void Paint(EnvCanvas c)
        {
            float cx = Pad + DialR + 4f, cy = DialTop + DialR;
            Color ink = EnvInk.Role("ink"), dim = EnvInk.Alpha(EnvInk.Role("ink-muted"), 0.9f);
            c.Ring(cx, cy, DialR, 1.5f, EnvInk.Role("frame"));
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f, r0 = DialR - (i % 4 == 0 ? 7f : 3f);
                c.Line(cx + Mathf.Sin(a) * r0, cy - Mathf.Cos(a) * r0, cx + Mathf.Sin(a) * DialR, cy - Mathf.Cos(a) * DialR, 1f, dim);
            }
            if (d.Kts >= 0.5f)
            {
                float to = d.To * Mathf.Deg2Rad;
                float dx = Mathf.Sin(to), dy = -Mathf.Cos(to);
                Color s = d.State == AvState.Caution || d.State == AvState.Danger ? EnvInk.State(d.State) : ink;
                float tail = DialR * 0.62f, head = DialR * 0.72f;
                c.Line(cx - dx * tail, cy - dy * tail, cx + dx * (head - 8f), cy + dy * (head - 8f), 2.4f, s);
                float nx = -dy, ny = dx;
                c.Tri(cx + dx * head, cy + dy * head,
                    cx + dx * (head - 12f) + nx * 6f, cy + dy * (head - 12f) + ny * 6f,
                    cx + dx * (head - 12f) - nx * 6f, cy + dy * (head - 12f) - ny * 6f, s);
                // Fletching at the upwind end marks where the air comes from.
                c.Disc(cx - dx * tail, cy - dy * tail, 2.5f, s);
            }
            else c.Ring(cx, cy, 3f, 1.5f, dim);
            float x = Pad + 2f * DialR + 24f, rw = PlacedW - x - Pad;
            if (rw > 0f)
                c.Bar(x, 98f, rw, 6f, Mathf.Clamp01(d.Turbulence / 0.8f), EnvInk.Track(),
                    EnvInk.State(d.TurbState == AvState.Info ? AvState.Ready : d.TurbState), -1f, Color.clear);
        }

        private void Tint()
        {
            bool alarm = d.State == AvState.Caution || d.State == AvState.Danger;
            advisory.color = alarm ? EnvInk.State(d.State) : EnvInk.Role("ink-dim");
            bool ta = d.TurbState == AvState.Caution || d.TurbState == AvState.Danger;
            turbValue.color = ta ? EnvInk.State(d.TurbState) : EnvInk.Role("ink");
            speed.color = alarm ? EnvInk.State(d.State) : EnvInk.Role("ink");
        }

        protected override void OnRestyle() => Tint();
    }

    /// <summary>Column geometry shared by the outlook header and its rows so every column lines up.</summary>
    internal static class EnvOutlookCols
    {
        public const float Time = 10f, TimeW = 34f;
        public const float Sky = 48f, SkyW = 70f;
        public const float CoverX = 120f, CoverW = 46f;
        public const float DeckX = 170f, DeckW = 62f;
        public const float RainX = 246f;
        public const float RightPad = 12f;
    }

    /// <summary>Column captions above the 60-minute outlook.</summary>
    internal sealed class EnvOutlookHeader : AvPart
    {
        private readonly TMP_Text time, sky, cover, deck, rain;

        public EnvOutlookHeader(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Outlook header");
            time = Head("Time", "TIME", TextAlignmentOptions.MidlineLeft);
            sky = Head("Sky", "SKY", TextAlignmentOptions.MidlineLeft);
            cover = Head("Cover", "COVER", TextAlignmentOptions.MidlineRight);
            deck = Head("Base", "BASE", TextAlignmentOptions.MidlineRight);
            rain = Head("Rain", "RAIN", TextAlignmentOptions.MidlineLeft);
            Restyle();
        }

        private TMP_Text Head(string name, string text, TextAlignmentOptions a) => AvText.Make(Rect, name, AvTextRole.Micro, text, a);

        public override float Measure(float width) => 18f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(time.rectTransform, EnvOutlookCols.Time, 0f, EnvOutlookCols.TimeW + 8f, s.H);
            AvLay.Place(sky.rectTransform, EnvOutlookCols.Sky, 0f, EnvOutlookCols.SkyW, s.H);
            AvLay.Place(cover.rectTransform, EnvOutlookCols.CoverX - 8f, 0f, EnvOutlookCols.CoverW + 8f, s.H);
            AvLay.Place(deck.rectTransform, EnvOutlookCols.DeckX, 0f, EnvOutlookCols.DeckW, s.H);
            AvLay.Place(rain.rectTransform, EnvOutlookCols.RainX, 0f, s.W - EnvOutlookCols.RainX, s.H);
        }

        public override void Restyle()
        {
            Color c = EnvInk.Role("key");
            time.color = sky.color = cover.color = deck.color = rain.color = c;
        }
    }

    /// <summary>
    /// One 60-minute outlook row: state rail, time, the regime pictogram, code badge, cover, cloud
    /// base and a rain bar. Columns come from <see cref="EnvOutlookCols"/>.
    /// </summary>
    internal sealed class ForecastRowPart : AvPart
    {
        private readonly Image rail, back, sep, rainTrack, rainFill;
        private readonly TMP_Text time, badge, cover, deckLabel, rainText;
        private readonly WeatherGlyph glyph;
        private AvState state = AvState.Info;
        private float rainFrac;
        private bool now;
        private AvSlot lastSlot;

        public ForecastRowPart(RectTransform parent, string name)
        {
            Rect = AvLay.Child(parent, name);
            back = AvLay.Solid(Rect, "Back", Color.clear);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            sep = AvLay.Solid(Rect, "Sep", Color.clear);
            time = AvText.Make(Rect, "Time", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineLeft);
            glyph = WeatherGlyph.Create(Rect, new Rect(EnvOutlookCols.Sky, 3f, 24f, 24f));
            badge = AvText.Make(Rect, "Badge", AvTextRole.Micro, "", TextAlignmentOptions.Center);
            cover = AvText.Make(Rect, "Cover", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            deckLabel = AvText.Make(Rect, "Deck", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            rainTrack = AvLay.Solid(Rect, "RainTrack", Color.clear);
            rainFill = AvLay.Solid(Rect, "RainFill", Color.clear);
            rainText = AvText.Make(Rect, "RainText", AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
            Restyle();
        }

        public void Set(string timeText, bool isNow, WeatherRegimeType regime, string code,
            float coverFrac, float deckMetres, float rainProbability, AvState rowState)
        {
            time.text = timeText;
            time.fontStyle = isNow ? FontStyles.Bold : FontStyles.Normal;
            now = isNow;
            glyph.SetKind(regime);
            badge.text = code ?? "";
            cover.text = AvNum.Percent(coverFrac);
            deckLabel.text = AvNum.Fixed(deckMetres, 0) + " M";
            rainFrac = Mathf.Clamp01(rainProbability);
            rainText.text = rainProbability <= 0.05f ? "DRY" : "RAIN " + AvNum.Percent(rainProbability);
            state = rowState;
            Restyle();
            if (lastSlot.W > 0f) Place(lastSlot);
        }

        public override float Measure(float width) => 30f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            lastSlot = s;
            AvLay.Place(back.rectTransform, 0f, 0f, s.W, s.H);
            AvLay.Place(rail.rectTransform, 0f, 0f, 2f, s.H);
            AvLay.Place(sep.rectTransform, 0f, s.H - 1f, s.W, 1f);
            AvLay.Place(time.rectTransform, EnvOutlookCols.Time, 0f, EnvOutlookCols.TimeW, s.H);
            AvLay.Place(glyph.rectTransform, EnvOutlookCols.Sky, 3f, 24f, 24f);
            AvLay.Place(badge.rectTransform, EnvOutlookCols.Sky + 28f, (s.H - 15f) * 0.5f, 42f, 15f);
            AvLay.Place(cover.rectTransform, EnvOutlookCols.CoverX, 0f, EnvOutlookCols.CoverW, s.H);
            AvLay.Place(deckLabel.rectTransform, EnvOutlookCols.DeckX, 0f, EnvOutlookCols.DeckW, s.H);
            float barX = EnvOutlookCols.RainX, textW = 62f;
            float barW = Mathf.Max(24f, s.W - barX - textW - EnvOutlookCols.RightPad);
            AvLay.Place(rainTrack.rectTransform, barX, (s.H - 6f) * 0.5f, barW, 6f);
            AvLay.Place(rainFill.rectTransform, barX, (s.H - 6f) * 0.5f, barW * rainFrac, 6f);
            AvLay.Place(rainText.rectTransform, barX + barW + 8f, 0f, textW - 8f + EnvOutlookCols.RightPad, s.H);
        }

        public override void Restyle()
        {
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(state));
            rail.color = AvStyleHost.Resolve(r.Rail, AvTheme.RailInfo);
            back.color = now ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("row").Background, AvTheme.SurfaceInert) : Color.clear;
            sep.color = EnvInk.Alpha(EnvInk.Role("hairline"), 0.7f);
            time.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            Color badgeColor = AvStyleHost.Resolve(AvStyleHost.FuiStyle("chip " + AvStates.Class(state)).Color, AvTheme.Dim);
            badge.color = badgeColor;
            glyph.color = badgeColor;
            cover.color = deckLabel.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-value").Color, AvTheme.TextPrimary);
            rainTrack.color = EnvInk.Track();
            rainFill.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("metric-fill " + AvStates.Class(state)).Background, AvTheme.Accent);
            rainText.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
        }
    }
}
