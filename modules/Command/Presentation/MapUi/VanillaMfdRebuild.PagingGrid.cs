using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        // ------------------------------------------------------------------ widgets

        /// <summary>
        /// A bounded, pooled control grid. Each cell is a row rather than a lone text
        /// toggle: a state LED, the item's own icon when it has one, the name, an optional
        /// one-line detail and a right-aligned state word. The whole page of cells sits on
        /// one card with hairline seams, so a filter matrix reads as a single control
        /// instead of nine unrelated buttons. Everything mutates in place, so a changing
        /// mission inventory cannot create or lay out an unbounded widget tree.
        /// </summary>
        private sealed class MfdPagingGrid
        {
            private const float LedInset = 9f;
            private const float LedSize = 3f;
            private const float IconSize = 16f;
            private const float ValueWidth = 36f;
            /// <summary>At or above this a cell may wrap its name to a second line.</summary>
            private const float TallCell = 36f;

            private readonly Cell[] cells;
            private readonly bool readOnly;
            private readonly bool exclusive;
            private readonly Image[] rowRules;
            private readonly float rowHeight;
            private readonly float cellWidth;
            private readonly float top;
            private readonly int columns;
            private readonly TMP_Text empty;
            private readonly int perPage;
            private readonly TMP_Text pageLabel;
            private readonly AvButton previous;
            private readonly AvButton next;
            private readonly RectTransform pagerRoot;

            private int page;
            private int count;
            private bool laidOut;
            private bool laidOutIcons;
            private bool laidOutSubs;
            private Func<int, string> label;
            private Func<int, bool> selected;
            private Func<int, bool> enabled;
            private Action<int> clicked;
            private Func<int, Sprite> icon;
            private Func<int, string> detail;
            private Func<int, string> sub;

            public MfdPagingGrid(RectTransform parent, float y, float width, int columns, int rows,
                                  bool pager = true, bool readOnly = false, float rowHeight = 0f,
                                  bool exclusive = false)
            {
                if (rowHeight <= 0f) rowHeight = AvTokens.RowHeight;
                this.readOnly = readOnly;
                this.exclusive = exclusive;
                this.rowHeight = rowHeight;
                this.columns = Mathf.Max(1, columns);
                rows = Mathf.Max(1, rows);
                top = y;
                cellWidth = Mathf.Max(40f, width - AvTokens.Space3) / this.columns;
                perPage = this.columns * rows;

                // The matrix is a single dark instrument well. Sparse cyan dividers mark
                // addressable softkeys; the green vertical cue is reserved for live state.
                Rect grid = new Rect(AvTokens.Space3, y, cellWidth * this.columns, rows * rowHeight);
                AvKit.Panel(parent, grid, AvTheme.SurfaceInert);
                AvKit.Outline(parent, grid, AvTheme.Hairline.WithAlpha(0.7f));
                AvKit.Rule(parent, new Rect(grid.x, grid.y, 28f, 2f), AvTheme.RailInfo);
                Color seam = AvTheme.Hairline.WithAlpha(0.35f);
                for (int c = 1; c < this.columns; c++)
                    AvKit.Rule(parent, new Rect(grid.x + c * cellWidth, grid.y, 1f, grid.height), seam);
                rowRules = new Image[Mathf.Max(0, rows - 1)];
                for (int r = 1; r < rows; r++)
                    rowRules[r - 1] = AvKit.Rule(parent, new Rect(grid.x, grid.y - r * rowHeight, grid.width, 1f), seam);

                empty = AvStyled.Label(parent,
                    new Rect(grid.x + 10f, y - Mathf.Max(6f, (grid.height - 30f) * 0.5f), grid.width - 20f, 30f),
                    "NO ENTRIES", "row-sub", align: TextAlignmentOptions.Center);
                empty.enableWordWrapping = true;

                bool tall = rowHeight >= TallCell;
                cells = new Cell[perPage];
                for (int i = 0; i < perPage; i++)
                {
                    int slot = i;
                    int row = i / this.columns;
                    int column = i % this.columns;
                    float x = AvTokens.Space3 + column * cellWidth;
                    cells[i] = new Cell(parent, x, y - row * rowHeight, cellWidth, rowHeight, tall, false, readOnly);
                    cells[i].Hit.SetAction(() => Click(slot));
                }

                if (pager)
                {
                    float pagerY = y - rows * rowHeight - AvTokens.Space1;
                    var go = new GameObject("Pager", typeof(RectTransform));
                    pagerRoot = go.GetComponent<RectTransform>();
                    pagerRoot.SetParent(parent, false);
                    AvKit.Place(pagerRoot, new Rect(AvTokens.Space3, pagerY, width - AvTokens.Space3, AvTokens.RowHeight));
                    AvButton[] pagerButtons = AvKit.Stepper(pagerRoot, 0f, 0f,
                                                             width - AvTokens.Space3,
                                                             out pageLabel, Previous, Next,
                                                             "Page the grid");
                    previous = pagerButtons[0];
                    next = pagerButtons[1];
                }
            }

            public int CurrentIndex(int slot) => page * perPage + slot;

            public AvButton ButtonAt(int slot) =>
                slot >= 0 && slot < cells.Length ? cells[slot].Hit : null;

            public void SetData(int newCount, Func<int, string> labels,
                                Func<int, bool> isSelected, Action<int> onClick,
                                Func<int, bool> isEnabled = null, Func<int, Sprite> icons = null,
                                Func<int, string> details = null, Func<int, string> subs = null)
            {
                count = Mathf.Max(0, newCount);
                label = labels;
                selected = isSelected;
                clicked = onClick;
                enabled = isEnabled;
                icon = icons;
                detail = details;
                sub = subs;
                int maxPage = Mathf.Max(0, PageCount - 1);
                if (page > maxPage) page = maxPage;
                Refresh();
            }

            public void ResetPage()
            {
                page = 0;
                Refresh();
            }

            public void SetEmptyMessage(string message)
            {
                if (empty != null) empty.text = message;
            }

            /// <summary>Temporarily fence controls while a native model is still initializing.</summary>
            public void SetInteractable(bool on)
            {
                if (on)
                {
                    Refresh();
                    return;
                }

                for (int i = 0; i < cells.Length; i++) cells[i].Hit.SetEnabled(false);
                previous?.SetEnabled(false);
                next?.SetEnabled(false);
            }

            private int PageCount => Mathf.Max(1, Mathf.CeilToInt(count / (float)perPage));

            private void Previous()
            {
                if (page > 0) page--;
                Refresh();
            }

            private void Next()
            {
                if (page < PageCount - 1) page++;
                Refresh();
            }

            private void Click(int slot)
            {
                int index = CurrentIndex(slot);
                if (readOnly || index < 0 || index >= count) return;
                if (enabled != null && !enabled(index)) return;
                clicked?.Invoke(index);
                Refresh();
            }

            private void Refresh()
            {
                // A grid that carries item icons spends a fixed column on them so names
                // still line up; one that never carries them gives that column to the name,
                // which is what keeps full platform-type words inside a three-column cell.
                bool icons = false;
                if (icon != null)
                {
                    for (int i = 0; i < cells.Length; i++)
                    {
                        int index = CurrentIndex(i);
                        if (index >= 0 && index < count && icon(index) != null) { icons = true; break; }
                    }
                }

                bool subs = sub != null;
                if (!laidOut || laidOutIcons != icons || laidOutSubs != subs)
                {
                    laidOut = true;
                    laidOutIcons = icons;
                    laidOutSubs = subs;
                    bool tall = rowHeight >= TallCell;
                    for (int i = 0; i < cells.Length; i++)
                    {
                        int row = i / columns;
                        int column = i % columns;
                        cells[i].Place(AvTokens.Space3 + column * cellWidth, top - row * rowHeight,
                                       cellWidth, rowHeight, tall, icons, subs);
                    }
                }

                int shownRows = Mathf.CeilToInt(Mathf.Min(perPage, Mathf.Max(0, count - page * perPage)) / (float)columns);
                int activeSlots = shownRows * columns;

                for (int i = 0; i < cells.Length; i++)
                {
                    int index = CurrentIndex(i);
                    bool exists = index >= 0 && index < count;
                    bool canUse = exists && (enabled == null || enabled(index));
                    bool on = exists && !readOnly && selected != null && selected(index);
                    Cell cell = cells[i];

                    if (!exists)
                    {
                        bool inActiveRow = count > 0 && i < activeSlots;
                        cell.Root.gameObject.SetActive(inActiveRow);
                        if (inActiveRow)
                        {
                            cell.Hit.SetEnabled(false);
                            cell.Hit.WithTooltip(null);
                            cell.Icon.enabled = false;
                            cell.Led.gameObject.SetActive(false);
                            cell.Name.text = "";
                            cell.Name.color = AvTheme.Disabled;
                            if (cell.Sub != null && cell.Sub.enabled) cell.Sub.text = "";
                            cell.State.text = "";
                            cell.Hit.SetRowHighlight(cell.Ground, Color.clear, Color.clear);
                        }
                        continue;
                    }

                    cell.Root.gameObject.SetActive(true);

                    // The hit target stays live so a fenced cell still publishes its "why"
                    // to the status strip; Click() is where the action is actually gated.
                    string text = label != null ? label(index) : "";
                    cell.Hit.SetEnabled(true);
                    cell.Hit.WithTooltip(detail != null ? detail(index) : text);

                    Sprite sprite = icon != null ? icon(index) : null;
                    cell.Icon.sprite = sprite;
                    cell.Icon.enabled = sprite != null;
                    cell.Led.gameObject.SetActive(!readOnly);

                    cell.Name.text = text;
                    cell.Name.color = canUse ? AvTheme.TextPrimary : AvTheme.Disabled;
                    if (cell.Sub.enabled)
                    {
                        cell.Sub.text = sub != null ? sub(index) : "";
                        cell.Sub.color = canUse ? AvTheme.Dim : AvTheme.Disabled;
                    }

                    if (readOnly)
                    {
                        cell.State.text = "";
                    }
                    else if (!canUse)
                    {
                        cell.State.text = "N/A";
                        cell.State.color = AvTheme.Disabled;
                    }
                    else if (on)
                    {
                        cell.State.text = exclusive ? "SET" : "ON";
                        cell.State.color = AvTheme.Accent;
                    }
                    else
                    {
                        cell.State.text = exclusive ? "—" : "OFF";
                        cell.State.color = AvTheme.Dim;
                    }

                    cell.Led.color = canUse && on ? AvTheme.Accent : AvTheme.RailInert;

                    // Keep a latched cell readable even when the pointer is elsewhere. The
                    // ON/OFF word and LED remain the primary redundant state cues; this wash
                    // only makes the selected row easier to find in a dense matrix.
                    Color rest = canUse && on ? AvTheme.Accent.WithAlpha(0.11f) : Color.clear;
                    Color hover = canUse && on ? AvTheme.Accent.WithAlpha(0.17f)
                        : AvTheme.RailInfo.WithAlpha(0.10f);
                    cell.Hit.SetRowHighlight(cell.Ground, rest, hover);
                }

                empty.gameObject.SetActive(count == 0);
                for (int i = 0; i < rowRules.Length; i++) rowRules[i].enabled = i + 1 < shownRows;
                if (pageLabel != null)
                    pageLabel.text = count == 0 ? "NO ENTRIES"
                        : "PAGE " + (page + 1) + " / " + PageCount + "  ·  " + count + " ITEMS";
                if (pagerRoot != null) pagerRoot.gameObject.SetActive(count > 0);
                previous?.SetEnabled(page > 0);
                next?.SetEnabled(page < PageCount - 1);
            }

            /// <summary>
            /// One pooled row. It owns its own layout so a grid can hand it the icon column
            /// once, and everything after that is text and colour writes.
            /// </summary>
            private sealed class Cell
            {
                public readonly RectTransform Root;
                public readonly Image Ground;
                public readonly Image Led;
                public readonly Image Icon;
                public readonly TMP_Text Name;
                public readonly TMP_Text Sub;
                public readonly TMP_Text State;
                public readonly AvButton Hit;
                private readonly bool readOnly;

                public Cell(RectTransform parent, float x, float y, float width, float height,
                            bool tall, bool subs, bool readOnly)
                {
                    this.readOnly = readOnly;
                    var go = new GameObject("GridCell", typeof(RectTransform), typeof(Image));
                    Root = go.GetComponent<RectTransform>();
                    Root.SetParent(parent, false);

                    Ground = go.GetComponent<Image>();
                    Ground.color = Color.clear;
                    Ground.raycastTarget = true;

                    Led = AvKit.Panel(Root, new Rect(0f, 0f, LedSize, 20f), AvTheme.RailInert);
                    Icon = AvKit.Panel(Root, new Rect(0f, 0f, IconSize, IconSize), Color.white);
                    Icon.preserveAspect = true;
                    Icon.enabled = false;

                    Name = AvStyled.Label(Root, new Rect(0f, 0f, 10f, 16f), "", "row-name");
                    Name.fontSizeMin = AvTokens.FontSmall;
                    Name.fontSizeMax = AvTokens.FontBody;
                    Name.enableAutoSizing = true;
                    Sub = AvStyled.Label(Root, new Rect(0f, 0f, 10f, 13f), "", "row-sub");
                    State = AvStyled.Label(Root, new Rect(0f, 0f, ValueWidth, 16f), "", "row-value",
                                           align: TextAlignmentOptions.MidlineRight);

                    Hit = go.AddComponent<AvButton>();
                    Hit.InitialiseHit(null);
                    Hit.SetRowHighlight(Ground, Color.clear,
                        AvTheme.Unity(AvTokens.RowFill(AvTheme.Accent.ToRgba(), false, true)));

                    Place(x, y, width, height, tall, false, subs);
                }

                public void Place(float x, float y, float width, float height, bool tall, bool icons, bool subs)
                {
                    AvKit.Place(Root, new Rect(x, y, width, height));
                    AvKit.Place(Led.rectTransform,
                        new Rect(LedInset, -(height - 20f) * 0.5f, LedSize, 20f));
                    AvKit.Place(Icon.rectTransform,
                        new Rect(LedInset + 15f, -(height - IconSize) * 0.5f, IconSize, IconSize));

                    float nameX = icons ? 44f : readOnly ? 12f : 24f;
                    float nameWidth = Mathf.Max(24f, width - nameX - (readOnly ? 0f : ValueWidth) - 10f);
                    // A cell with no detail line gives the whole height to its name; a grid
                    // that never shows a state (a read-only list) still gets the width.
                    float nameHeight = tall ? Mathf.Max(16f, height - (subs ? 26f : 8f)) : 16f;
                    AvKit.Place(Name.rectTransform, new Rect(nameX, -(height - nameHeight - (subs ? 16f : 0f)) * .5f, nameWidth, nameHeight));
                    AvKit.Place(Sub.rectTransform, new Rect(nameX, -(height - 15f), nameWidth, 13f));
                    Sub.enabled = tall && subs;
                    // The full native name remains in the cell's tooltip. A long name
                    // signals overflow instead of silently losing its last words.
                    Name.enableWordWrapping = tall;
                    Name.overflowMode = tall ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;

                    AvKit.Place(State.rectTransform,
                        new Rect(width - ValueWidth - 6f, -(height - 16f) * 0.5f, ValueWidth, 16f));
                }
            }
        }
    }
}
