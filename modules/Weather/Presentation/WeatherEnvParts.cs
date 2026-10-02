using NOAvionics;
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Weather.Presentation
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
        protected float PlacedW, PlacedH;

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

        /// <summary>Hover help on the whole part (shown in the console footer).</summary>
        public void SetHelp(string text)
        {
            if (Art == null) return;
            Art.raycastTarget = !string.IsNullOrEmpty(text);
            AvHelpTip.Attach(Art.gameObject, text);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            PlacedW = s.W; PlacedH = s.H;
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

    /// <summary>Everything the hero condition card shows.</summary>
    internal sealed class EnvSky
    {
        public string Code = "", Word = "", Category = "";
        public WeatherRegimeType Regime;
        public float Cover;
        public AvState State = AvState.Info, CategoryState = AvState.Info;
    }

    /// <summary>
    /// METAR-style hero: the sky-cover code at display size with its pictogram and word, a cover bar
    /// and the flight category. Base, wind, visibility and rain live in the header metrics and the rings.
    /// </summary>
    internal sealed class EnvConditionCard : EnvPart
    {
        private readonly TMP_Text code, word, cover, catKey, catValue;
        private readonly WeatherGlyph glyph;
        private readonly Image rail, divider;
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
            state = d.State; catState = d.CategoryState;
            Tint();
            Redraw();
        }

        public override float Measure(float width) => 68f;

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
            AvLay.Place(divider.rectTransform, Pad, 66f, w - 2f * Pad, 1f);
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

        protected override void OnRestyle() => Tint();
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
        private const float PlotX = 50f, LabelW = 122f, PlotTop = 14f, Height = 132f, BottomMargin = 14f;
        private const int MaxTicks = 7;
        private readonly TMP_Text[] ticks = new TMP_Text[MaxTicks];
        private readonly TMP_Text topLabel, baseLabel, camLabel, empty, status;
        private float plotBottom = Height - BottomMargin;
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
            status = Txt("Status", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "ink-dim", false, true);
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
            bool alarm = data.State == AvState.Caution || data.State == AvState.Danger;
            SetText(status, data.HasCamera ? (alarm ? AvStates.Glyph(data.State) : "") + data.Status : "");
            for (int i = 0; i < MaxTicks; i++)
            {
                SetText(ticks[i], i < tickCount ? AvNum.Fixed(tickAlt[i], 0) : "");
                ticks[i].gameObject.SetActive(i < tickCount);
            }
            Tint();
            Redraw();
            if (PlacedW > 0f) Layout(PlacedW, PlacedH > 0f ? PlacedH : Height);
        }

        public override float Measure(float width) => Height;

        private float Y(float alt) => Mathf.Lerp(plotBottom, PlotTop, Mathf.InverseLerp(minAlt, maxAlt, alt));

        protected override void Layout(float w, float h)
        {
            plotBottom = Mathf.Max(PlotTop + 60f, h - BottomMargin);   // a growing profile stretches its metre scale
            Box(status, PlotX + 6f, 0f, w - PlotX - LabelW - 12f, 14f);
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
                y = Mathf.Min(y, plotBottom - 8f - (2 - k) * 17f);
                Box(who[i], x, y, lw, 16f);
                last = y;
            }
            camLabel.gameObject.SetActive(d.HasCamera);
            Box(empty, PlotX + 8f, plotBottom - 22f, w - PlotX - LabelW - 16f, 15f);
            empty.gameObject.SetActive(!d.HasCamera);
        }

        private void Paint(EnvCanvas c)
        {
            float w = PlacedW;
            if (w <= 0f) return;
            float x0 = PlotX, x1 = w - LabelW;
            Color grid = EnvInk.Alpha(EnvInk.Role("hairline"), 0.8f);
            for (int i = 0; i < tickCount; i++) c.Line(x0, Y(tickAlt[i]), x1, Y(tickAlt[i]), 1f, grid);
            c.Line(x0, PlotTop - 4f, x0, plotBottom + 4f, 1f, EnvInk.Role("frame"));

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
            status.color = d.State == AvState.Caution || d.State == AvState.Danger ? s : EnvInk.Role("ink-dim");
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
            Color dim = EnvInk.Alpha(EnvInk.Role("ink-muted"), 0.9f);
            float t = ((d.TimeOfDay % 24f) + 24f) % 24f;
            // One rectangular 24-hour strip: day/night blocks and the current time cursor.
            float cell = ArcW / 24f;
            for (int hour = 0; hour < 24; hour++)
            {
                bool daylight = d.PolarDay || (!d.PolarNight && hour + .5f >= d.Sunrise && hour + .5f < d.Sunset);
                c.Quad(Pad + hour * cell, 26f, cell - 2f, 32f,
                    daylight ? EnvInk.Alpha(EnvInk.Role("key"), .65f) : EnvInk.Track());
                c.Line(Pad + hour * cell, 62f, Pad + hour * cell, hour % 6 == 0 ? 72f : 67f, 1f, dim);
            }
            float cursor = Pad + t / 24f * ArcW;
            c.Line(cursor, 20f, cursor, 75f, 2f, EnvInk.Role("ink"));
            c.Quad(cursor - 3f, 17f, 6f, 4f, EnvInk.Role("ink"));
        }
    }

    /// <summary>Moonlight readings.</summary>
    internal sealed class EnvMoon
    {
        public string Phase = "", Guidance = "";
        public float Lit, Glow;
        public bool Waxing = true, Moonless;
    }

    /// <summary>Moon phase and light levels with a labeled illumination meter.</summary>
    internal sealed class EnvMoonCard : EnvPart
    {
        private const float DiagramH = 78f;
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
            float x = Pad, rw = w - 2f * Pad;
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
            float bw = PlacedW - 2f * Pad;
            if (bw > 0f) c.Bar(Pad, 71f, bw, 3f, d.Lit, EnvInk.Track(), EnvInk.Role("ink-dim"), -1f, Color.clear);
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
            north.text = "N  E  S  W  N";
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

        private string AdvisoryText() => "";

        private float AdvH(float width) => advisory.text.Length == 0 ? 0f : AvText.Height(advisory, width - 2f * Pad);

        public override float Measure(float width)
        {
            float a = AdvH(width);
            return DiagramH + (a > 0f ? a + 6f : 0f) + 6f;
        }

        protected override void Layout(float w, float h)
        {
            Box(north, Pad, 8f, 2f * DialR + 8f, 15f);
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
            Color ink = EnvInk.Role("ink"), dim = EnvInk.Alpha(EnvInk.Role("ink-muted"), 0.9f);
            float tapeW = 2f * DialR + 8f;
            c.Quad(Pad, 30f, tapeW, 34f, EnvInk.Track());
            for (int i = 0; i <= 16; i++)
            {
                float at = Pad + i * tapeW / 16f;
                c.Line(at, 30f, at, i % 4 == 0 ? 41f : 36f, 1f, dim);
            }
            if (d.Kts >= 0.5f)
            {
                Color s = d.State == AvState.Caution || d.State == AvState.Danger ? EnvInk.State(d.State) : ink;
                float at = Pad + Mathf.Repeat(d.From, 360f) / 360f * tapeW;
                c.Line(at, 27f, at, 70f, 2f, s);
                c.Quad(at - 3f, 68f, 6f, 4f, s);
            }
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

    /// <summary>Icon-first sky state: SUN / CLOUD / RAIN / STORM / MIST cells, the ones that apply lit in the state colour.</summary>
    internal sealed class EnvStateRow : EnvPart
    {
        public const int Sun = 1, Cloud = 2, Rain = 4, Storm = 8, Mist = 16;
        private static readonly string[] Names = { "SUN", "CLOUD", "RAIN", "STORM", "MIST" };
        private const float H = 58f;
        private readonly TMP_Text[] labels = new TMP_Text[5];
        private int mask;
        private AvState state = AvState.Info;

        public EnvStateRow(RectTransform parent) : base(parent, "State row")
        {
            MakeArt(Paint);
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = Txt("Label" + i, AvTextRole.Micro, TextAlignmentOptions.Center, "ink-dim", false, true);
                labels[i].text = Names[i];
            }
            Restyle();
        }

        /// <summary>Which cells a regime lights.</summary>
        public static int LitFor(WeatherRegimeType regime)
        {
            switch (regime)
            {
                case WeatherRegimeType.Clear: return Sun;
                case WeatherRegimeType.Fair:
                case WeatherRegimeType.Scattered: return Sun | Cloud;
                case WeatherRegimeType.RainSquall: return Cloud | Rain;
                case WeatherRegimeType.Storm: return Rain | Storm;
                default: return Cloud;
            }
        }

        public void Set(int litMask, AvState s)
        {
            if (litMask == mask && s == state) return;
            mask = litMask; state = s;
            Apply();
        }

        private void Apply()
        {
            bool alarm = state == AvState.Caution || state == AvState.Danger;
            for (int i = 0; i < labels.Length; i++)
            {
                bool on = (mask & (1 << i)) != 0;
                SetText(labels[i], (on && alarm && i == 3 ? AvStates.Glyph(state) : "") + Names[i]);
                labels[i].color = on ? (alarm ? EnvInk.State(state) : EnvInk.Role("ink")) : EnvInk.Role("ink-dim");
            }
            Redraw();
        }

        public override float Measure(float width) => H;

        protected override void Layout(float w, float h)
        {
            float cw = w / labels.Length;
            for (int i = 0; i < labels.Length; i++) Box(labels[i], i * cw + 2f, 38f, cw - 4f, 16f);
        }

        protected override void OnRestyle()
        {
            if (labels[labels.Length - 1] == null) return;
            Apply();
        }

        private void Paint(EnvCanvas c)
        {
            float w = PlacedW;
            if (w <= 0f) return;
            float cw = w / labels.Length;
            bool alarm = state == AvState.Caution || state == AvState.Danger;
            Color lit = EnvInk.State(state == AvState.Info ? AvState.Ready : state);
            Color ink = EnvInk.Role("ink");
            for (int i = 0; i < labels.Length; i++)
            {
                bool on = (mask & (1 << i)) != 0;
                float x = i * cw, cx = x + cw * 0.5f;
                Color col = on ? (alarm ? lit : ink) : EnvInk.Alpha(EnvInk.Role("ink-dim"), 0.6f);
                if (on)
                {
                    c.Quad(x + 3f, 3f, cw - 6f, H - 6f, EnvInk.Alpha(lit, 0.14f));
                    c.Quad(x + 3f, 3f, cw - 6f, 2f, lit);
                }
                switch (i)
                {
                    case 0: DrawSun(c, cx, 22f, col); break;
                    case 1: DrawCloud(c, cx, 22f, col); break;
                    case 2: DrawCloud(c, cx, 17f, col); DrawDrops(c, cx, 22f, col); break;
                    case 3: DrawCloud(c, cx, 17f, col); DrawBolt(c, cx, 22f, col); break;
                    default: DrawMist(c, cx, 22f, col); break;
                }
            }
        }

        private static void DrawSun(EnvCanvas c, float cx, float cy, Color col)
        {
            c.Ring(cx, cy, 6f, 1.6f, col);
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                c.Line(cx + Mathf.Cos(a) * 9f, cy + Mathf.Sin(a) * 9f, cx + Mathf.Cos(a) * 13f, cy + Mathf.Sin(a) * 13f, 1.6f, col);
            }
        }

        private static void DrawCloud(EnvCanvas c, float cx, float cy, Color col)
        {
            c.Disc(cx - 7f, cy + 1f, 5f, col, 16);
            c.Disc(cx, cy - 3f, 7f, col, 16);
            c.Disc(cx + 8f, cy + 1f, 5f, col, 16);
            c.Quad(cx - 7f, cy + 1f, 15f, 5f, col);
        }

        private static void DrawDrops(EnvCanvas c, float cx, float cy, Color col)
        {
            for (int k = -1; k <= 1; k++)
                c.Line(cx + k * 6f + 1.5f, cy + 3f, cx + k * 6f - 1.5f, cy + 10f, 1.6f, col);
        }

        private static void DrawBolt(EnvCanvas c, float cx, float cy, Color col)
        {
            c.Line(cx + 2f, cy + 2f, cx - 2f, cy + 7f, 1.8f, col);
            c.Line(cx - 2f, cy + 7f, cx + 3f, cy + 7f, 1.8f, col);
            c.Line(cx + 3f, cy + 7f, cx - 1f, cy + 13f, 1.8f, col);
        }

        private static void DrawMist(EnvCanvas c, float cx, float cy, Color col)
        {
            c.Line(cx - 11f, cy - 6f, cx + 9f, cy - 6f, 1.8f, col);
            c.Line(cx - 8f, cy, cx + 12f, cy, 1.8f, col);
            c.Line(cx - 11f, cy + 6f, cx + 9f, cy + 6f, 1.8f, col);
        }
    }

    /// <summary>The 60-minute outlook as one row of icon cells: time, regime pictogram, code and a rain tick.</summary>
    internal sealed class EnvOutlookStrip : EnvPart
    {
        private const float H = 76f;
        private readonly int count;
        private readonly TMP_Text[] time, code;
        private readonly WeatherGlyph[] glyph;
        private readonly float[] rain;
        private readonly AvState[] state;
        private int nowIndex;

        public EnvOutlookStrip(RectTransform parent, int cells) : base(parent, "Outlook strip")
        {
            count = cells;
            time = new TMP_Text[cells]; code = new TMP_Text[cells];
            glyph = new WeatherGlyph[cells]; rain = new float[cells]; state = new AvState[cells];
            MakeArt(Paint);
            for (int i = 0; i < cells; i++)
            {
                time[i] = Txt("Time" + i, AvTextRole.Micro, TextAlignmentOptions.Center, "ink-dim", false, true);
                code[i] = Txt("Code" + i, AvTextRole.Micro, TextAlignmentOptions.Center, "ink", false, true);
                glyph[i] = WeatherGlyph.Create(Rect, new Rect(0f, 0f, 26f, 26f));
                state[i] = AvState.Info;
            }
            Restyle();
        }

        public void Set(int i, string timeText, bool isNow, WeatherRegimeType regime, string codeText, float rainProbability, AvState s)
        {
            if (i < 0 || i >= count) return;
            SetText(time[i], timeText);
            bool alarm = s == AvState.Caution || s == AvState.Danger;
            SetText(code[i], (alarm ? AvStates.Glyph(s) : "") + (codeText ?? ""));
            glyph[i].SetKind(regime);
            rain[i] = Mathf.Clamp01(rainProbability);
            state[i] = s;
            if (isNow) nowIndex = i;
            Tint(i);
            Redraw();
        }

        private void Tint(int i)
        {
            bool alarm = state[i] == AvState.Caution || state[i] == AvState.Danger;
            Color c = alarm ? EnvInk.State(state[i]) : EnvInk.Role("ink");
            glyph[i].color = c; code[i].color = c;
            glyph[i].SetVerticesDirty();
        }

        public override float Measure(float width) => H;

        protected override void Layout(float w, float h)
        {
            float cw = (w - 8f) / count;
            for (int i = 0; i < count; i++)
            {
                float x = 4f + i * cw;
                Box(time[i], x, 5f, cw, 16f);
                AvLay.Place(glyph[i].rectTransform, x + (cw - 26f) * 0.5f, 23f, 26f, 26f);
                Box(code[i], x, 51f, cw, 16f);
            }
        }

        protected override void OnRestyle()
        {
            if (state == null || glyph == null || glyph[count - 1] == null) return;
            for (int i = 0; i < count; i++) Tint(i);
        }

        private void Paint(EnvCanvas c)
        {
            float w = PlacedW;
            if (w <= 0f) return;
            float cw = (w - 8f) / count;
            for (int i = 0; i < count; i++)
            {
                float x = 4f + i * cw;
                bool alarm = state[i] == AvState.Caution || state[i] == AvState.Danger;
                Color s = EnvInk.State(state[i] == AvState.Info ? AvState.Ready : state[i]);
                if (i == nowIndex) c.Quad(x + 1f, 2f, cw - 2f, H - 4f, EnvInk.Alpha(EnvInk.Role("ink"), 0.08f));
                c.Bar(x + 6f, H - 8f, cw - 12f, 3f, rain[i], EnvInk.Track(), alarm ? s : EnvInk.Role("ink-dim"), -1f, Color.clear);
            }
        }
    }
}
