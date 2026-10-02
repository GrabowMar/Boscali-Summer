using NOAvionics;
using System;
using BoscaliSummer.Modules.Command.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>Contested-ground list that shows as many single-line rows as its slot holds and pages beyond that.</summary>
    internal sealed class StrNodeBoard : AvPart
    {
        private const float Gap = 2f, PagerH = AvGridTokens.RowDense;
        private const int MinRows = 3;
        private readonly AvRow[] rows;
        private readonly float rowH;
        private readonly Action<int, AvRow> bind;
        private readonly AvControl prev, next;
        private readonly TMP_Text range;
        private int count, page, boundFirst = -1, boundVisible = -1;
        private AvSlot lastSlot;
        private bool placed;

        public StrNodeBoard(RectTransform parent, AvTicker ticker, int maxRows, Action<int, AvRow> binder,
            float rowHeight = AvGridTokens.RowDense)
        {
            Rect = AvLay.Child(parent, "NodeBoard");
            bind = binder;
            rowH = Mathf.Max(AvGridTokens.RowDense, rowHeight);
            rows = new AvRow[Mathf.Clamp(maxRows, MinRows, 24)];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new AvRow(Rect) { Parent = this };
                rows[i].Rect.gameObject.SetActive(false);
                ticker?.Register(rows[i]);
            }
            prev = AvControl.Make(Rect, new AvControl.Spec("PREV", () => Go(page - 1), AvButtonStyle.Quiet, AvIcon.ChevronLeft));
            next = AvControl.Make(Rect, new AvControl.Spec("NEXT", () => Go(page + 1), AvButtonStyle.Quiet, AvIcon.ChevronRight, true));
            prev.Help = "Previous page of contested ground.";
            next.Help = "Next page of contested ground.";
            range = AvText.Make(Rect, "Range", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
            Restyle();
        }

        public int Capacity => rows.Length;

        public void SetCount(int n)
        {
            count = Mathf.Max(0, n);
            boundFirst = boundVisible = -1;
            if (placed) Place(lastSlot);
            Changed();
        }

        private void Go(int target)
        {
            page = Mathf.Max(0, target);
            boundFirst = boundVisible = -1;
            if (placed) Place(lastSlot);
        }

        public override float Measure(float width)
        {
            int n = Mathf.Min(count, MinRows);
            float h = n > 0 ? n * (rowH + Gap) - Gap : 0f;
            if (count > MinRows) h += Gap + PagerH;
            return h;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            lastSlot = s; placed = true;
            float pitch = rowH + Gap;
            int fitAll = Mathf.Min(rows.Length, Mathf.FloorToInt((s.H + Gap) / pitch));
            bool paged = count > fitAll;
            int cap = paged ? Mathf.Clamp(Mathf.FloorToInt((s.H - PagerH) / pitch), 1, rows.Length) : Mathf.Max(0, count);
            int pages = cap > 0 ? Mathf.Max(1, (count + cap - 1) / cap) : 1;
            page = Mathf.Clamp(page, 0, pages - 1);
            int first = page * cap, visible = Mathf.Clamp(count - first, 0, cap);
            bool rebind = first != boundFirst || visible != boundVisible;
            boundFirst = first; boundVisible = visible;
            for (int i = 0; i < rows.Length; i++)
            {
                bool on = i < visible;
                if (rows[i].Rect.gameObject.activeSelf != on) rows[i].Rect.gameObject.SetActive(on);
                if (!on) continue;
                if (rebind) bind?.Invoke(first + i, rows[i]);
                rows[i].Place(new AvSlot(0f, i * pitch, s.W, rowH));
            }
            prev.gameObject.SetActive(paged); next.gameObject.SetActive(paged); range.gameObject.SetActive(paged);
            if (!paged) return;
            float y = visible * pitch;
            prev.Interactable = page > 0;
            next.Interactable = page < pages - 1;
            range.text = count == 0 ? "0 OF 0" : (first + 1) + "–" + (first + visible) + " OF " + count;
            AvLay.Place(prev.Rect, 0f, y, 96f, PagerH);
            AvLay.Place(next.Rect, s.W - 96f, y, 96f, PagerH);
            AvLay.Place(range.rectTransform, 100f, y, s.W - 200f, PagerH);
        }

        public override void Restyle()
        {
            for (int i = 0; i < rows.Length; i++) rows[i].Restyle();
            range.color = StrPaint.Dim;
            prev.Restyle();
            next.Restyle();
        }
    }
}
