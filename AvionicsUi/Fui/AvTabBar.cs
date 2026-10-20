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
        private readonly bool[] hidden;
        private readonly Action<int> onSelect;
        private AvSlot slot;
        private bool placed;

        public AvTabBar(RectTransform parent, (AvIcon icon, string label)[] items, Action<int> select)
        {
            Rect = AvLay.Child(parent, "Tabs");
            onSelect = select;
            tabs = new AvControl[items.Length];
            hidden = new bool[items.Length];
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

        /// <summary>
        /// Show or hide one tab; a hidden tab takes no width and the rest re-spread over the slot the bar has.
        /// The owner re-lays the console only if the visible count crosses <see cref="MaxPerRow"/> (the bar's height changes).
        /// </summary>
        public void SetShown(int index, bool shown)
        {
            if (index < 0 || index >= tabs.Length || hidden[index] == !shown) return;
            hidden[index] = !shown;
            tabs[index].gameObject.SetActive(shown);
            if (placed) Place(slot);
        }

        private int Visible
        {
            get { int n = 0; foreach (bool h in hidden) if (!h) n++; return n; }
        }

        private int Rows => Visible > MaxPerRow ? 2 : 1;

        public override float Measure(float width) => Rows * AvGridTokens.Tab + (Rows - 1) * 2f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            slot = s;
            placed = true;
            int count = Visible;
            if (count == 0) return;
            int perRow = (count + Rows - 1) / Rows;
            if (Rows == 1)
            {
                // One row: a tab's share follows its label, so a long word (BEHAVIOUR) is not squeezed onto its icon by short ones.
                const float Base = 6f;
                float total = 0f, x = 0f, avail = s.W - (count - 1) * 2f;
                for (int i = 0; i < tabs.Length; i++) if (!hidden[i]) total += Base + (tabs[i].Label?.Length ?? 0);
                for (int i = 0; i < tabs.Length; i++)
                {
                    if (hidden[i]) continue;
                    float w = Mathf.Floor(avail * (Base + (tabs[i].Label?.Length ?? 0)) / total);
                    AvLay.Place(tabs[i].Rect, x, 0f, w, AvGridTokens.Tab);
                    x += w + 2f;
                }
                return;
            }
            for (int i = 0, v = 0; i < tabs.Length; i++)
            {
                if (hidden[i]) continue;
                int row = v / perRow, inRow = row == Rows - 1 ? count - row * perRow : perRow;
                float w = AvFlowMath.ColumnWidth(s.W, inRow, 2f);
                AvLay.Place(tabs[i].Rect, (v - row * perRow) * (w + 2f), row * (AvGridTokens.Tab + 2f), w, AvGridTokens.Tab);
                v++;
            }
        }

        public override void Restyle() { foreach (AvControl t in tabs) t.Restyle(); }
    }
}
