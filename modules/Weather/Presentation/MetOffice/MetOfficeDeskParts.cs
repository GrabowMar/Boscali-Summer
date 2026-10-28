using System;
using BoscaliSummer.Modules.Weather.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Presentation
{
    /// <summary>The figures a sky is drawn with on its tile: cover and base of the high (0), mid (1) and low (2) layer.</summary>
    internal static class MetSkyFacts
    {
        public static float Cover(SkyDef d, int layer) => layer switch
        {
            0 => d.HasHigh ? Mathf.Clamp01(d.Nominal.High.Cover) : 0f,
            1 => d.HasMid ? Mathf.Clamp01(d.Nominal.Mid.Cover) : 0f,
            _ => d.HasLow ? Mathf.Clamp01(Mathf.Max(d.Nominal.SheetCover, d.Nominal.CumulusCover)) : 0f,
        };

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
    /// A tile click sets its sky; the sky the host set is latched and outlined.
    /// </summary>
    internal sealed class MetSkyGallery : AvPart
    {
        public const int Columns = 4, TileCount = 7;
        private readonly EnvGraphic[] art = new EnvGraphic[TileCount];
        private readonly AvControl[] tile = new AvControl[TileCount];
        private readonly int[] sky = new int[TileCount];
        private int pinned = -1;

        public AvControl Auto { get; }

        public MetSkyGallery(RectTransform parent, Action<int> pin, AvControl.Spec autoSpec)
        {
            Rect = AvLay.Child(parent, "Sky gallery");
            for (int i = 0; i < TileCount; i++)
            {
                int slot = i;
                sky[i] = -1;
                art[i] = EnvGraphic.Create(Rect, "TileArt" + i);
                art[i].Draw = c => Paint(c, slot);
                tile[i] = AvControl.Make(Rect, new AvControl.Spec("", () => { if (sky[slot] >= 0) pin(sky[slot]); }));
            }
            Auto = AvControl.Make(Rect, autoSpec);
        }

        public void Show(WeatherRegimeType cls, int pinnedSky, int overheadSky, bool enabled, string help)
        {
            pinned = pinnedSky;
            int n = 0;
            for (int i = 0; i < SkyLibrary.Count && n < TileCount; i++)
            {
                if (SkyLibrary.ClassOf(i) != cls) continue;
                SkyDef d = SkyLibrary.Get(i);
                sky[n] = i;
                string flag = i == pinnedSky ? " · SET" : i == overheadSky ? " · OVERHEAD" : "";
                string label = TwoLines(d.Name) + "\n#" + d.Number + " · " + MetSkyFacts.Code(d) + flag;
                if (tile[n].Label != label) tile[n].Label = label;
                tile[n].Latched = i == pinnedSky;
                tile[n].Interactable = enabled;
                tile[n].Help = help;
                n++;
            }
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
            Color edge = sky[slot] == pinned ? EnvInk.Role("key") : hair;
            c.Quad(0f, 0f, w, 1f, edge); c.Quad(0f, 0f, 1f, h, edge); c.Quad(w - 1f, 0f, 1f, h, edge);
        }

        public override void Restyle()
        {
            for (int i = 0; i < TileCount; i++) { tile[i].Restyle(); art[i].SetVerticesDirty(); }
            Auto.Restyle();
        }
    }
}
