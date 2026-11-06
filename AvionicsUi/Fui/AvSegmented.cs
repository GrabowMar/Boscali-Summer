using System;
using TMPro;
using UnityEngine;

namespace NOAvionics
{
    public sealed class AvSegmented : AvPart
    {
        private readonly TMP_Text label;
        private readonly AvControl[] options;
        public AvControl[] Options => options;
        private readonly Func<int> get;

        public AvSegmented(RectTransform parent, string labelText, string[] choices, Func<int> getter, Action<int> set)
        {
            Rect = AvLay.Child(parent, "Segmented " + labelText);
            get = getter;
            label = AvText.Make(Rect, "Label", AvTextRole.Label, labelText, TextAlignmentOptions.MidlineLeft, true);
            options = new AvControl[choices.Length];
            for (int i = 0; i < choices.Length; i++)
            {
                int index = i;
                options[i] = AvControl.Make(Rect, new AvControl.Spec(choices[i], () => { set?.Invoke(index); Refresh(); }, AvButtonStyle.Default));
            }
            Refresh();
            Restyle();
        }

        public void Refresh() { int sel = get?.Invoke() ?? -1; for (int i = 0; i < options.Length; i++) options[i].Latched = i == sel; }

        /// <summary>Label-less, full-width mode selector (A/G · A/A · SEAD): a panel shows only what its mode needs.</summary>
        public static AvSegmented Strip(RectTransform parent, string[] choices, Func<int> getter, Action<int> set) =>
            new AvSegmented(parent, "", choices, getter, set);

        private bool Stacked(float width)
        {
            float widest = 0f;
            foreach (AvControl option in options) widest = Mathf.Max(widest, option.PreferredWidth);
            return label.text.Length > 0 && AvText.Width(label) + 8f + options.Length * widest + Mathf.Max(0, options.Length - 1) * 2f > width;
        }

        private float GroupWidth(float width) => label.text.Length == 0 || Stacked(width) ? width : Mathf.Min(width * 0.62f, options.Length * 86f);

        private float ControlsHeight(float width)
        {
            float column = AvFlowMath.ColumnWidth(width, options.Length, 2f), height = AvGridTokens.Row;
            foreach (AvControl option in options) height = Mathf.Max(height, option.PreferredHeight(column));
            return height;
        }

        public override float Measure(float width)
        {
            float group = GroupWidth(width), controls = ControlsHeight(group);
            if (Stacked(width)) return AvText.Height(label, width) + 4f + controls;
            return Mathf.Max(controls, label.text.Length == 0 ? 0f : AvText.Height(label, width - group - 8f) + 8f);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            bool stacked = Stacked(s.W);
            float gw = GroupWidth(s.W), ow = AvFlowMath.ColumnWidth(gw, options.Length, 2f), height = ControlsHeight(gw);
            float labelH = stacked ? AvText.Height(label, s.W) : s.H, y = stacked ? labelH + 4f : (s.H - height) * 0.5f;
            label.gameObject.SetActive(label.text.Length > 0);
            AvLay.Place(label.rectTransform, 0f, 0f, stacked ? s.W : Mathf.Max(0f, s.W - gw - 8f), labelH);
            for (int i = 0; i < options.Length; i++) AvLay.Place(options[i].Rect, s.W - gw + i * (ow + 2f), y, ow, height);
        }

        public override void Restyle()
        {
            label.color = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
            foreach (AvControl o in options) o.Restyle();
        }
    }
}
