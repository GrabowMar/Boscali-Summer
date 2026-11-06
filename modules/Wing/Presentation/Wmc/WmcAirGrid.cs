using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The airframe grid SUPPLY and LOADOUT share (remade 2026-11-05; replaces the paged one-line rows and LOADOUT's ◂ ▸ row):
    /// every listed airframe at once as a tile, three across — silhouette, code, name, and a foot line (left: price or what it carries;
    /// right: a status word in its level colour). The picked tile is lit and bracketed. Tiles are pooled and rebound only when their key
    /// changes; one inert line says why when nothing is listed. ponytail: a fixed pool (<see cref="Capacity"/>); a longer list shows its
    /// first tiles only — grow the pool if the game ever lists more airframes.</summary>
    internal sealed class WmcAirGrid : AvPart
    {
        public const int Capacity = 30, Columns = 3;
        private const float TileH = 58f, Gap = 4f;

        private sealed class Tile
        {
            public RectTransform Root;
            public AvFrame Frame;
            public Image Rail, Icon;
            public TMP_Text Code, Name, Foot, Status;
            public string CodeFull, NameFull, FootFull, StatusFull, Level = "";
            public int Key = int.MinValue;
            public bool Selected, Enabled = true, Hover, Shown;
            public AvState State = AvState.Inert;
        }

        private readonly Tile[] tiles = new Tile[Capacity];
        private readonly TMP_Text emptyText;
        private bool empty;
        private float tileW = 140f;

        public WmcAirGrid(RectTransform parent, Action<int> pick)
        {
            Rect = AvLay.Child(parent, "AirGrid");
            for (int i = 0; i < Capacity; i++) tiles[i] = Build(i, pick);
            emptyText = AvText.Make(Rect, "EmptyText", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            emptyText.gameObject.SetActive(false);
            Restyle();
        }

        private Tile Build(int slot, Action<int> pick)
        {
            var t = new Tile { Root = AvLay.Child(Rect, "Tile" + slot) };
            t.Frame = WmcDraw.FillFrame(t.Root, AvChamfer.Diagonal(5f));
            t.Rail = AvLay.Solid(t.Root, "Rail", Color.clear);
            t.Icon = AvLay.Solid(t.Root, "Icon", Color.white);
            t.Icon.preserveAspect = true;
            t.Code = WmcSheet.Text(t.Root, "Code", AvTextRole.Label);
            t.Name = WmcSheet.Text(t.Root, "Name", AvTextRole.ProseSmall);
            t.Foot = WmcSheet.Text(t.Root, "Foot", AvTextRole.DataSmall);
            t.Status = WmcSheet.Text(t.Root, "Status", AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight);
            AvHit hit = AvHit.On(t.Frame);
            hit.Hover = h => { t.Hover = h; Paint(t); };
            hit.Click = e => pick(slot);
            t.Root.gameObject.SetActive(false);
            return t;
        }

        /// <summary>Whether tile <paramref name="slot"/> must be bound again (its key moved, or it is hidden).</summary>
        public bool NeedsBind(int slot, int key)
        {
            Tile t = tiles[slot];
            if (t.Key == key && t.Shown) return false;
            t.Key = key;
            return true;
        }

        /// <summary><paramref name="level"/> colours the status word ("ok", "warn", "bad", "" dim); <paramref name="railClass"/> the rail.</summary>
        public void Bind(int slot, Sprite icon, string code, string name, string foot, string status, string level, string railClass,
            bool selected, bool enabled, string tip)
        {
            Tile t = tiles[slot];
            if (!t.Shown)
            {
                t.Shown = true;
                t.Root.gameObject.SetActive(true);
                Changed();
            }
            t.Icon.sprite = icon;
            t.Icon.enabled = icon != null;
            t.CodeFull = code ?? "";
            t.NameFull = name ?? "";
            t.FootFull = foot ?? "";
            t.StatusFull = status ?? "";
            t.Level = level ?? "";
            t.State = WmcState.Of(railClass);
            t.Selected = selected;
            t.Enabled = enabled;
            AvHit hit = t.Frame.GetComponent<AvHit>();
            if (hit != null) hit.Interactable = enabled;
            AvHelpTip.Attach(t.Frame.gameObject, tip);
            Fit(t);
            Paint(t);
        }

        public void Hide(int slot)
        {
            Tile t = tiles[slot];
            t.Key = int.MinValue;
            if (!t.Shown) return;
            t.Shown = false;
            t.Root.gameObject.SetActive(false);
            Changed();
        }

        /// <summary>One inert line saying why nothing is listed; null hides it.</summary>
        public void ShowEmpty(string text)
        {
            bool on = text != null;
            if (on) AvText.Set(emptyText, text);
            if (on == empty) return;
            empty = on;
            emptyText.gameObject.SetActive(on);
            Changed();
        }

        private int ShownCount()
        {
            int n = 0;
            foreach (Tile t in tiles)
                if (t.Shown) n++;
            return n;
        }

        public override float Measure(float w)
        {
            if (empty) return Mathf.Max(WmcSheet.RowH, AvText.Height(emptyText, w - 16f) + 10f);
            int rows = (ShownCount() + Columns - 1) / Columns;
            return rows == 0 ? 0f : rows * (TileH + Gap) - Gap;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            tileW = AvFlowMath.ColumnWidth(s.W, Columns, Gap);
            AvLay.Place(emptyText.rectTransform, 8f, 5f, Mathf.Max(0f, s.W - 16f), AvText.Height(emptyText, s.W - 16f));
            int at = 0;
            foreach (Tile t in tiles)
            {
                if (!t.Shown) continue;
                AvLay.Place(t.Root, at % Columns * (tileW + Gap), at / Columns * (TileH + Gap), tileW, TileH);
                at++;
                Fit(t);
            }
        }

        private void Fit(Tile t)
        {
            float w = tileW, textX = 44f, textW = Mathf.Max(0f, w - textX - 6f);
            AvLay.Place(t.Rail.rectTransform, 0f, 0f, 3f, TileH);
            AvLay.Place(t.Icon.rectTransform, 8f, 6f, 30f, 30f);
            WmcSheet.SetFit(t.Code, t.CodeFull, textW);
            AvLay.Place(t.Code.rectTransform, textX, 4f, textW, 18f);
            WmcSheet.SetFit(t.Name, t.NameFull, textW);
            AvLay.Place(t.Name.rectTransform, textX, 21f, textW, 15f);
            // The status word takes what it needs (at most 60 %), the foot (price, ON SUPPLY) the rest: neither is cut short.
            float inner = Mathf.Max(0f, w - 12f);
            AvText.Set(t.Status, t.StatusFull);
            float sw = Mathf.Min(inner * 0.55f, AvText.Width(t.Status) + 2f);
            WmcSheet.SetFit(t.Status, t.StatusFull, sw);
            AvLay.Place(t.Status.rectTransform, w - 6f - sw, TileH - 19f, sw, 15f);
            float fw = Mathf.Max(0f, inner - sw - 2f);
            WmcSheet.SetFit(t.Foot, t.FootFull, fw);
            AvLay.Place(t.Foot.rectTransform, 7f, TileH - 19f, fw, 15f);
        }

        private void Paint(Tile t)
        {
            WmcSheet.PaintRow(t.Frame, t.Rail, t.State, !t.Enabled ? "disabled" : t.Selected ? "armed" : t.Hover ? "hover" : null);
            t.Frame.Bracket = t.Selected ? 6f : 0f;
            t.Frame.BracketColor = AvStyleHost.FuiColor("select", AvTheme.Accent);
            t.Frame.SetVerticesDirty();
            t.Code.color = !t.Enabled ? AvTheme.Disabled : AvInk.Ink;
            t.Name.color = AvInk.Dim;
            t.Foot.color = AvInk.Ink;
            t.Status.color = t.Level.Length == 0 ? AvInk.Dim : WmcSheet.Level(t.Level);
            t.Icon.color = t.Selected ? Color.white : t.Enabled ? AvTheme.Friendly : AvTheme.Dim;
        }

        public override void Restyle()
        {
            foreach (Tile t in tiles)
                if (t != null) Paint(t);
            if (emptyText != null) emptyText.color = AvInk.Dim;
        }
    }
}
