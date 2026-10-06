using System;
using TMPro;
using UnityEngine;

namespace NOAvionics
{
    /// <summary>
    /// A paged stack of pooled parts with the kit pager line ("PREV  1–5 OF 23  NEXT"). The pool is fixed at the page
    /// size and rebound as the page or the item count changes; <see cref="AvList"/> is the pooled-row case.
    /// </summary>
    public class AvPagedStack<T> : AvPart where T : AvPart
    {
        private readonly float gap, pagerLead;
        private readonly T[] rows;   // the pooled parts (the offline harness reads this field by name)
        private readonly Action<int, T> bind;
        private readonly AvControl prev, next;
        private readonly TMP_Text range;
        private int count;

        /// <param name="gap">Space between rows.</param>
        /// <param name="factory">Builds pool slot <c>i</c> under the stack's rect.</param>
        /// <param name="pagerLead">Extra space between the last item and the pager line.</param>
        /// <param name="startHidden">Hide every pool slot until the first <see cref="SetCount"/> binds the page.</param>
        public AvPagedStack(RectTransform parent, AvTicker ticker, int pageSize, float gap,
            Func<RectTransform, int, T> factory, Action<int, T> binder,
            string name = "PagedStack", int maxPageSize = 12, float pagerLead = 0f, bool startHidden = true)
        {
            Rect = AvLay.Child(parent, name);
            this.gap = gap;
            this.pagerLead = pagerLead;
            bind = binder;
            rows = new T[Mathf.Clamp(pageSize, 1, maxPageSize)];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = factory(Rect, i);
                rows[i].Parent = this;
                if (startHidden) rows[i].Rect.gameObject.SetActive(false);
                ticker?.Register(rows[i]);
            }
            prev = AvControl.Make(Rect, new AvControl.Spec("PREV", () => Go(Page - 1), AvButtonStyle.Quiet, AvIcon.ChevronLeft));
            next = AvControl.Make(Rect, new AvControl.Spec("NEXT", () => Go(Page + 1), AvButtonStyle.Quiet, AvIcon.ChevronRight, true));
            range = AvText.Make(Rect, "Range", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
        }

        public int Page { get; private set; }
        public int PageSize => rows.Length;
        private int Pages => Mathf.Max(1, (count + rows.Length - 1) / rows.Length);
        private bool Paged => Pages > 1;

        public void SetPage(int page) => Go(page);

        /// <summary>Jump to the page holding <paramref name="item"/>.</summary>
        public void Reveal(int item) => Go(item / rows.Length);

        public void SetCount(int n) { count = Mathf.Max(0, n); Go(Page); }

        private void Go(int page)
        {
            Page = Mathf.Clamp(page, 0, Pages - 1);
            int first = Page * rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                int item = first + i;
                bool shown = item < count;
                rows[i].Rect.gameObject.SetActive(shown);
                if (shown) bind?.Invoke(item, rows[i]);
            }
            range.text = count == 0 ? "0 OF 0" : (first + 1) + "–" + Mathf.Min(count, first + rows.Length) + " OF " + count;
            prev.Interactable = Page > 0;
            next.Interactable = Page < Pages - 1;
            prev.gameObject.SetActive(Paged); next.gameObject.SetActive(Paged); range.gameObject.SetActive(Paged);
            Changed();
        }

        public override float Measure(float width)
        {
            float h = 0f;
            foreach (T item in rows) if (item.Rect.gameObject.activeSelf) h += item.Measure(width) + gap;
            if (h > 0f) h -= gap;
            return h + (Paged ? AvGridTokens.Row + 6f : 0f);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float y = 0f;
            foreach (T item in rows)
            {
                if (!item.Rect.gameObject.activeSelf) continue;
                float h = item.Measure(s.W);
                item.Place(new AvSlot(0f, y, s.W, h));
                y += h + gap;
            }
            prev.gameObject.SetActive(Paged); next.gameObject.SetActive(Paged); range.gameObject.SetActive(Paged);
            if (!Paged) return;
            y += pagerLead;
            AvLay.Place(prev.Rect, 0f, y, 96f, AvGridTokens.Row);
            AvLay.Place(next.Rect, s.W - 96f, y, 96f, AvGridTokens.Row);
            AvLay.Place(range.rectTransform, 100f, y, s.W - 200f, AvGridTokens.Row);
        }

        public override void Restyle()
        {
            foreach (T item in rows) item.Restyle();
            prev.Restyle(); next.Restyle();
            range.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
        }
    }
}
