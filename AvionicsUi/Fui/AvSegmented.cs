using System;
using NOAvionics;
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
        }

        public void Refresh() { int sel = get?.Invoke() ?? -1; for (int i = 0; i < options.Length; i++) options[i].Latched = i == sel; }

        /// <summary>Label-less, full-width mode selector (A/G · A/A · SEAD): a panel shows only what its mode needs.</summary>
        public static AvSegmented Strip(RectTransform parent, string[] choices, Func<int> getter, Action<int> set) =>
            new AvSegmented(parent, "", choices, getter, set);

        private float GroupWidth(float width) => label.text.Length == 0 ? width : Mathf.Min(width * 0.62f, options.Length * 86f);

        public override float Measure(float width) => Mathf.Max(AvGridTokens.Row, AvText.Height(label, width - GroupWidth(width) - 8f) + 8f);

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float gw = GroupWidth(s.W), ow = AvFlowMath.ColumnWidth(gw, options.Length, 2f), y = (s.H - 26f) * 0.5f;
            AvLay.Place(label.rectTransform, 0f, 0f, s.W - gw - 8f, s.H);
            for (int i = 0; i < options.Length; i++) AvLay.Place(options[i].Rect, s.W - gw + i * (ow + 2f), y, ow, 26f);
        }

        public override void Restyle()
        {
            label.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            foreach (AvControl o in options) o.Restyle();
        }
    }
}
