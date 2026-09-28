using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        /// <summary>
        /// A bounded, pooled toggle grid built from kit v2's <see cref="MfdIconCell"/> (outline + LED + faint
        /// select wash + mono ON/OFF word — never the v1 solid-fill accent plate). One cell is a row: state
        /// LED, the item's own icon when it has one, the name, an optional one-line detail and a right-aligned
        /// ON/OFF/N-A word. Everything mutates in place, so a changing mission inventory cannot create or lay
        /// out an unbounded widget tree. An <see cref="AvFlow"/> page adds one with <c>page.Add(grid)</c>.
        /// </summary>
        private sealed class MfdPagingGrid : AvPart
        {
            private readonly MfdIconCell[] cells;
            private readonly int columns, rows, perPage;
            private readonly bool readOnly, pagerEnabled;
            private readonly float rowHeight;
            private readonly TMP_Text empty;
            private readonly AvControl prev, next;
            private readonly TMP_Text pageLabel;

            private int page, count;
            private Func<int, string> label;
            private Func<int, bool> selected;
            private Func<int, bool> enabledFn;
            private Action<int> clicked;
            private Func<int, Sprite> iconFn;
            private Func<int, string> subFn;

            public MfdPagingGrid(RectTransform parent, int columns, int rows, bool pager = true,
                bool readOnly = false, float rowHeight = 0f)
            {
                this.columns = Mathf.Max(1, columns);
                this.rows = Mathf.Max(1, rows);
                this.readOnly = readOnly;
                pagerEnabled = pager;
                this.rowHeight = rowHeight > 0f ? rowHeight : AvGridTokens.ToolCell;
                perPage = this.columns * this.rows;

                Rect = AvLay.Child(parent, "Grid");
                cells = new MfdIconCell[perPage];
                string onWord = readOnly ? "" : "ON", offWord = readOnly ? "" : "OFF";
                for (int i = 0; i < perPage; i++)
                {
                    int slot = i;
                    cells[i] = MfdIconCell.Toggle(Rect, () => IsSelected(slot), v => Click(slot), onWord, offWord);
                    cells[i].Rect.gameObject.SetActive(false);
                }
                empty = AvText.Make(Rect, "Empty", AvTextRole.ProseSmall, "NO ENTRIES", TextAlignmentOptions.Center, true);
                if (pagerEnabled)
                {
                    prev = AvControl.Make(Rect, new AvControl.Spec("PREV", () => Go(page - 1), AvButtonStyle.Quiet, AvIcon.ChevronLeft));
                    next = AvControl.Make(Rect, new AvControl.Spec("NEXT", () => Go(page + 1), AvButtonStyle.Quiet, AvIcon.ChevronRight));
                    pageLabel = AvText.Make(Rect, "Range", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
                }
            }

            /// <summary>The cell currently showing page-relative <paramref name="slot"/>; set
            /// <see cref="MfdIconCell.OnRightClick"/> on it to wire a solo/clear/assign action.</summary>
            public MfdIconCell CellAt(int slot) => slot >= 0 && slot < cells.Length ? cells[slot] : null;

            public int CurrentIndex(int slot) => page * perPage + slot;

            private int PageCount => Mathf.Max(1, Mathf.CeilToInt(count / (float)perPage));

            /// <summary>
            /// <paramref name="details"/> was the v1 hover-tooltip text; kit v2 has no tooltip seam on
            /// AvCell/AvControl yet, so it is accepted for source compatibility and otherwise unused
            /// (kit gap — see the slice report).
            /// </summary>
            public void SetData(int newCount, Func<int, string> labels, Func<int, bool> isSelected,
                Action<int> onClick, Func<int, bool> isEnabled = null, Func<int, Sprite> icons = null,
                Func<int, string> details = null, Func<int, string> subs = null)
            {
                count = Mathf.Max(0, newCount);
                label = labels;
                selected = isSelected;
                clicked = onClick;
                enabledFn = isEnabled;
                iconFn = icons;
                subFn = subs;
                int maxPage = Mathf.Max(0, PageCount - 1);
                if (page > maxPage) page = maxPage;
                Refresh();
            }

            public void ResetPage() { page = 0; Refresh(); }

            public void SetEmptyMessage(string message) { if (empty != null) empty.text = message ?? ""; }

            /// <summary>Temporarily fence controls while a native model is still initializing.</summary>
            public void SetInteractable(bool on)
            {
                if (on) { Refresh(); return; }
                for (int i = 0; i < cells.Length; i++) cells[i].Interactable = false;
                if (prev != null) prev.Interactable = false;
                if (next != null) next.Interactable = false;
            }

            private bool IsSelected(int slot)
            {
                int index = CurrentIndex(slot);
                return index >= 0 && index < count && !readOnly && selected != null && selected(index);
            }

            private void Go(int p) { page = Mathf.Clamp(p, 0, PageCount - 1); Refresh(); }

            private void Click(int slot)
            {
                int index = CurrentIndex(slot);
                if (readOnly || index < 0 || index >= count) return;
                if (enabledFn != null && !enabledFn(index)) return;
                clicked?.Invoke(index);
                Refresh();
            }

            private void Refresh()
            {
                int shownRows = Mathf.CeilToInt(Mathf.Min(perPage, Mathf.Max(0, count - page * perPage)) / (float)columns);
                int activeSlots = shownRows * columns;
                for (int i = 0; i < cells.Length; i++)
                {
                    int index = CurrentIndex(i);
                    bool exists = index >= 0 && index < count;
                    MfdIconCell cell = cells[i];
                    cell.Rect.gameObject.SetActive(exists || (count > 0 && i < activeSlots));
                    if (!exists) continue;

                    bool canUse = enabledFn == null || enabledFn(index);
                    cell.SetTitle(label != null ? label(index) : "", subFn != null ? subFn(index) : "");
                    cell.SetIcon(iconFn != null ? iconFn(index) : null);
                    cell.Interactable = canUse && !readOnly;
                    cell.Refresh();
                }

                empty.gameObject.SetActive(count == 0);
                if (pageLabel != null)
                    pageLabel.text = count == 0 ? "NO ENTRIES"
                        : "PAGE " + AvNum.Fixed(page + 1, 0) + " / " + AvNum.Fixed(PageCount, 0) +
                          "  ·  " + AvNum.Fixed(count, 0) + " ITEMS";
                if (prev != null) { prev.gameObject.SetActive(count > 0); prev.Interactable = page > 0; }
                if (next != null) { next.gameObject.SetActive(count > 0); next.Interactable = page < PageCount - 1; }
                if (pageLabel != null) pageLabel.gameObject.SetActive(count > 0);
            }

            public override float Measure(float width)
            {
                float h = rowHeight * rows + AvGridTokens.Gap * (rows - 1);
                if (pagerEnabled) h += AvGridTokens.Gap + AvGridTokens.Row;
                return h;
            }

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float colW = AvFlowMath.ColumnWidth(s.W, columns, AvGridTokens.Gap);
                for (int i = 0; i < cells.Length; i++)
                {
                    int row = i / columns, col = i % columns;
                    cells[i].Place(new AvSlot(col * (colW + AvGridTokens.Gap), row * (rowHeight + AvGridTokens.Gap), colW, rowHeight));
                }
                float gridH = rowHeight * rows + AvGridTokens.Gap * (rows - 1);
                AvLay.Place(empty.rectTransform, 0f, 0f, s.W, gridH);
                if (pagerEnabled)
                {
                    float y = gridH + AvGridTokens.Gap;
                    AvLay.Place(prev.Rect, 0f, y, 96f, AvGridTokens.Row);
                    AvLay.Place(next.Rect, s.W - 96f, y, 96f, AvGridTokens.Row);
                    AvLay.Place(pageLabel.rectTransform, 100f, y, s.W - 200f, AvGridTokens.Row);
                }
            }

            public override void Restyle()
            {
                foreach (MfdIconCell c in cells) c.Restyle();
                empty.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
                prev?.Restyle();
                next?.Restyle();
            }
        }
    }
}
