using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static partial class VanillaMfdRebuild
    {
        // ------------------------------------------------------------------ widgets

        /// <summary>
        /// A bounded, pooled control grid. It mutates labels and latch state in place, so a
        /// changing mission inventory cannot create or lay out an unbounded widget tree.
        /// </summary>
        private sealed class MfdPagingGrid
        {
            private readonly AvButton[] buttons;
            private readonly bool readOnly;
            private readonly TMP_Text empty;
            private readonly int perPage;
            private readonly TMP_Text pageLabel;
            private readonly AvButton previous;
            private readonly AvButton next;
            private readonly RectTransform pagerRoot;

            private int page;
            private int count;
            private Func<int, string> label;
            private Func<int, bool> selected;
            private Func<int, bool> enabled;
            private Action<int> clicked;
            private Func<int, Sprite> icon;
            private Func<int, string> detail;

            public MfdPagingGrid(RectTransform parent, float y, float width, int columns, int rows,
                                  bool pager = true, bool readOnly = false, float rowHeight = 0f)
            {
                if (rowHeight <= 0f) rowHeight = AvTokens.RowHeight;
                this.readOnly = readOnly;
                empty = AvStyled.Label(parent, new Rect(AvTokens.Space3, y, width-AvTokens.Space3, AvTokens.RowHeight),
                    "NO ENTRIES", "row-sub");
                perPage = Mathf.Max(1, columns * rows);
                buttons = new AvButton[perPage];
                float gap = AvTokens.Gap;
                float cellWidth = (width - AvTokens.Space3 - gap * (columns - 1)) / columns;
                for (int i = 0; i < perPage; i++)
                {
                    int slot = i;
                    int row = i / columns;
                    int column = i % columns;
                    buttons[i] = PanelButton(parent,
                        new Rect(AvTokens.Space3 + column * (cellWidth + gap),
                                 y - row * (rowHeight + gap),
                                 cellWidth, rowHeight),
                        "", "toggle", () => Click(slot), readOnly ? AvButtonStyle.Quiet : AvButtonStyle.Toggle);
                    if (readOnly)
                    {
                        buttons[i].InitialiseHit(null);
                        buttons[i].GetComponentInChildren<TMP_Text>().color = AvTheme.TextPrimary;
                    }
                }

                if (pager)
                {
                    float pagerY = y - rows * (rowHeight + gap) - AvTokens.Space1;
                    var go = new GameObject("Pager", typeof(RectTransform));
                    pagerRoot = go.GetComponent<RectTransform>();
                    pagerRoot.SetParent(parent, false);
                    AvKit.Place(pagerRoot, new Rect(AvTokens.Space3, pagerY, width-AvTokens.Space3, AvTokens.RowHeight));
                    AvButton[] pagerButtons = AvKit.Stepper(pagerRoot, 0f, 0f,
                                                             width - AvTokens.Space3,
                                                             out pageLabel, Previous, Next);
                    previous = pagerButtons[0];
                    next = pagerButtons[1];
                }
            }

            public int CurrentIndex(int slot) => page * perPage + slot;

            public AvButton ButtonAt(int slot) =>
                slot >= 0 && slot < buttons.Length ? buttons[slot] : null;

            public void SetData(int newCount, Func<int, string> labels,
                                Func<int, bool> isSelected, Action<int> onClick,
                                Func<int, bool> isEnabled = null, Func<int, Sprite> icons = null,
                                Func<int, string> details = null)
            {
                count = Mathf.Max(0, newCount);
                label = labels;
                selected = isSelected;
                clicked = onClick;
                enabled = isEnabled;
                icon = icons;
                detail = details;
                int maxPage = Mathf.Max(0, PageCount - 1);
                if (page > maxPage) page = maxPage;
                Refresh();
            }

            public void ResetPage()
            {
                page = 0;
                Refresh();
            }

            /// <summary>Temporarily fence controls while a native model is still initializing.</summary>
            public void SetInteractable(bool on)
            {
                if (on)
                {
                    Refresh();
                    return;
                }

                for (int i = 0; i < buttons.Length; i++) buttons[i].SetEnabled(false);
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
                for (int i = 0; i < buttons.Length; i++)
                {
                    int index = CurrentIndex(i);
                    bool exists = index >= 0 && index < count;
                    bool canUse = exists && (enabled == null || enabled(index));
                    buttons[i].SetEnabled(canUse);
                    buttons[i].gameObject.SetActive(exists);
                    string text = exists && label != null ? label(index) : "";
                    PaintButton(buttons[i], text, exists && selected != null && selected(index),
                        exists && icon != null ? icon(index) : null);
                    buttons[i].WithTooltip(exists && detail != null ? detail(index) : text);
                }

                empty.gameObject.SetActive(count == 0);
                if (pageLabel != null) pageLabel.text = count == 0 ? "NO ENTRIES" :
                    (page + 1).ToString() + " / " + PageCount.ToString();
                if (pagerRoot != null) pagerRoot.gameObject.SetActive(PageCount > 1);
                previous?.SetEnabled(page > 0);
                next?.SetEnabled(page < PageCount - 1);
            }
        }
    }
}
