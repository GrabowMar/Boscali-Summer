using System;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace NOAvionics.Ui
{
    /// <summary>Paged, pooled row list with a pager line ("PREV  1–5 OF 23  NEXT"). Pool ceiling 64 rows.</summary>
    public sealed class AvList : AvPart
    {
        public const int MaxPageSize = 64;
        private readonly AvRow[] rows;
        private readonly Action<int, AvRow> bind;
        private readonly AvControl prev, next;
        private readonly TMP_Text range;
        private int count;

        public AvList(RectTransform parent, AvTicker ticker, int pageSize, Action<int, AvRow> binder)
        {
            Rect = AvLay.Child(parent, "List");
            bind = binder;
            rows = new AvRow[Mathf.Clamp(pageSize, 1, MaxPageSize)];
            for (int i = 0; i < rows.Length; i++)
            {
                int slot = i;
                rows[i] = new AvRow(Rect, () => RowClicked?.Invoke(Page * rows.Length + slot));
                if (ticker != null) ticker.Register(rows[i]);
            }
            prev = AvControl.Make(Rect, new AvControl.Spec("PREV", () => Go(Page - 1), AvButtonStyle.Quiet, AvIcon.ChevronLeft));
            next = AvControl.Make(Rect, new AvControl.Spec("NEXT", () => Go(Page + 1), AvButtonStyle.Quiet, AvIcon.ChevronRight));
            range = AvText.Make(Rect, "Range", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
        }

        public int Page { get; private set; }

        /// <summary>Item index of a clicked row (rows are clickable only when this is set).</summary>
        public System.Action<int> RowClicked;

        /// <summary>Jump to the page holding <paramref name="item"/>.</summary>
        public void Reveal(int item) => Go(item / rows.Length);
        private int Pages => Math.Max(1, (count + rows.Length - 1) / rows.Length);

        public void SetCount(int n) { count = Math.Max(0, n); Go(Page); }

        private void Go(int page)
        {
            Page = Mathf.Clamp(page, 0, Pages - 1);
            int first = Page * rows.Length;
            for (int i = 0; i < rows.Length; i++)
            {
                int item = first + i;
                bool shown = item < count;
                rows[i].Rect.gameObject.SetActive(shown);
                if (shown) { bind?.Invoke(item, rows[i]); rows[i].Restyle(); }
            }
            range.text = count == 0 ? "0 OF 0" : (first + 1) + "\u2013" + Math.Min(count, first + rows.Length) + " OF " + count;
            prev.Interactable = Page > 0; next.Interactable = Page < Pages - 1;
        }

        public override float Measure(float width)
        {
            float h = 0f;
            foreach (AvRow r in rows) if (r.Rect.gameObject.activeSelf) h += r.Measure(width) + 2f;
            return h + (Pages > 1 ? AvGridTokens.Row + 4f : 0f);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float y = 0f;
            foreach (AvRow r in rows)
            {
                if (!r.Rect.gameObject.activeSelf) continue;
                float h = r.Measure(s.W);
                r.Place(new AvSlot(0f, y, s.W, h));
                y += h + 2f;
            }
            bool paged = Pages > 1;
            prev.gameObject.SetActive(paged); next.gameObject.SetActive(paged); range.gameObject.SetActive(paged);
            if (!paged) return;
            y += 2f;
            AvLay.Place(prev.Rect, 0f, y, 96f, AvGridTokens.Row);
            AvLay.Place(next.Rect, s.W - 96f, y, 96f, AvGridTokens.Row);
            AvLay.Place(range.rectTransform, 100f, y, s.W - 200f, AvGridTokens.Row);
        }
    }
}
