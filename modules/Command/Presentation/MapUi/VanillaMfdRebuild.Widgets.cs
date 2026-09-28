using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
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
            private const float PadX = 10f, PadY = 7f, Led = 6f, StateW = 34f, IconSize = 16f, IconGap = 6f;
            private AvFrame frame;
            private Image led, icon;
            private TMP_Text title, sub, stateWord;
            private Func<bool> get;
            private Action<bool> set;
            private string onWord, offWord;
            private bool on, hover, interactable = true, hasIcon;
            private AvFx fx;

            public Action OnRightClick;

            public static MfdIconCell Toggle(RectTransform parent, Func<bool> get, Action<bool> set,
                string onWord = "ON", string offWord = "OFF")
            {
                var c = new MfdIconCell { get = get, set = set, onWord = onWord, offWord = offWord };
                c.Rect = AvLay.Child(parent, "Cell");
                c.frame = AvFrame.Add(c.Rect, "Frame", AvChamfer.Diagonal(5f)); AvLay.Fill(c.frame.rectTransform);
                c.led = AvLay.Solid(c.Rect, "Led", Color.clear);
                c.icon = AvLay.Solid(c.Rect, "Icon", Color.clear);
                c.icon.preserveAspect = true;
                c.icon.enabled = false;
                c.title = AvText.Make(c.Rect, "Title", AvTextRole.Label, "", TextAlignmentOptions.TopLeft, true);
                c.sub = AvText.Make(c.Rect, "Sub", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
                c.stateWord = AvText.Make(c.Rect, "State", AvTextRole.DataSmall, "", TextAlignmentOptions.TopRight);
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
                float iconW = hasIcon ? IconSize + IconGap : 0f;
                float textW = width - 2f * PadX - Led - 6f - iconW - StateW;
                float h = PadY + AvText.Height(title, textW) + (sub.text.Length > 0 ? 2f + AvText.Height(sub, textW) : 0f) + PadY;
                return Mathf.Max(AvGridTokens.ToolCell, h);
            }

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float iconW = hasIcon ? IconSize + IconGap : 0f;
                float x = PadX + Led + 6f + iconW, textW = s.W - x - PadX - StateW;
                float th = AvText.Height(title, textW);
                AvLay.Place(led.rectTransform, PadX, PadY + 4f, Led, Led);
                if (hasIcon) AvLay.Place(icon.rectTransform, PadX + Led + 6f, PadY, IconSize, IconSize);
                AvLay.Place(title.rectTransform, x, PadY, textW, th);
                AvLay.Place(sub.rectTransform, x, PadY + th + 2f, textW, AvText.Height(sub, textW));
                AvLay.Place(stateWord.rectTransform, s.W - PadX - StateW, PadY, StateW, 16f);
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
            }
        }
    }
}
