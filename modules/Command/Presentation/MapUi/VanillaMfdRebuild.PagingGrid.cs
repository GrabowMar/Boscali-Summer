using NOAvionics;
using System;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
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
        /// <summary>Add <paramref name="grid"/> to <paramref name="flow"/> and let it request a relayout when its
        /// content height changes (a grid sizes to the rows it shows).</summary>
        private static MfdPagingGrid AddGrid(AvFlow flow, MfdPagingGrid grid)
        {
            flow.Add(grid);
            grid.SizeChanged = flow.RequestRelayout;
            return grid;
        }

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
            private Func<int, string> detailFn;
            private Func<int, AvIcon> glyphFn;
            private readonly bool tile;
            private int lastSizeKey = -1;

            /// <summary>Raised when the grid's measured height changes (rows shown, pager shown), so the
            /// owning <see cref="AvFlow"/> can relayout. Wired by <see cref="AddGrid"/>.</summary>
            public Action SizeChanged;

            public MfdPagingGrid(RectTransform parent, int columns, int rows, bool pager = true,
                bool readOnly = false, float rowHeight = 0f, bool tile = false)
            {
                this.tile = tile;
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
                    cells[i] = MfdIconCell.Toggle(Rect, () => IsSelected(slot), v => Click(slot), onWord, offWord, tile);
                    cells[i].Rect.gameObject.SetActive(false);
                }
                empty = AvText.Make(Rect, "Empty", AvTextRole.ProseSmall, "NO ENTRIES", TextAlignmentOptions.Center, true);
                if (pagerEnabled)
                {
                    prev = AvControl.Make(Rect, new AvControl.Spec("PREV", () => Go(page - 1), AvButtonStyle.Quiet, AvIcon.ChevronLeft));
                    next = AvControl.Make(Rect, new AvControl.Spec("NEXT", () => Go(page + 1), AvButtonStyle.Quiet, AvIcon.ChevronRight, true));
                    pageLabel = AvText.Make(Rect, "Range", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
                }
            }

            /// <summary>The cell currently showing page-relative <paramref name="slot"/>; set
            /// <see cref="MfdIconCell.OnRightClick"/> on it to wire a solo/clear/assign action.</summary>
            public MfdIconCell CellAt(int slot) => slot >= 0 && slot < cells.Length ? cells[slot] : null;

            public int CurrentIndex(int slot) => page * perPage + slot;

            private int PageCount => Mathf.Max(1, Mathf.CeilToInt(count / (float)perPage));

            private bool PagerShown => pagerEnabled && PageCount > 1;

            /// <summary>Rows the grid occupies: one line for the empty message, a full page on every page of a
            /// multi-page grid (so the pager stays put), and only the rows holding items on a single page.</summary>
            private int ShownRows()
            {
                if (count == 0) return 1;
                if (PageCount > 1) return rows;
                return Mathf.Max(1, Mathf.CeilToInt(Mathf.Min(perPage, count) / (float)columns));
            }


            /// <summary>
            /// <paramref name="details"/> is the hover help for a cell (shown in the console footer);
            /// when it is null the cell's own label is shown instead.
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
                detailFn = details;
                int maxPage = Mathf.Max(0, PageCount - 1);
                if (page > maxPage) page = maxPage;
                Refresh();
            }

            /// <summary>Tile grids: the chrome glyph for item <c>i</c> (used when the item has no sprite). Set before SetData.</summary>
            public void SetGlyphs(Func<int, AvIcon> glyphs) { glyphFn = glyphs; }

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
                for (int i = 0; i < cells.Length; i++)
                {
                    int index = CurrentIndex(i);
                    bool exists = index >= 0 && index < count;
                    MfdIconCell cell = cells[i];
                    cell.Rect.gameObject.SetActive(exists);
                    if (!exists) continue;

                    bool canUse = enabledFn == null || enabledFn(index);
                    string text = label != null ? label(index) : "";
                    cell.SetTitle(text, subFn != null ? subFn(index) : "");
                    cell.SetIcon(iconFn != null ? iconFn(index) : null);
                    if (tile) cell.SetGlyph(glyphFn != null ? glyphFn(index) : AvIcon.None);
                    cell.Interactable = canUse && !readOnly;
                    // A fenced cell still publishes its "why" to the footer; Click() gates the action.
                    cell.Help = detailFn != null ? detailFn(index) : text;
                    cell.Refresh();
                }

                empty.gameObject.SetActive(count == 0);
                if (pageLabel != null)
                    pageLabel.text = count == 0 ? "—"
                        : AvNum.Fixed(page + 1, 0) + " / " + AvNum.Fixed(PageCount, 0);
                bool pager = PagerShown;
                if (prev != null) { prev.gameObject.SetActive(pager); prev.Interactable = page > 0; }
                if (next != null) { next.gameObject.SetActive(pager); next.Interactable = page < PageCount - 1; }
                if (pageLabel != null) pageLabel.gameObject.SetActive(pager);

                // Wrapping changes with content even when the number of rows stays the same.
                Changed();

                int sizeKey = ShownRows() * 2 + (pager ? 1 : 0);
                if (sizeKey != lastSizeKey)
                {
                    lastSizeKey = sizeKey;
                    SizeChanged?.Invoke();
                }
            }

            public override float Measure(float width)
            {
                float h = MeasuredRowHeight(width) * ShownRows() + AvGridTokens.Gap * (ShownRows() - 1);
                if (PagerShown) h += AvGridTokens.Gap + AvGridTokens.Row;
                return h;
            }

            private float MeasuredRowHeight(float width)
            {
                float height = rowHeight;
                float column = AvFlowMath.ColumnWidth(width, columns, AvGridTokens.Gap);
                foreach (MfdIconCell cell in cells)
                    if (cell.Rect.gameObject.activeSelf) height = Mathf.Max(height, cell.Measure(column));
                return height;
            }

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float colW = AvFlowMath.ColumnWidth(s.W, columns, AvGridTokens.Gap);
                float height = MeasuredRowHeight(s.W);
                for (int i = 0; i < cells.Length; i++)
                {
                    int row = i / columns, col = i % columns;
                    cells[i].Place(new AvSlot(col * (colW + AvGridTokens.Gap), row * (height + AvGridTokens.Gap), colW, height));
                }
                float gridH = height * ShownRows() + AvGridTokens.Gap * (ShownRows() - 1);
                AvLay.Place(empty.rectTransform, 0f, 0f, s.W, gridH);
                if (PagerShown)
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
