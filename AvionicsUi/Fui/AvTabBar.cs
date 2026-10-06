using System;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>
    /// Icon + label tabs; the latched one carries a 2 px select rail and a shine. Labels shrink, never wrap
    /// (a 48 px tab used to break "DISPLAY" into "DISPL/AY"); more than <see cref="MaxPerRow"/> tabs take two rows.
    /// </summary>
    public sealed class AvTabBar : AvPart
    {
        public const int MaxPerRow = 6;
        private readonly AvControl[] tabs;
        private readonly Action<int> onSelect;

        public AvTabBar(RectTransform parent, (AvIcon icon, string label)[] items, Action<int> select)
        {
            Rect = AvLay.Child(parent, "Tabs");
            onSelect = select;
            tabs = new AvControl[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                int index = i;
                tabs[i] = AvControl.Make(Rect, new AvControl.Spec(items[i].label, () => Select(index), AvButtonStyle.Default, items[i].icon), "tab");
                tabs[i].SingleLine();
            }
            Selected = -1;
        }

        public int Selected { get; private set; }

        public void Select(int index, bool notify = true)
        {
            if (index < 0 || index >= tabs.Length) return;
            Selected = index;
            for (int i = 0; i < tabs.Length; i++) tabs[i].Latched = i == index;
            if (notify) onSelect?.Invoke(index);
        }

        private int Rows => tabs.Length > MaxPerRow ? 2 : 1;

        public override float Measure(float width) => Rows * AvGridTokens.Tab + (Rows - 1) * 2f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            int perRow = (tabs.Length + Rows - 1) / Rows;
            for (int i = 0; i < tabs.Length; i++)
            {
                int row = i / perRow, inRow = row == Rows - 1 ? tabs.Length - row * perRow : perRow;
                float w = AvFlowMath.ColumnWidth(s.W, inRow, 2f);
                AvLay.Place(tabs[i].Rect, (i - row * perRow) * (w + 2f), row * (AvGridTokens.Tab + 2f), w, AvGridTokens.Tab);
            }
        }

        public override void Restyle() { foreach (AvControl t in tabs) t.Restyle(); }
    }
}
