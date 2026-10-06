using NOAvionics;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Core.Contracts;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Base of the growing "what will this look like" strips at the foot of the THIS PILOT pages: a hairline
    /// card, a "// KEY" header with a note on the right, and one quad canvas that the subclass redraws from the
    /// settings it mirrors. The canvas takes whatever height the page gives the preview, so the preview is the
    /// element that soaks up leftover space. Purely a readout: it never writes a setting.
    /// </summary>
    internal abstract class SetPreview : AvPart
    {
        protected const float HeadH = 16f, Inset = 4f;
        protected readonly AvFrame Frame;
        protected readonly TMP_Text Head, Note;
        protected readonly AvQuadGraphic Quads;
        protected float AreaW, AreaH;
        private readonly float natural;

        protected SetPreview(RectTransform parent, string name, string headText, float naturalHeight)
        {
            natural = naturalHeight;
            Rect = AvLay.Child(parent, name);
            Frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
            AvLay.Fill(Frame.rectTransform);
            Frame.raycastTarget = true;
            Head = AvText.Make(Rect, "Head", AvTextRole.Micro, headText);
            AvText.Fit(Head, false);
            Note = AvText.Make(Rect, "Note", AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight);
            AvText.Fit(Note, false);
            var go = new GameObject("Quads", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            Quads = go.AddComponent<AvQuadGraphic>();
            Quads.raycastTarget = false;
            PaintFrame();
        }

        public string Help { set => AvHelpTip.Attach(Frame.gameObject, value); }

        protected void SetNote(string text)
        {
            string t = text ?? "";
            if (Note.text != t) Note.text = t;
        }

        public override float Measure(float width) => natural;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(Head.rectTransform, 8f, 3f, s.W * 0.4f, HeadH);
            AvLay.Place(Note.rectTransform, s.W * 0.4f, 3f, s.W * 0.6f - 8f, HeadH);
            AreaW = Mathf.Max(0f, s.W - 2f * Inset);
            AreaH = Mathf.Max(0f, s.H - HeadH - 6f - Inset);
            AvLay.Place(Quads.rectTransform, Inset, HeadH + 6f, AreaW, AreaH);
            Redraw();
        }

        /// <summary>Draw the canvas from the current state (called when the slot or the settings change).</summary>
        protected void Redraw()
        {
            Quads.Begin();
            if (AreaW > 8f && AreaH > 8f) Draw(AreaW, AreaH);
            Quads.End();
        }

        protected abstract void Draw(float w, float h);

        private void PaintFrame()
        {
            AvStyle card = AvStyleHost.FuiStyle("card");
            Frame.Paint(AvStyleHost.Resolve(card.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(card.Border, AvTheme.Hairline));
        }

        public override void Restyle()
        {
            PaintFrame();
            Head.color = AvStyleHost.FuiColor("ink-muted", AvTheme.Disabled);
            Note.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
            Redraw();
        }

        protected static Color Ground => AvStyleHost.FuiColor("ground", AvTheme.Ground);
        protected static Color Ready => AvStyleHost.FuiColor("ready", AvTheme.RailReady);
        protected static Color Caution => AvStyleHost.FuiColor("caution", AvTheme.RailCaution);
        protected static Color Danger => AvStyleHost.FuiColor("danger", AvTheme.RailDanger);
        protected static Color Info => AvStyleHost.FuiColor("info", AvTheme.RailInfo);
        protected static Color Hairline => AvStyleHost.FuiColor("hairline", AvTheme.Hairline);
        protected static Color Surface => AvStyleHost.FuiColor("surface", AvTheme.Surface);

        protected TMP_Text Label(string name, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            TMP_Text t = AvText.Make(Rect, name, AvTextRole.Micro, "", align);
            AvText.Fit(t, false);
            t.color = AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
            return t;
        }

        protected void PlaceLabel(TMP_Text t, float x, float y, float w) =>
            AvLay.Place(t.rectTransform, Inset + x, HeadH + 6f + y, Mathf.Max(20f, w), 15f);
    }

    /// <summary>DISPLAY: the display finish (tint, CRT lines, edge shading, glass sheen) over a sample of the panel vocabulary.</summary>
    internal sealed class SetFinishPreview : SetPreview
    {
        private static readonly string[] TintNames = { "NEUTRAL", "GREEN", "AMBER", "ICE", "ROSE" };
        private static readonly Color[] Tints =
        {
            Color.white, new Color(.22f, 1f, .48f), new Color(1f, .64f, .18f), new Color(.25f, .75f, 1f), new Color(1f, .38f, .57f),
        };
        private static readonly float[] Wave = { .35f, .6f, .85f, .5f, .95f, .7f, .4f, .8f, .55f };
        private bool effects = true;
        private float glass, scan, edge, strength;
        private int tint;

        public SetFinishPreview(RectTransform parent) : base(parent, "FinishPreview", "// FINISH PREVIEW", 72f)
        {
            Help = "A sample panel under the display finish you have set: tint wash, scan texture, edge shading and the glass sheen, " +
                   "each at its real strength. The finish covers every maximized MFD, this one included.";
            Restyle();
        }

        public void Refresh(CommandSettings settings)
        {
            bool e = settings.DisplayEffects.Value;
            float g = settings.DisplayGlass.Value, sc = settings.DisplayScanlines.Value, ed = settings.DisplayVignette.Value;
            float st = settings.DisplayTintStrength.Value;
            int t = Mathf.Clamp(settings.DisplayTint.Value, 0, TintNames.Length - 1);
            SetNote(!e ? "EFFECTS OFF" : TintNames[t] + (t > 0 ? " " + AvNum.Percent(st) : "") +
                         " · TEXTURE " + AvNum.Percent(sc) + " · EDGE " + AvNum.Percent(ed));
            if (e == effects && Mathf.Approximately(g, glass) && Mathf.Approximately(sc, scan) &&
                Mathf.Approximately(ed, edge) && Mathf.Approximately(st, strength) && t == tint) return;
            effects = e; glass = g; scan = sc; edge = ed; strength = st; tint = t;
            Redraw();
        }

        protected override void Draw(float w, float h)
        {
            Quads.Add(0f, 0f, w, h, Ground);
            float pad = 12f, barH = Mathf.Clamp(h * 0.13f, 8f, 34f), gap = barH * 0.8f, colW = w * 0.5f - pad * 1.5f;
            float y0 = (h - (3f * barH + 2f * gap)) * 0.5f;
            Color[] states = { Ready, Caution, Danger };
            float[] fills = { .8f, .55f, .3f };
            for (int i = 0; i < 3; i++)
            {
                float y = y0 + i * (barH + gap);
                Quads.Add(pad, y, colW, barH, Hairline);
                Quads.Add(pad, y, colW * fills[i], barH, states[i]);
            }
            int n = Wave.Length;
            float ex = w * 0.5f + pad * 0.5f, ew = w * 0.5f - pad * 1.5f, bw = (ew - (n - 1) * 3f) / n;
            Color info = Info;
            for (int k = 0; k < n; k++)
            {
                float bh = Mathf.Max(3f, (h - 2f * pad) * Wave[k]);
                Quads.Add(ex + k * (bw + 3f), h - pad - bh, bw, bh, info);
            }
            if (!effects) return;
            if (tint > 0 && strength > 0f)
            {
                Color c = Tints[tint];
                c.a = strength * .18f;
                Quads.Add(0f, 0f, w, h, c);
            }
            if (scan > 0f)
            {
                var line = new Color(0f, 0f, 0f, scan * .28f);
                for (float y = 0f; y < h; y += 4f) Quads.Add(0f, y, w, 1f, line);
            }
            if (edge > 0f)
            {
                float e = Mathf.Min(w, h) * .3f;
                var dark = new Color(0f, 0f, 0f, edge * .5f);
                var clear = new Color(0f, 0f, 0f, 0f);
                Quads.Add(0f, 0f, w, e, dark, clear, false);
                Quads.Add(0f, h - e, w, e, clear, dark, false);
                Quads.Add(0f, 0f, e, h, dark, clear, true);
                Quads.Add(w - e, 0f, e, h, clear, dark, true);
            }
            if (glass > 0f)
                Quads.Add(0f, 0f, w, h * .42f, new Color(1f, 1f, 1f, glass * .45f * .16f), new Color(1f, 1f, 1f, 0f), false);
        }
    }

    /// <summary>MAP: a schematic of the maximized map — map, console dock, dispatch ticker, terrain, darkening and backdrop as set.</summary>
    internal sealed class SetLayoutPreview : SetPreview
    {
        private readonly TMP_Text mapLabel, consoleLabel, backdropLabel, tickerLabel;
        private bool expanded = true, terrain, relief, ticker;
        private float terrainOpacity, deckOpacity, darkening, backdropStrength;
        private string backdrop = "MATTE";

        public SetLayoutPreview(RectTransform parent) : base(parent, "LayoutPreview", "// CONSOLE LAYOUT", 84f)
        {
            mapLabel = Label("MapLabel");
            consoleLabel = Label("ConsoleLabel");
            backdropLabel = Label("BackdropLabel");
            tickerLabel = Label("TickerLabel");
            mapLabel.text = "MAP";
            consoleLabel.text = "CONSOLE";
            tickerLabel.text = "NEWS TICKER";
            Help = "A schematic of the maximized map as you have set it: the console dock, the news ticker, the terrain image, " +
                   "how far the map is darkened and the backdrop behind the console.";
            Restyle();
        }

        public void Refresh(CommandSettings s)
        {
            bool ex = s.ExpandedMapUi.Value, tr = s.MapTerrainImage.Value, re = s.MapRelief3D.Value, tk = s.NewsTickerEnabled.Value;
            float to = s.MapTerrainOpacity.Value, dk = s.DeckOpacity.Value, dm = s.MapTrayOpacity.Value;
            string bg = SettingsChoices.BackgroundName(s.DeckGrid.Value, s.CheckerboardOverlay.Value,
                s.BackgroundImage.Value, s.BackgroundImagePreset.Value);
            float bs = bg == "CUSTOM" ? s.BackgroundImageOpacity.Value : 0f;
            SetNote(!ex ? "NATIVE LAYOUT" : "EXPANDED · " + (tr ? "TERRAIN " + AvNum.Percent(to) : "NO TERRAIN") +
                          " · DARKEN " + AvNum.Percent(dm));
            if (ex == expanded && tr == terrain && re == relief && tk == ticker && Mathf.Approximately(to, terrainOpacity) &&
                Mathf.Approximately(dk, deckOpacity) && Mathf.Approximately(dm, darkening) &&
                Mathf.Approximately(bs, backdropStrength) && bg == backdrop) return;
            expanded = ex; terrain = tr; relief = re; ticker = tk; terrainOpacity = to; deckOpacity = dk; darkening = dm;
            backdropStrength = bs; backdrop = bg;
            Redraw();
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float mapW = MapWidth(AreaW);
            PlaceLabel(mapLabel, 6f, expanded && ticker ? 18f : 4f, mapW - 12f);
            PlaceLabel(tickerLabel, 6f, 0f, mapW - 12f);
            PlaceLabel(backdropLabel, 6f, AreaH - 17f, mapW - 12f);
            PlaceLabel(consoleLabel, mapW + 12f, 4f, AreaW - mapW - 16f);
        }

        private float MapWidth(float w) => expanded ? w * 0.7f : w;

        protected override void Draw(float w, float h)
        {
            bool bar = expanded && ticker;
            mapLabel.gameObject.SetActive(true);
            tickerLabel.gameObject.SetActive(bar);
            consoleLabel.gameObject.SetActive(expanded);
            backdropLabel.text = "BACKDROP " + backdrop;
            Quads.Add(0f, 0f, w, h, Ground);
            float mapW = MapWidth(w);
            Quads.Add(0f, 0f, mapW, h, Color.Lerp(Ground, Surface, 0.5f));
            if (terrain)
            {
                float a = Mathf.Clamp01(terrainOpacity);
                Quads.Add(0f, 0f, mapW, h, new Color(.20f, .34f, .26f, a));
                Quads.Add(mapW * .08f, h * .42f, mapW * .38f, h * .30f, new Color(.30f, .44f, .30f, a * .8f));
                Quads.Add(mapW * .52f, h * .18f, mapW * .36f, h * .26f, new Color(.16f, .28f, .40f, a * .8f));
                Quads.Add(mapW * .40f, h * .62f, mapW * .50f, h * .22f, new Color(.34f, .30f, .20f, a * .7f));
            }
            Color hair = Hairline;
            if (relief)
            {
                Color line = Info;
                line.a = .45f;
                for (int i = 0; i < 6; i++)
                {
                    float t = i / 5f;
                    Quads.Add(mapW * .06f * (1f - t), h * (.30f + .62f * t * t), mapW * (1f - .12f * (1f - t)), 1f, line);
                }
            }
            if (darkening > 0f) Quads.Add(0f, 0f, mapW, h, new Color(0f, 0f, 0f, Mathf.Clamp01(darkening)));
            if (bar) Quads.Add(0f, 0f, mapW, 14f, new Color(Info.r, Info.g, Info.b, .45f));
            if (expanded)
            {
                Color deck = Surface;
                deck.a = Mathf.Clamp01(deckOpacity);
                float cx = mapW + 4f, cw = w - cx;
                Quads.Add(cx, 0f, cw, h, deck);
                Quads.Add(cx, 0f, 2f, h, Hairline);
                for (int i = 0; i < 8; i++)
                {
                    float y = 26f + i * 18f;
                    if (y + 10f > h - 4f) break;
                    Quads.Add(cx + 8f, y, cw - 16f, 10f, new Color(hair.r, hair.g, hair.b, .8f));
                }
            }
        }
    }

    /// <summary>COCKPIT: a sample of the common HUD element with the size, opacity, contrast, line count and detail lines as set.</summary>
    internal sealed class SetHudPreview : SetPreview
    {
        private readonly IHudBoard board;
        private readonly TMP_Text offsetLabel, stateLabel;
        private static readonly float[] Lines = { .78f, .55f, .68f, .46f, .72f, .52f };
        private bool on = true, details = true, notices = true;
        private int scale, opacity, contrast, rows, offX, offY;

        public SetHudPreview(RectTransform parent, IHudBoard hudBoard) : base(parent, "HudPreview", "// STATUS ELEMENT", 72f)
        {
            board = hudBoard;
            offsetLabel = Label("OffsetLabel", TextAlignmentOptions.MidlineRight);
            stateLabel = Label("StateLabel");
            Help = "A sample of the common HUD element with the size, opacity, contrast, line count and detail lines you have set. " +
                   "It shows the look, not the position: the offsets move the element from its dock and are clamped to the safe area.";
            Restyle();
        }

        public void Refresh()
        {
            if (board == null)
            {
                SetNote("HUD SERVICE UNAVAILABLE");
                if (on) { on = false; Redraw(); }
                return;
            }
            bool e = board.Enabled, d = board.ShowDetails, n = board.NoticesEnabled;
            int sc = HudLayout.ClampScale(board.ScaleStep), op = HudLayout.ClampOpacity(board.OpacityStep);
            int co = Mathf.Clamp(board.Contrast, 0, 2), r = HudLayout.ClampRows(board.MaxRows);
            SetNote(HudLayout.ScaleName(sc) + " · " + HudLayout.ContrastName(co) + " · " + r + (r == 1 ? " LINE" : " LINES"));
            string off = "OFFSET " + AvNum.Fixed(board.OffsetX, 0) + " / " + AvNum.Fixed(board.OffsetY, 0) + " PX";
            if (offsetLabel.text != off) offsetLabel.text = off;
            if (e == on && d == details && n == notices && sc == scale && op == opacity && co == contrast && r == rows &&
                board.OffsetX == offX && board.OffsetY == offY) return;
            on = e; details = d; notices = n; scale = sc; opacity = op; contrast = co; rows = r;
            offX = board.OffsetX; offY = board.OffsetY;
            Redraw();
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            PlaceLabel(offsetLabel, AreaW - 190f, AreaH - 17f, 184f);
            PlaceLabel(stateLabel, 6f, AreaH - 17f, Mathf.Max(20f, AreaW - 200f));
        }

        protected override void Draw(float w, float h)
        {
            Color ground = Ground;
            Quads.Add(0f, 0f, w, h, Color.Lerp(ground, Info, .12f), ground, false);
            if (board == null) { stateLabel.text = "NO HUD ELEMENT IN THIS SESSION"; return; }
            float alpha = HudLayout.Opacity(opacity);
            stateLabel.text = !on ? "ELEMENT OFF" : alpha <= 0f ? "OPACITY OFF · HIDDEN" : "";
            // The sample scales with the strip: a tall preview draws the element larger, not lonelier.
            int shown = Mathf.Clamp(rows, 1, 6);
            float k = HudLayout.Scale(scale), baseRow = 16f * k, baseW = Mathf.Max(120f, 200f * k), baseH = 22f + shown * baseRow;
            float z = Mathf.Clamp(Mathf.Min((h - 24f) / baseH, w * 0.7f / baseW), 0.5f, 2.4f);
            float rowH = baseRow * z, boxW = baseW * z, boxH = baseH * z;
            float bx = 12f, by = Mathf.Max(6f, (h - 20f - boxH) * 0.5f);
            float a = on ? Mathf.Max(alpha, 0.12f) : 0.12f;
            if (contrast == 1) Quads.Add(bx, by, boxW, boxH, new Color(0f, 0f, 0f, .40f * a));
            else if (contrast == 2) Quads.Add(bx, by, boxW, boxH, new Color(0f, 0f, 0f, .90f * a));
            Color info = Info, ready = Ready, caution = Caution, ink = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            info.a *= a; ready.a *= a; caution.a *= a; ink.a *= a;
            Quads.Add(bx + 6f * z, by + 5f * z, boxW * .3f, 8f * z, info);
            for (int i = 0; i < shown; i++)
            {
                float y = by + 20f * z + i * rowH;
                bool notice = notices && i == shown - 1 && shown > 1;
                Color c = notice ? caution : i % 2 == 0 ? ready : info;
                Quads.Add(bx + 6f * z, y + rowH * .18f, rowH * .42f, rowH * .42f, c);
                float textX = bx + 8f * z + rowH * .5f, textW = (boxW - 24f * z - rowH * .5f) * Lines[i % Lines.Length];
                Quads.Add(textX, y + rowH * .2f, textW, Mathf.Max(3f, rowH * .32f), ink);
                if (details)
                    Quads.Add(textX, y + rowH * .7f, (boxW - 24f * z - rowH * .5f) * .5f, Mathf.Max(2f, 2f * z), new Color(ink.r, ink.g, ink.b, ink.a * .5f));
            }
        }
    }
}
