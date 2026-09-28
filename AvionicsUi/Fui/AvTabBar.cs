using System;
using NOAvionics;
using UnityEngine;

namespace NOAvionics.Ui
{
    /// <summary>Icon + label tabs; the latched one carries a 2 px select rail and a shine.</summary>
    public sealed class AvTabBar : AvPart
    {
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

        public override float Measure(float width) => AvGridTokens.Tab;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = AvFlowMath.ColumnWidth(s.W, tabs.Length, 2f);
            for (int i = 0; i < tabs.Length; i++) AvLay.Place(tabs[i].Rect, i * (w + 2f), 0f, w, s.H);
        }

        public override void Restyle() { foreach (AvControl t in tabs) t.Restyle(); }
    }
}
