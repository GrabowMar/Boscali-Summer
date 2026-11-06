using System;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>
    /// Icon + label tabs; the latched one carries a 2 px select rail and a shine. Labels shrink, never wrap
    /// (a 48 px tab used to break "DISPLAY" into "DISPL/AY"). Rows adapt to the labels and available width.
    /// </summary>
    public sealed class AvTabBar : AvPart
    {
        public const int MaxPerRow = 6;
        private readonly AvControl[] tabs;
        private readonly bool[] hidden;
        private readonly Action<int> onSelect;
        private AvSlot slot;
        private bool placed;
        // Where each tab sits (x, y, width) after the last Place, for the select rail's glide.
        private readonly Vector3[] spots;
        private AvTabGlide glide;
        internal event Action HeightChanged;

        public AvTabBar(RectTransform parent, (AvIcon icon, string label)[] items, Action<int> select)
        {
            Rect = AvLay.Child(parent, "Tabs");
            onSelect = select;
            tabs = new AvControl[items.Length];
            hidden = new bool[items.Length];
            spots = new Vector3[items.Length];
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
            int from = Selected;
            Selected = index;
            for (int i = 0; i < tabs.Length; i++) tabs[i].Latched = i == index;
            if (placed && from >= 0 && from != index && !hidden[from]) Glide(from, index);
            if (notify) onSelect?.Invoke(index);
        }

        /// <summary>
        /// Show or hide one tab; a hidden tab takes no width and the rest re-spread over the slot the bar has.
        /// The console reclaims the body space when the number of rows changes.
        /// </summary>
        public void SetShown(int index, bool shown)
        {
            if (index < 0 || index >= tabs.Length || hidden[index] == !shown) return;
            int before = placed ? Rows(slot.W) : 0;
            hidden[index] = !shown;
            tabs[index].gameObject.SetActive(shown);
            if (placed) Place(slot);
            if (placed && before != Rows(slot.W)) HeightChanged?.Invoke();
        }

        private int Visible
        {
            get { int n = 0; foreach (bool h in hidden) if (!h) n++; return n; }
        }

        private int Rows(float width)
        {
            int count = Visible;
            for (int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)MaxPerRow)); rows < count; rows++)
            {
                int perRow = (count + rows - 1) / rows, v = 0;
                if ((count + perRow - 1) / perRow != rows) continue;
                float used = 0f;
                bool fits = true;
                for (int i = 0; i < tabs.Length; i++)
                {
                    if (hidden[i]) continue;
                    if (v % perRow == 0) used = 0f;
                    else used += 2f;
                    used += Mathf.Max(48f, tabs[i].PreferredWidth);
                    if (used > width) { fits = false; break; }
                    v++;
                }
                if (fits) return rows;
            }
            return Mathf.Max(1, count);
        }

        public override float Measure(float width)
        {
            int rows = Rows(width);
            return rows * AvGridTokens.Tab + (rows - 1) * 2f;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            slot = s;
            placed = true;
            int count = Visible;
            if (count == 0) return;
            int rows = Rows(s.W), perRow = (count + rows - 1) / rows;
            for (int row = 0, v = 0; row < rows; row++)
            {
                int inRow = Mathf.Min(perRow, count - row * perRow);
                float total = 0f, x = 0f, avail = Mathf.Max(0f, s.W - (inRow - 1) * 2f);
                int start = v;
                for (int n = 0; n < inRow; v++) if (!hidden[v]) { total += Mathf.Max(48f, tabs[v].PreferredWidth); n++; }
                int end = v;
                for (int i = start; i < end; i++)
                {
                    if (hidden[i]) continue;
                    float w = i == end - 1 ? Mathf.Max(0f, s.W - x) : Mathf.Floor(avail * Mathf.Max(48f, tabs[i].PreferredWidth) / total);
                    AvLay.Place(tabs[i].Rect, x, row * (AvGridTokens.Tab + 2f), w, AvGridTokens.Tab);
                    spots[i] = new Vector3(x, row * (AvGridTokens.Tab + 2f), w);
                    x += w + 2f;
                }
            }
        }

        /// <summary>
        /// The select rail slides from the old tab to the new one (2026-11-06): the leading edge runs ahead, the trailing edge
        /// follows, so it stretches and settles like a pulled band. The new tab's own rail is already lit under it; the glide only
        /// carries the eye there. Skipped under reduced motion.
        /// </summary>
        private void Glide(int from, int to)
        {
            if (AvFxDriver.ReducedMotion) return;
            if (glide == null)
            {
                UnityEngine.UI.Image bar = AvLay.Solid(Rect, "Glide", Color.clear);
                bar.raycastTarget = false;
                glide = bar.gameObject.AddComponent<AvTabGlide>();
                glide.Bar = bar;
            }
            Transform rail = tabs[to].Rect.Find("Rail");
            var ink = rail != null ? rail.GetComponent<UnityEngine.UI.Image>() : null;
            glide.transform.SetAsLastSibling();
            glide.Run(spots[from], spots[to], ink != null ? ink.color : Color.white);
        }

        /// <summary>Every tab takes a console's own accent (see <see cref="AvControl.Accent"/>); null returns them to the scheme.</summary>
        public void SetAccent(Color? accent) { foreach (AvControl t in tabs) t.Accent = accent; }

        public override void Restyle() { foreach (AvControl t in tabs) t.Restyle(); }
    }

    /// <summary>Per-frame driver of the tab bar's select-rail glide (unscaled time; about a fifth of a second).</summary>
    internal sealed class AvTabGlide : MonoBehaviour
    {
        private const float Seconds = .22f;
        internal UnityEngine.UI.Image Bar;
        private Vector3 from, to;
        private float t = 1f;

        internal void Run(Vector3 a, Vector3 b, Color ink)
        {
            from = a; to = b; t = 0f;
            Bar.color = ink;
            Bar.enabled = true;
            enabled = true;
            Step();
        }

        private void Update()
        {
            t += Time.unscaledDeltaTime / Seconds;
            Step();
            if (t >= 1f) { Bar.enabled = false; enabled = false; }
        }

        private void Step()
        {
            float lead = 1f - Mathf.Pow(1f - Mathf.Clamp01(t * 1.5f), 3f), trail = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
            bool right = to.x >= from.x;
            float headFrom = right ? from.x + from.z : from.x, headTo = right ? to.x + to.z : to.x;
            float tailFrom = right ? from.x : from.x + from.z, tailTo = right ? to.x : to.x + to.z;
            float head = Mathf.Lerp(headFrom, headTo, lead), tail = Mathf.Lerp(tailFrom, tailTo, trail);
            float y = Mathf.Lerp(from.y, to.y, lead) + AvGridTokens.Tab - 2f;
            AvLay.Place((RectTransform)Bar.transform, Mathf.Min(head, tail), y, Mathf.Abs(head - tail), 2f);
        }
    }
}
