using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// Toggle cell (replaces the solid-green PagingGrid cell): outline + LED + faint select wash + mono state word.
    /// Title and sub wrap, so long labels grow the cell instead of clipping (R3, spec §2 item 2–3).
    /// </summary>
    public sealed class AvCell : AvPart
    {
        private const float PadX = 10f, PadY = 7f, Led = 6f, StateW = 34f;
        private AvFrame frame;
        private Image led;
        private TMP_Text title, sub, stateWord;
        private Func<bool> get;
        private Action<bool> set;
        private string onWord, offWord;
        private bool on, hover, interactable = true;
        private AvFx fx;

        public Action OnRightClick;

        /// <summary>Hover help shown in the console footer.</summary>
        public string Help { set => AvHelpTip.Attach(frame.gameObject, value); }

        public static AvCell Toggle(RectTransform parent, string titleText, string subText, Func<bool> get, Action<bool> set,
            string onWord = "ON", string offWord = "OFF")
        {
            var c = new AvCell { get = get, set = set, onWord = onWord, offWord = offWord };
            c.Rect = AvLay.Child(parent, "Cell " + titleText);
            c.frame = AvFrame.Add(c.Rect, "Frame", AvChamfer.Diagonal(5f)); AvLay.Fill(c.frame.rectTransform);
            c.led = AvLay.Solid(c.Rect, "Led", Color.clear);
            c.title = AvText.Make(c.Rect, "Title", AvTextRole.Label, titleText, TextAlignmentOptions.TopLeft, true);
            c.sub = AvText.Make(c.Rect, "Sub", AvTextRole.ProseSmall, subText ?? "", TextAlignmentOptions.TopLeft, true);
            c.stateWord = AvText.Make(c.Rect, "State", AvTextRole.DataSmall, "", TextAlignmentOptions.TopRight);
            AvText.Fit(c.stateWord, false); // custom state words shrink into the 34 px box, never spill
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

        public void Refresh()
        {
            bool now = get != null && get();
            if (now != on) { on = now; if (on) fx.Play(AvFxKind.Shine, 0.4f); }
            stateWord.text = on ? onWord : offWord;
            Restyle();
        }

        public override float Measure(float width)
        {
            float textW = width - 2f * PadX - Led - 6f - StateW;
            float h = PadY + AvText.Height(title, textW) + (sub.text.Length > 0 ? 2f + AvText.Height(sub, textW) : 0f) + PadY;
            return Mathf.Max(AvGridTokens.ToolCell, h);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float x = PadX + Led + 6f, textW = s.W - x - PadX - StateW;
            float th = AvText.Height(title, textW);
            AvLay.Place(led.rectTransform, PadX, PadY + 4f, Led, Led);
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
            sub.color = AvStyleHost.FuiInk("cell-sub", AvTheme.Dim);
            stateWord.color = AvStyleHost.FuiInk("cell-state", AvTheme.Disabled, on ? "on" : null);
            led.color = AvStyleHost.FuiFill("cell-led", AvTheme.RailInert, on ? "on" : null);
        }
    }
}
