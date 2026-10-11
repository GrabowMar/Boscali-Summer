using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>
    /// The inside of the SKY NOW card (R1, R1b): the pictogram, class code and word, cover, the sky's name line, the METAR line and the three layer tiles.
    /// Frameless (the card owns the frame). Values arrive as a <see cref="MetOfficeModel"/>; nothing here reads the game.
    /// </summary>
    internal sealed class MetSkyBody : EnvPart
    {
        private const float HeadY = 44f, MetarY = 62f, TileY = 84f, TileH = 36f, TileGap = 4f;
        private readonly TMP_Text code, word, cover, coverCap, head, metarKey, metarText;
        private readonly TMP_Text[] layerKey = new TMP_Text[3], layerBase = new TMP_Text[3], layerDetail = new TMP_Text[3];
        private readonly float[] layerCover = new float[3];
        private readonly WeatherGlyph glyph;
        private static readonly string[] LayerNames = { "HIGH", "MID", "LOW" };
        private AvState state = AvState.Info;

        public MetSkyBody(RectTransform parent) : base(parent, "Sky now", null)
        {
            MakeArt(Paint);
            glyph = WeatherGlyph.Create(Rect, new Rect(0f, 0f, 40f, 40f));
            code = Txt("Code", AvTextRole.Display, TextAlignmentOptions.MidlineLeft, "ink");
            word = Txt("Word", AvTextRole.Title, TextAlignmentOptions.MidlineLeft, "ink", false, true);
            cover = Txt("Cover", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight, "ink");
            coverCap = Txt("CoverCap", AvTextRole.Micro, TextAlignmentOptions.MidlineRight, "ink-muted");
            coverCap.text = "COVER";
            head = Txt("Head", AvTextRole.Label, TextAlignmentOptions.MidlineLeft, "ink-dim", false, true);
            metarKey = Txt("MetarKey", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "info");
            metarKey.text = "METAR";
            metarText = Txt("Metar", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft, "ink", false, true);
            for (int i = 0; i < 3; i++)
            {
                layerKey[i] = Txt("LayerKey" + i, AvTextRole.DataStrong, TextAlignmentOptions.MidlineLeft, i == 0 ? "info" : "caution", false, true);
                layerBase[i] = Txt("LayerBase" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight, "ink", false, true);
                layerDetail[i] = Txt("LayerDetail" + i, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft, "ink-muted", false, true);
            }
            Restyle();
        }

        public override float Measure(float width) => MetOfficeLayout.SkyCard - MetOfficeLayout.CardChrome;

        /// <summary>The card's rail colour for the class showing.</summary>
        public AvState RailState => state;

        public void Set(MetOfficeModel m)
        {
            RegimeSnapshot regime = RegimeSnapshot.FromType(m.Class);
            state = WeatherEnvView.RegimeState(m.Class);
            AvText.Set(code, AvStates.Glyph(state) + regime.Code);
            AvText.Set(word, regime.Name.ToUpperInvariant());
            AvText.Set(cover, MetOfficeText.Cover(m.Cover));
            AvText.Set(head, m.PinnedSky >= 0 ? MetOfficeText.SkyHead(m.SkyNumber, m.SkyName.ToUpperInvariant()) + " · PINNED" : "AUTO · CLASS SKY");
            AvText.Set(metarText, m.Metar.Length > 0 ? m.Metar : "—");
            glyph.SetKind(m.Class);
            for (int i = 0; i < 3; i++)
            {
                MetLayer l = m.Layers[i];
                layerCover[i] = l.Present ? Mathf.Clamp01(l.Cover) : 0f;
                AvText.Set(layerKey[i], string.IsNullOrEmpty(l.Label) ? LayerNames[i] : l.Label);
                AvText.Set(layerBase[i], l.Present ? MetOfficeText.BaseKm(l.BaseMetres) : "—");
                string oktas = l.Present ? WeatherMetar.Oktas(l.Cover) : "";
                AvText.Set(layerDetail[i], l.Present ? (oktas.Length > 0 ? oktas : "CLR") + " " + MetOfficeText.Cover(l.Cover) : "");
            }
            Tint();
            Redraw();
        }

        protected override void Layout(float w, float h)
        {
            AvLay.Place(glyph.rectTransform, 0f, 0f, 40f, 40f);
            AvLay.Place(code, 46f, 0f, 84f, 40f);
            AvLay.Place(word, 132f, 0f, Mathf.Max(1f, w - 132f - 58f), 40f);
            AvLay.Place(cover, w - 56f, 2f, 56f, 18f);
            AvLay.Place(coverCap, w - 56f, 20f, 56f, 15f);
            AvLay.Place(head, 0f, HeadY, w, 16f);
            AvLay.Place(metarKey, 6f, MetarY, 44f, 19f);
            AvLay.Place(metarText, 52f, MetarY, Mathf.Max(1f, w - 56f), 19f);
            float tw = (w - 2f * TileGap) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float x = i * (tw + TileGap);
                AvLay.Place(layerKey[i], x + 5f, TileY + 2f, 34f, 16f);
                AvLay.Place(layerBase[i], x + 40f, TileY + 2f, tw - 45f, 16f);
                AvLay.Place(layerDetail[i], x + 5f, TileY + 18f, tw - 10f, 15f);
            }
        }

        private void Tint()
        {
            bool alarm = state == AvState.Caution || state == AvState.Danger;
            Color s = EnvInk.State(state);
            glyph.color = alarm ? s : EnvInk.Role("ink");
            word.color = alarm ? s : EnvInk.Role("ink");
            code.color = alarm ? s : EnvInk.Role("ink");
            glyph.SetVerticesDirty();
        }

        protected override void OnRestyle() { if (glyph != null) Tint(); }

        private void Paint(EnvCanvas c)
        {
            float w = PlacedW;
            if (w <= 0f) return;
            Color sunken = EnvInk.Role("surface-sunken"), hair = EnvInk.Role("hairline");
            c.Quad(0f, MetarY, w, 19f, sunken);
            c.Quad(0f, MetarY, 2f, 19f, EnvInk.Role("info"));
            float tw = (w - 2f * TileGap) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float x = i * (tw + TileGap);
                c.Quad(x, TileY, tw, TileH, sunken);
                c.Quad(x, TileY, tw, 1f, hair); c.Quad(x, TileY + TileH - 1f, tw, 1f, hair);
                c.Quad(x, TileY, 1f, TileH, hair); c.Quad(x + tw - 1f, TileY, 1f, TileH, hair);
                c.Bar(x + 5f, TileY + TileH - 5f, tw - 10f, 3f, layerCover[i], EnvInk.Track(), EnvInk.Role(i == 0 ? "info" : "caution"), -1f, Color.clear);
            }
        }
    }
}
