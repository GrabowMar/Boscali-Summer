using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        /// <summary>Explanatory copy under a heading: dim, wrapped, never truncated. Built from kit v2 primitives
        /// (AvPart + AvText) because the kit has no bare "note" part — only wrapping the current console kind
        /// (AvSection/AvAlert/AvRow) has a fixed shape.</summary>
        private sealed class ProseNote : AvPart
        {
            private readonly TMP_Text text;

            public ProseNote(RectTransform parent, string body)
            {
                Rect = AvLay.Child(parent, "Note");
                text = AvText.Make(Rect, "Text", AvTextRole.ProseSmall, body ?? "", TextAlignmentOptions.TopLeft, true);
                Restyle();
            }

            public void Set(string body) => text.text = body ?? "";

            public override float Measure(float width) => AvText.Height(text, width);

            public override void Place(AvSlot s)
            {
                base.Place(s);
                AvLay.Place(text.rectTransform, 0f, 0f, s.W, s.H);
            }

            public override void Restyle() =>
                text.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
        }

        /// <summary>
        /// A row of equal-width latched buttons choosing one of a few named views. Kit v2's
        /// <see cref="AvSegmented"/> caps its group at 62% of the width, which wraps or clips
        /// four long labels ("MANPOWER", "BUILDINGS") mid-word; equal columns across the whole
        /// row keep every label whole, so 3-4 way pickers use this instead.
        /// </summary>
        private sealed class ChoiceRow
        {
            private readonly AvControl[] controls;
            private readonly Func<int> get;

            public ChoiceRow(AvFlow page, string[] labels, Func<int> get, Action<int> set)
            {
                this.get = get;
                var specs = new AvControl.Spec[labels.Length];
                for (int i = 0; i < specs.Length; i++)
                {
                    int index = i;
                    specs[i] = new AvControl.Spec(labels[i], () => { set(index); Refresh(); }, AvButtonStyle.Tab);
                }
                controls = page.Buttons(specs).Controls;
                Refresh();
            }

            /// <summary>The <paramref name="index"/>th choice button (for hover help).</summary>
            public AvControl this[int index] => controls[index];

            public void Refresh()
            {
                int selected = get();
                for (int i = 0; i < controls.Length; i++) controls[i].Latched = i == selected;
            }
        }

        /// <summary>
        /// A toggle cell that carries a leading platform/unit sprite (a NATO/vehicle-class icon — data, not
        /// chrome, so it stays a plain <see cref="Image"/> rather than an <see cref="AvIcon"/>). Otherwise the
        /// same visual language as kit v2's <see cref="AvCell"/> (outline + LED + faint select wash + mono
        /// ON/OFF word): AvCell itself has no icon slot, so this is a local fork built from the same kit
        /// primitives (AvFrame, AvText, AvLay, AvHit, AvFx) per the brief's "build it locally" allowance.
        /// Title and sub are mutable after construction so a bounded cell pool (<see cref="MfdPagingGrid"/>)
        /// can rebind the same instances to different rows as the page changes.
        /// </summary>
        private sealed class MfdIconCell : AvPart
        {
            private const float PadX = 10f, PadY = 7f, Led = 6f, StateW = 34f, IconSize = 20f, IconGap = 6f, TileIcon = 22f;
            private AvFrame frame;
            private Image led, icon;
            private TMP_Text title, sub, stateWord;
            private Func<bool> get;
            private Action<bool> set;
            private string onWord, offWord;
            private bool on, hover, interactable = true, hasIcon, tile;
            private TMP_Text glyph;
            private AvFx fx;

            public Action OnRightClick;

            /// <summary>Hover help shown in the console footer (null/empty clears it).</summary>
            public string Help { set => AvHelpTip.Attach(frame.gameObject, value); }

            /// <summary>
            /// <paramref name="tile"/> gives the icon-first compact cell: LED, big glyph (or sprite) and the state word
            /// on one line, a short single-line title under them. The second line and its detail live in the tip.
            /// </summary>
            public static MfdIconCell Toggle(RectTransform parent, Func<bool> get, Action<bool> set,
                string onWord = "ON", string offWord = "OFF", bool tile = false)
            {
                var c = new MfdIconCell { get = get, set = set, onWord = onWord, offWord = offWord, tile = tile };
                c.Rect = AvLay.Child(parent, "Cell");
                c.frame = AvFrame.Add(c.Rect, "Frame", AvChamfer.Diagonal(5f)); AvLay.Fill(c.frame.rectTransform);
                c.led = AvLay.Solid(c.Rect, "Led", Color.clear);
                c.icon = AvLay.Solid(c.Rect, "Icon", Color.clear);
                c.icon.preserveAspect = true;
                c.icon.enabled = false;
                c.title = AvText.Make(c.Rect, "Title", AvTextRole.Label, "", TextAlignmentOptions.TopLeft, true);
                c.sub = AvText.Make(c.Rect, "Sub", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                c.stateWord = AvText.Make(c.Rect, "State", AvTextRole.DataSmall, "", TextAlignmentOptions.TopRight);
                if (tile)
                {
                    c.glyph = AvIcons.Make(c.Rect, AvIcon.None, TileIcon, Color.white);
                    AvText.Fit(c.title, true);
                }
                c.fx = AvFx.On(c.frame);
                AvHit hit = AvHit.On(c.frame);
                hit.Hover = h => { c.hover = h; c.Restyle(); };
                hit.Click = e =>
                {
                    if (e.button == PointerEventData.InputButton.Right) { c.OnRightClick?.Invoke(); c.Refresh(); return; }
                    if (e.button != PointerEventData.InputButton.Left || c.set == null) return;
                    c.set(!c.on); c.Refresh();
                };
                c.Refresh();
                return c;
            }

            public bool Interactable
            {
                get => interactable;
                set { interactable = value; frame.GetComponent<AvHit>().Interactable = value; Restyle(); }
            }

            public void SetTitle(string titleText, string subText)
            {
                title.text = titleText ?? "";
                sub.text = subText ?? "";
            }

            /// <summary>Tile cells only: the chrome glyph shown when the item has no sprite of its own.</summary>
            public void SetGlyph(AvIcon icon)
            {
                if (glyph == null) return;
                if (icon == AvIcon.None) { glyph.text = ""; return; }
                AvIcons.Set(glyph, icon, TileIcon);
            }

            public void SetIcon(Sprite sprite)
            {
                hasIcon = sprite != null;
                icon.sprite = sprite;
                icon.enabled = hasIcon;
            }

            public void Refresh()
            {
                bool now = get != null && get();
                if (now != on) { on = now; if (on) fx.Play(AvFxKind.Shine, 0.4f); }
                stateWord.text = on ? onWord : offWord;
                Restyle();
            }

            public override float Measure(float width)
            {
                if (tile) return 56f;
                float iconW = hasIcon ? IconSize + IconGap : 0f;
                float textW = width - 2f * PadX - Led - 6f - iconW - StateW;
                float h = PadY + AvText.Height(title, textW) + (sub.text.Length > 0 ? 2f + AvText.Height(sub, textW) : 0f) + PadY;
                return Mathf.Max(AvGridTokens.ToolCell, h);
            }

            public override void Place(AvSlot s)
            {
                base.Place(s);
                if (tile) { PlaceTile(s); return; }
                float iconW = hasIcon ? IconSize + IconGap : 0f;
                float x = PadX + Led + 6f + iconW, textW = s.W - x - PadX - StateW;
                float th = AvText.Height(title, textW);
                bool hasSub = sub.text.Length > 0;
                float sh = hasSub ? AvText.Height(sub, textW) : 0f;
                // A cell with a sub-line keeps the MAP layout (text block pinned to the top pad); a title-only
                // cell centres its single line instead of leaving the bottom half of the frame empty.
                float y0 = hasSub ? PadY : Mathf.Max(PadY - 2f, (s.H - th) * 0.5f);
                AvLay.Place(led.rectTransform, PadX, y0 + 4f, Led, Led);
                if (hasIcon) AvLay.Place(icon.rectTransform, PadX + Led + 6f, (s.H - IconSize) * 0.5f, IconSize, IconSize);
                AvLay.Place(title.rectTransform, x, y0, textW, th);
                AvLay.Place(sub.rectTransform, x, y0 + th + 2f, textW, sh);
                AvLay.Place(stateWord.rectTransform, s.W - PadX - StateW, y0, StateW, 16f);
            }

            private void PlaceTile(AvSlot s)
            {
                // Top line: LED, glyph or sprite, state word. Bottom line: the short title, full width.
                AvLay.Place(led.rectTransform, PadX, 12f, Led, Led);
                float gx = PadX + Led + 8f;
                if (hasIcon) AvLay.Place(icon.rectTransform, gx, 6f, TileIcon, TileIcon);
                else AvLay.Place(glyph.rectTransform, gx, 6f, TileIcon, TileIcon);
                glyph.gameObject.SetActive(!hasIcon);
                AvLay.Place(stateWord.rectTransform, s.W - PadX - StateW, 8f, StateW, 16f);
                sub.gameObject.SetActive(false);
                float tw = s.W - 2f * PadX, th = Mathf.Min(AvText.Height(title, tw), Mathf.Max(16f, s.H - 34f));
                AvLay.Place(title.rectTransform, PadX, 32f, tw, th);
            }

            public override void Restyle()
            {
                string st = !interactable ? "disabled" : on ? "on" : hover ? "hover" : null;
                AvStyle c = AvStyleHost.FuiStyle("cell", st);
                frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
                title.color = AvStyleHost.Resolve(c.Color, AvTheme.TextPrimary);
                sub.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("cell-sub").Color, AvTheme.Dim);
                stateWord.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("cell-state", on ? "on" : null).Color, AvTheme.Disabled);
                led.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("cell-led", on ? "on" : null).Background, AvTheme.RailInert);
                icon.color = interactable ? Color.white : AvTheme.Disabled;
                if (glyph != null) glyph.color = !interactable ? AvTheme.Disabled : on ? AvStyleHost.Resolve(c.Color, AvTheme.TextPrimary) : AvTheme.Dim;
            }
        }

        // Chrome glyphs for the stock target and HUD pages. The live label wins; the index is the
        // game's fixed order, which is all the atlas has when the native selector is absent.
        private static readonly string[] VehicleFallback =
            { "TRUCK", "UGV", "LCV", "AFV", "MBT", "ART", "AAA", "IR SAM", "R SAM", "RADAR" };

        private static readonly string[] BuildingFallback =
            { "CIVILIAN", "INDUSTRY", "EW RADAR", "DEPOT", "HANGAR", "FORTIFY", "MUNITIONS" };

        private static AvIcon VehicleGlyph(string label, int index)
        {
            if (string.IsNullOrEmpty(label) && index >= 0 && index < VehicleFallback.Length) label = VehicleFallback[index];
            switch (label)
            {
                case "TRUCK": return AvIcon.Stack2;
                case "UGV": return AvIcon.CurrentLocation;
                case "LCV": return AvIcon.Eye;
                case "AFV": return AvIcon.Shield;
                case "MBT": return AvIcon.ShieldLock;
                case "ART": return AvIcon.Target;
                case "AAA": return AvIcon.Bolt;
                case "IR SAM": return AvIcon.Flame;
                case "R SAM": return AvIcon.Antenna;
                case "RDR":
                case "RADAR": return AvIcon.Radar2;
                default: return AvIcon.Circle;
            }
        }

        private static AvIcon BuildingGlyph(string label, int index)
        {
            if (string.IsNullOrEmpty(label) && index >= 0 && index < BuildingFallback.Length) label = BuildingFallback[index];
            switch (label)
            {
                case "CIVILIAN": return AvIcon.User;
                case "INDUSTRY": return AvIcon.BuildingBank;
                case "EW RADAR":
                case "RDR": return AvIcon.Radar2;
                case "DEPOT": return AvIcon.Database;
                case "HANGAR": return AvIcon.Plane;
                case "FORTIFY": return AvIcon.Shield;
                case "MUNITIONS":
                case "AMMO": return AvIcon.Flame;
                default: return AvIcon.BuildingBank;
            }
        }
    }
}
