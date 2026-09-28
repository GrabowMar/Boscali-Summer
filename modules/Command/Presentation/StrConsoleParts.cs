using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>
    /// Small kit v2 building blocks shared by the STR console and its two floating windows.
    /// The kit's own <see cref="AvList"/> pools rows but does not wire a per-row click (its rows
    /// only ever display), and it has no plain wrapped-paragraph part; these three fill that gap
    /// locally, built from <see cref="AvPart"/>/<see cref="AvText"/>/<see cref="AvLay"/> primitives
    /// exactly as the kit's own parts are (spec: "build it locally in your module from kit
    /// primitives" when the kit lacks something). Kit gap, reported alongside the slice.
    /// </summary>
    internal sealed class ProseText : AvPart
    {
        private readonly TMP_Text text;

        public ProseText(RectTransform parent, AvTextRole role = AvTextRole.ProseSmall)
        {
            Rect = AvLay.Child(parent, "Prose");
            text = AvText.Make(Rect, "Text", role, "", TextAlignmentOptions.TopLeft, true);
            AvLay.Fill(text.rectTransform);
            Restyle();
        }

        public void Set(string value)
        {
            string v = value ?? "";
            if (text.text != v) text.text = v;
        }

        public override float Measure(float width) => text.text.Length == 0 ? 0f : Mathf.Max(14f, AvText.Height(text, width));

        public override void Restyle() =>
            text.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
    }

    /// <summary>A fixed pool of clickable rows with no paging (roster/log boards bounded well under a page).</summary>
    internal sealed class AvRowStack : AvPart
    {
        private readonly AvRow[] rows;

        public AvRowStack(RectTransform parent, int count, Action<int> onClick)
        {
            Rect = AvLay.Child(parent, "RowStack");
            rows = new AvRow[count];
            for (int i = 0; i < count; i++)
            {
                int slot = i;
                rows[i] = new AvRow(Rect, () => onClick?.Invoke(slot));
                rows[i].Rect.gameObject.SetActive(false);
            }
        }

        public int Count => rows.Length;
        public AvRow Row(int i) => rows[i];
        public void Hide(int i) => rows[i].Rect.gameObject.SetActive(false);
        public void Show(int i) => rows[i].Rect.gameObject.SetActive(true);

        public override float Measure(float width)
        {
            float h = 0f;
            for (int i = 0; i < rows.Length; i++) if (rows[i].Rect.gameObject.activeSelf) h += rows[i].Measure(width) + 2f;
            return h;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float y = 0f;
            for (int i = 0; i < rows.Length; i++)
            {
                if (!rows[i].Rect.gameObject.activeSelf) continue;
                float h = rows[i].Measure(s.W);
                rows[i].Place(new AvSlot(0f, y, s.W, h));
                y += h + 2f;
            }
        }

        public override void Restyle() { for (int i = 0; i < rows.Length; i++) rows[i].Restyle(); }
    }

    /// <summary>Paged, pooled row list with a click per row (the roster boards a page at a time need both).</summary>
    internal sealed class AvClickList : AvPart
    {
        public const int MaxPageSize = 32;
        private readonly AvRow[] rows;
        private readonly Action<int, AvRow> bind;
        private readonly AvControl prev, next;
        private readonly TMP_Text range;
        private int count;

        public AvClickList(RectTransform parent, AvTicker ticker, int pageSize, Action<int, AvRow> binder, Action<int> onRowClick)
        {
            Rect = AvLay.Child(parent, "ClickList");
            bind = binder;
            rows = new AvRow[Mathf.Clamp(pageSize, 1, MaxPageSize)];
            for (int i = 0; i < rows.Length; i++)
            {
                int slot = i;
                rows[i] = new AvRow(Rect, () => onRowClick?.Invoke(Page * rows.Length + slot));
                ticker?.Register(rows[i]);
            }
            prev = AvControl.Make(Rect, new AvControl.Spec("PREV", () => Go(Page - 1), AvButtonStyle.Quiet, AvIcon.ChevronLeft));
            next = AvControl.Make(Rect, new AvControl.Spec("NEXT", () => Go(Page + 1), AvButtonStyle.Quiet, AvIcon.ChevronRight));
            range = AvText.Make(Rect, "Range", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
        }

        public int Page { get; private set; }
        private int Pages => Math.Max(1, (count + rows.Length - 1) / rows.Length);

        public void SetCount(int n) { count = Math.Max(0, n); Go(Page); }
        public void Refresh() => Go(Page);

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
            range.text = count == 0 ? "0 OF 0" : (first + 1) + "–" + Math.Min(count, first + rows.Length) + " OF " + count;
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

        public override void Restyle() { foreach (AvRow r in rows) r.Restyle(); prev.Restyle(); next.Restyle(); }
    }
}
