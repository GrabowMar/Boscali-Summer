using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>One card of the hero row: a caption, a state word on the right, one large line and two small ones, with a rail in the state's colour. Read-only.</summary>
    internal sealed class MetHero : AvPart
    {
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text caption, tag, head, line1, line2;
        private AvState state = AvState.Info;

        public MetHero(RectTransform parent, string title)
        {
            Rect = AvLay.Child(parent, "Hero " + title);
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            frame.Bracket = 8f;
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            caption = Make("Caption", AvTextRole.Label, TextAlignmentOptions.MidlineLeft);
            caption.text = title;
            tag = Make("Tag", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            head = Make("Head", AvTextRole.Headline, TextAlignmentOptions.MidlineLeft);
            line1 = Make("Line1", AvTextRole.DataStrong, TextAlignmentOptions.MidlineLeft);
            line2 = Make("Line2", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
            AvHelpTip.Attach(frame.gameObject, MetCatalog.HeroHelp);
            Restyle();
        }

        private TMP_Text Make(string name, AvTextRole role, TextAlignmentOptions align)
        {
            TMP_Text t = AvText.Make(Rect, name, role, "", align);
            AvText.Fit(t, false);
            return t;
        }

        public void Set(string tagText, string headText, string first, string second, AvState s)
        {
            AvText.Set(tag, tagText); AvText.Set(head, headText); AvText.Set(line1, first); AvText.Set(line2, second);
            if (s != state) { state = s; Restyle(); }
        }

        public override float Measure(float width) => MetOfficeLayout.Hero;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = s.W - 24f;
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(caption.rectTransform, 12f, 5f, w * 0.5f, 14f);
            AvLay.Place(tag.rectTransform, 12f + w * 0.5f, 5f, w * 0.5f, 14f);
            AvLay.Place(head.rectTransform, 12f, 19f, w, 32f);
            AvLay.Place(line1.rectTransform, 12f, 51f, w, 15f);
            AvLay.Place(line2.rectTransform, 12f, 66f, w, 14f);
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("card inert");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Surface), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            frame.BracketColor = AvStyleHost.FuiFill("card-bracket", AvTheme.Frame);
            frame.SetVerticesDirty();
            Color tint = EnvInk.State(state == AvState.Info ? AvState.Ready : state);
            rail.color = tint;
            caption.color = EnvInk.Role("key");
            tag.color = state == AvState.Inert ? EnvInk.Role("ink-muted") : tint;
            head.color = EnvInk.Role("ink");
            line1.color = EnvInk.Role("ink");
            line2.color = EnvInk.Role("ink-muted");
        }
    }

    /// <summary>Parts side by side in one row: fixed widths, and a part of width 0 takes the room that is left.</summary>
    internal sealed class MetHRow : AvPart
    {
        private const float Gap = 12f;
        private readonly List<AvPart> parts = new List<AvPart>(4);
        private readonly List<float> widths = new List<float>(4);

        public MetHRow(RectTransform parent) { Rect = AvLay.Child(parent, "HRow"); }

        public T Add<T>(T part, float width) where T : AvPart { parts.Add(part); widths.Add(width); return part; }

        public override float Measure(float width) => MetOfficeLayout.Row;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float fixedW = 0f;
            for (int i = 0; i < parts.Count; i++) fixedW += widths[i];
            float flex = Mathf.Max(1f, s.W - fixedW - Gap * (parts.Count - 1));
            float x = 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                float w = widths[i] > 0f ? widths[i] : flex;
                parts[i].Place(new AvSlot(x, 0f, w, s.H));
                x += w + Gap;
            }
        }

        public override void Restyle() { for (int i = 0; i < parts.Count; i++) parts[i].Restyle(); }
    }

    /// <summary>The figures a sky is drawn with on its tile and in the inspector: cover and base of the high (0), mid (1) and low (2) layer.</summary>
    internal static class MetSkyFacts
    {
        public static float Cover(SkyDef d, int layer)
        {
            switch (layer)
            {
                case 0: return d.HasHigh ? Mathf.Clamp01(d.Nominal.High.Cover) : 0f;
                case 1: return d.HasMid ? Mathf.Clamp01(d.Nominal.Mid.Cover) : 0f;
                default: return d.HasLow ? Mathf.Clamp01(Mathf.Max(d.Nominal.SheetCover, d.Nominal.CumulusCover)) : 0f;
            }
        }

        public static float Base(SkyDef d, int layer) => layer == 0 ? d.Nominal.High.Base : layer == 1 ? d.Nominal.Mid.Base : d.Nominal.CloudBase;

        /// <summary>The METAR-style group of the lowest layer that has cloud, or SKC.</summary>
        public static string Code(SkyDef d)
        {
            for (int layer = 2; layer >= 0; layer--)
            {
                string group = MetOfficeText.LayerGroup(Cover(d, layer), Base(d, layer));
                if (group.Length > 0) return group;
            }
            return "SKC";
        }

        public static Color Tint(WeatherRegimeType cls)
        {
            AvState s = WeatherEnvView.RegimeState(cls);
            return EnvInk.State(s == AvState.Info ? AvState.Ready : s);
        }
    }

    /// <summary>
    /// The sky gallery: the skies of one class as tiles (cover bars for high, mid and low over a button with the number, name and code) and the AUTO tile in the last slot.
    /// A tile only selects its sky for the inspector; nothing is pinned from here.
    /// </summary>
    internal sealed class MetSkyGallery : AvPart
    {
        public const int Columns = 4, TileCount = 7;
        private readonly EnvGraphic[] art = new EnvGraphic[TileCount];
        private readonly AvControl[] tile = new AvControl[TileCount];
        private readonly int[] sky = new int[TileCount];
        private int selected = -1;

        public AvControl Auto { get; }
        public AvControl[] Tiles => tile;
        public int Shown { get; private set; }

        public MetSkyGallery(RectTransform parent, Action<int> select, AvControl.Spec autoSpec)
        {
            Rect = AvLay.Child(parent, "Sky gallery");
            for (int i = 0; i < TileCount; i++)
            {
                int slot = i;
                sky[i] = -1;
                art[i] = EnvGraphic.Create(Rect, "TileArt" + i);
                art[i].Draw = c => Paint(c, slot);
                tile[i] = AvControl.Make(Rect, new AvControl.Spec("", () => { if (sky[slot] >= 0) select(sky[slot]); }));
                tile[i].Help = MetCatalog.TileHelp;
            }
            Auto = AvControl.Make(Rect, autoSpec);
        }

        public int SkyAt(int slot) => slot >= 0 && slot < TileCount ? sky[slot] : -1;

        public void Show(WeatherRegimeType cls, int selectedSky, int pinnedSky, int overheadSky)
        {
            selected = selectedSky;
            int n = 0;
            for (int i = 0; i < SkyLibrary.Count && n < TileCount; i++)
            {
                if (SkyLibrary.ClassOf(i) != cls) continue;
                SkyDef d = SkyLibrary.Get(i);
                sky[n] = i;
                string flag = i == pinnedSky ? " · PINNED" : i == overheadSky ? " · OVERHEAD" : "";
                string label = TwoLines(d.Name) + "\n#" + d.Number + " · " + MetSkyFacts.Code(d) + flag;
                if (tile[n].Label != label) tile[n].Label = label;
                tile[n].Latched = i == selectedSky;
                n++;
            }
            Shown = n;
            for (int i = 0; i < TileCount; i++)
            {
                if (i >= n) sky[i] = -1;
                tile[i].gameObject.SetActive(i < n);
                art[i].gameObject.SetActive(i < n);
                art[i].SetVerticesDirty();
            }
        }

        /// <summary>A long sky name on two lines, broken at the space nearest its middle, so it never runs to the edge of its tile.</summary>
        private static string TwoLines(string name)
        {
            if (name.Length <= 16) return name;
            int best = -1;
            for (int i = 1; i < name.Length - 1; i++)
                if (name[i] == ' ' && (best < 0 || Math.Abs(i - name.Length / 2) < Math.Abs(best - name.Length / 2))) best = i;
            return best < 0 ? name : name.Substring(0, best) + "\n" + name.Substring(best + 1);
        }

        public override float Measure(float width) => MetOfficeLayout.Gallery;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float cw = AvFlowMath.ColumnWidth(s.W, Columns, MetOfficeLayout.CellGap);
            for (int i = 0; i <= TileCount; i++)
            {
                float x = (i % Columns) * (cw + MetOfficeLayout.CellGap), y = (i / Columns) * (MetOfficeLayout.Tile + MetOfficeLayout.CellGap);
                if (i == TileCount) { AvLay.Place(Auto.Rect, x, y, cw, MetOfficeLayout.Tile); break; }
                AvLay.Place(art[i].rectTransform, x, y, cw, MetOfficeLayout.TileArt);
                AvLay.Place(tile[i].Rect, x, y + MetOfficeLayout.TileArt, cw, MetOfficeLayout.TileButton);
            }
        }

        private void Paint(EnvCanvas c, int slot)
        {
            if (sky[slot] < 0) return;
            float w = art[slot].rectTransform.rect.width, h = MetOfficeLayout.TileArt;
            if (w <= 0f) return;
            SkyDef d = SkyLibrary.Get(sky[slot]);
            Color tint = MetSkyFacts.Tint(d.Class), hair = EnvInk.Role("hairline");
            c.Quad(0f, 0f, w, h, EnvInk.Role("surface-sunken"));
            for (int layer = 0; layer < 3; layer++)
            {
                float y = 10f + layer * 18f, cover = MetSkyFacts.Cover(d, layer);
                c.Quad(6f, y, w - 12f, 10f, EnvInk.Alpha(hair, 0.5f));
                if (cover > 0.01f) c.Quad(6f, y, (w - 12f) * cover, 10f, EnvInk.Alpha(tint, 0.35f + 0.55f * cover));
            }
            Color edge = sky[slot] == selected ? EnvInk.Role("key") : hair;
            c.Quad(0f, 0f, w, 1f, edge); c.Quad(0f, 0f, 1f, h, edge); c.Quad(w - 1f, 0f, 1f, h, edge);
        }

        public override void Restyle()
        {
            for (int i = 0; i < TileCount; i++) { tile[i].Restyle(); art[i].SetVerticesDirty(); }
            Auto.Restyle();
        }
    }

    /// <summary>The inspector's three layer lines for the selected sky: the layer, a cover bar, its base and cover in figures.</summary>
    internal sealed class MetInspectBody : EnvPart
    {
        private const float NameW = 44f, ValueW = 150f;
        private static readonly string[] Names = { "HIGH", "MID", "LOW" };
        private readonly TMP_Text[] name = new TMP_Text[3], value = new TMP_Text[3];
        private int sky = -1;

        public MetInspectBody(RectTransform parent) : base(parent, "Inspect layers", null)
        {
            MakeArt(Paint);
            for (int i = 0; i < 3; i++)
            {
                name[i] = Txt("Layer" + i, AvTextRole.Label, TextAlignmentOptions.MidlineLeft, "ink-dim");
                name[i].text = Names[i];
                value[i] = Txt("LayerValue" + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight, "ink", false, true);
            }
            Restyle();
        }

        public void Set(int skyIndex)
        {
            sky = skyIndex;
            SkyDef d = sky >= 0 && sky < SkyLibrary.Count ? SkyLibrary.Get(sky) : null;
            for (int i = 0; i < 3; i++)
            {
                float cover = d != null ? MetSkyFacts.Cover(d, i) : 0f;
                AvText.Set(value[i], cover >= 0.02f ? AvNum.Fixed(MetSkyFacts.Base(d, i), 0) + " M · " + MetOfficeText.Cover(cover) : "NONE");
            }
            Redraw();
        }

        public override float Measure(float width) => 3f * MetOfficeLayout.InspectRow;

        protected override void Layout(float w, float h)
        {
            for (int i = 0; i < 3; i++)
            {
                AvLay.Place(name[i], 0f, i * MetOfficeLayout.InspectRow, NameW, MetOfficeLayout.InspectRow);
                AvLay.Place(value[i], w - ValueW, i * MetOfficeLayout.InspectRow, ValueW, MetOfficeLayout.InspectRow);
            }
        }

        private void Paint(EnvCanvas c)
        {
            float w = PlacedW - NameW - ValueW - 16f;
            if (w <= 0f) return;
            SkyDef d = sky >= 0 && sky < SkyLibrary.Count ? SkyLibrary.Get(sky) : null;
            Color tint = d != null ? MetSkyFacts.Tint(d.Class) : EnvInk.Role("ink-muted");
            for (int i = 0; i < 3; i++)
            {
                float y = i * MetOfficeLayout.InspectRow + 8f, cover = d != null ? MetSkyFacts.Cover(d, i) : 0f;
                c.Quad(NameW + 8f, y, w, 8f, EnvInk.Track());
                if (cover > 0.01f) c.Quad(NameW + 8f, y, w * cover, 8f, EnvInk.Alpha(tint, 0.9f));
            }
        }
    }
}
