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
        private readonly bool trailingGap;
        private readonly T[] rows;   // the pooled parts (the offline harness reads this field by name)
        private readonly Action<int, T> bind;
        private readonly AvControl prev, next;
        private readonly TMP_Text range;
        private int count, rangeFirst = -1, rangeCount = -1;
        private float arrangedWidth = -1f;

        /// <param name="gap">Space between rows.</param>
        /// <param name="factory">Builds pool slot <c>i</c> under the stack's rect.</param>
        /// <param name="pagerLead">Extra space between the last item and the pager line.</param>
        /// <param name="startHidden">Hide every pool slot until the first <see cref="SetCount"/> binds the page.</param>
        /// <param name="trailingGap">Keep space after the final row even when there is no pager.</param>
        public AvPagedStack(RectTransform parent, AvTicker ticker, int pageSize, float gap,
            Func<RectTransform, int, T> factory, Action<int, T> binder,
            string name = "PagedStack", int maxPageSize = 12, float pagerLead = 0f, bool startHidden = true,
            bool trailingGap = false)
        {
            Rect = AvLay.Child(parent, name);
            this.gap = gap;
            this.pagerLead = pagerLead;
            this.trailingGap = trailingGap;
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
            range = AvText.Make(Rect, "Range", AvTextRole.DataSmall, "", TextAlignmentOptions.Center, true);
        }

        public int Page { get; private set; }
        public int PageSize => rows.Length;
        private int Pages => Mathf.Max(1, (count + rows.Length - 1) / rows.Length);
        private bool Paged => Pages > 1;

        /// <summary>Jump to the page holding <paramref name="item"/>.</summary>
        public void Reveal(int item) => Go(item / rows.Length);

        public void SetCount(int n) { count = Mathf.Max(0, n); Go(Page); }

        private void Go(int page)
        {
            Page = Mathf.Clamp(page, 0, Pages - 1);
            int first = Page * rows.Length;
            bool changedPage = first != rangeFirst || count != rangeCount;
            for (int i = 0; i < rows.Length; i++)
            {
                int item = first + i;
                bool shown = item < count;
                rows[i].Rect.gameObject.SetActive(shown);
                if (shown) bind?.Invoke(item, rows[i]);
            }
            if (changedPage)
            {
                rangeFirst = first; rangeCount = count;
                range.text = count == 0 ? "0 OF 0" : (first + 1) + "–" + Mathf.Min(count, first + rows.Length) + " OF " + count;
            }
            prev.Interactable = Page > 0;
            next.Interactable = Page < Pages - 1;
            prev.gameObject.SetActive(Paged); next.gameObject.SetActive(Paged); range.gameObject.SetActive(Paged);
            // Two rows can exchange heights while the stack keeps exactly the same total height.
            // The outer flow only sees that total, so position the rebound rows here as well.
            if (arrangedWidth > 0f) ArrangeRows(arrangedWidth, changedPage);
            Changed();
        }

        public override float Measure(float width)
        {
            float h = 0f;
            foreach (T item in rows) if (item.Rect.gameObject.activeSelf) h += item.Measure(width) + gap;
            if (Paged) return h + pagerLead + PagerHeight(width, 0f, false);
            return !trailingGap && h > 0f ? h - gap : h;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            arrangedWidth = s.W;
            ArrangeRows(s.W, true);
        }

        private void ArrangeRows(float width, bool force)
        {
            float y = 0f;
            foreach (T item in rows)
            {
                if (!item.Rect.gameObject.activeSelf) continue;
                float h = item.Measure(width);
                if (force || Mathf.Abs(item.Rect.anchoredPosition.y + y) > .1f || Mathf.Abs(item.Rect.rect.height - h) > .1f)
                    item.Place(new AvSlot(0f, y, width, h));
                y += h + gap;
            }
            prev.gameObject.SetActive(Paged); next.gameObject.SetActive(Paged); range.gameObject.SetActive(Paged);
            if (Paged) PagerHeight(width, y + pagerLead, true);
        }

        // The range stays between the buttons when the three labels fit. Narrow columns put it on its own line.
        private float PagerHeight(float width, float top, bool place)
        {
            const float space = 4f;
            width = Mathf.Max(1f, width);
            float minButton = Mathf.Max(prev.PreferredWidth, next.PreferredWidth);
            float besideRange = (width - AvText.Width(range) - 2f - 2f * space) * .5f;
            bool inline = besideRange >= minButton;
            bool stackButtons = !inline && width < 2f * minButton + space;
            float buttonW = inline ? Mathf.Min(Mathf.Max(minButton, 96f), besideRange) :
                stackButtons ? width : (width - space) * .5f;
            float buttonH = Mathf.Max(AvGridTokens.Row, Mathf.Max(prev.PreferredHeight(buttonW), next.PreferredHeight(buttonW)));
            float rangeW = inline ? width - 2f * (buttonW + space) : width;
            float rangeH = AvText.Height(range, rangeW);
            float rowH = inline ? Mathf.Max(buttonH, rangeH) : buttonH;
            float buttonsY = inline ? top : top + rangeH + space;
            if (place)
            {
                AvLay.Place(range.rectTransform, inline ? buttonW + space : 0f, top, rangeW, inline ? rowH : rangeH);
                AvLay.Place(prev.Rect, 0f, buttonsY, buttonW, rowH);
                AvLay.Place(next.Rect, width - buttonW, stackButtons ? buttonsY + rowH + space : buttonsY, buttonW, rowH);
            }
            return (buttonsY - top) + rowH + (stackButtons ? space + rowH : 0f);
        }

        public override void Restyle()
        {
            foreach (T item in rows) item.Restyle();
            prev.Restyle(); next.Restyle();
            range.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
        }
    }
}
