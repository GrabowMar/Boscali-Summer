using NOAvionics;
using BoscaliSummer.Modules.Command.Configuration;
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
            string bg = SettingsChoices.BackgroundName(
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
}
