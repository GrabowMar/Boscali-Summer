using System;
using System.Collections.Generic;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NOAvionics.Ui
{
    /// <summary>One choice in an <see cref="AvPopup"/>.</summary>
    public readonly struct AvPopupEntry
    {
        public readonly string Text;
        public readonly string Detail;
        public readonly bool Selected;
        public readonly bool Enabled;

        public AvPopupEntry(string text, string detail = null, bool selected = false, bool enabled = true)
        {
            Text = text;
            Detail = detail;
            Selected = selected;
            Enabled = enabled;
        }
    }

    /// <summary>
    /// Kit v2 pick list (replaces v1 <c>AvKit.Popup</c>, same call shape). A full-page click-catcher closes it; at most
    /// <see cref="MaxRows"/> rows show, longer lists page with a "MORE…" row. Only one popup is open at a time.
    /// <c>area</c> is in the page root's top-left space with y already negative downward (as v1 took it).
    /// </summary>
    public sealed class AvPopup
    {
        public const int MaxRows = 7;
        private const int PagedRows = MaxRows - 1;
        private const float RowH = 32f, Inset = 4f;
        private static AvPopup open;

        private readonly GameObject root;
        private readonly RectTransform list;
        private readonly AvFrame ground;
        private readonly List<Row> rows = new List<Row>(MaxRows);
        private int page;

        public AvPopup(RectTransform pageRoot, float panelWidth)
        {
            root = new GameObject("AvPopup", typeof(RectTransform));
            var rt = (RectTransform)root.transform;
            rt.SetParent(pageRoot, false);
            AvLay.Fill(rt);

            var catcherGo = new GameObject("CloseCatcher", typeof(RectTransform), typeof(CanvasRenderer));
            catcherGo.transform.SetParent(rt, false);
            var catcher = catcherGo.AddComponent<UnityEngine.UI.Image>();
            catcher.color = Color.clear;
            AvLay.Fill((RectTransform)catcherGo.transform);
            AvHit.On(catcher).Click = _ => Close();

            list = AvLay.Child(rt, "PopupList");
            ground = AvFrame.Add(list, "Ground", AvChamfer.Diagonal(6f));
            AvLay.Fill(ground.rectTransform);
            ground.raycastTarget = true;   // clicks between rows must not reach the catcher
            root.SetActive(false);
        }

        public static void CloseAny() => open?.Close();

        public bool IsOpen => root != null && root.activeSelf;

        public void Show(Rect area, IReadOnlyList<AvPopupEntry> entries, Action<int> onPick)
        {
            page = 0;
            Render(area, entries, onPick);
        }

        public void Close()
        {
            if (root == null) return;
            root.SetActive(false);
            if (ReferenceEquals(open, this)) open = null;
        }

        private void Render(Rect area, IReadOnlyList<AvPopupEntry> entries, Action<int> onPick)
        {
            if (root == null) return;
            if (open != null && !ReferenceEquals(open, this)) open.Close();
            open = this;

            int total = entries?.Count ?? 0;
            bool paged = total > MaxRows;
            int perPage = paged ? PagedRows : MaxRows;
            int pages = paged ? Mathf.CeilToInt(total / (float)perPage) : 1;
            page = pages > 0 ? ((page % pages) + pages) % pages : 0;
            int first = page * perPage;
            int shown = Mathf.Max(0, Mathf.Min(perPage, total - first));
            int used = shown + (paged ? 1 : 0);

            float height = Mathf.Max(RowH, RowH * used) + Inset * 2f;
            list.anchorMin = list.anchorMax = list.pivot = new Vector2(0f, 1f);
            list.anchoredPosition = new Vector2(area.x, area.y);
            list.sizeDelta = new Vector2(area.width, height);
            AvStyle g = AvStyleHost.FuiStyle("card raised");
            ground.Paint(AvStyleHost.Resolve(g.Background, AvTheme.SurfaceRaised), AvStyleHost.Resolve(g.Border, AvTheme.Frame));

            while (rows.Count < MaxRows) rows.Add(new Row(list, rows.Count));
            for (int i = 0; i < rows.Count; i++)
            {
                float y = Inset + i * RowH;
                if (i < shown)
                {
                    int index = first + i;
                    rows[i].Bind(entries[index], area.width, y, () => { Close(); onPick?.Invoke(index); });
                }
                else if (paged && i == shown)
                {
                    rows[i].Bind(new AvPopupEntry("MORE…", "PAGE " + (page + 1) + " OF " + pages), area.width, y,
                        () => { page++; Render(area, entries, onPick); });
                }
                else rows[i].Hide();
            }
            root.SetActive(true);
            root.transform.SetAsLastSibling();
        }

        private sealed class Row
        {
            private readonly RectTransform rect;
            private readonly AvFrame frame;
            private readonly TMP_Text text, detail;
            private readonly AvHit hit;
            private AvPopupEntry entry;
            private bool hover;
            private Action click;

            public Row(RectTransform parent, int index)
            {
                rect = AvLay.Child(parent, "PopupRow" + index);
                frame = AvFrame.Add(rect, "Frame", default(AvChamfer));
                AvLay.Fill(frame.rectTransform);
                text = AvText.Make(rect, "Text", AvTextRole.Label);
                detail = AvText.Make(rect, "Detail", AvTextRole.ProseSmall, "", TextAlignmentOptions.MidlineRight);
                AvText.Fit(text, false);
                AvText.Fit(detail, false);
                hit = AvHit.On(frame);
                hit.Hover = h => { hover = h; Paint(); };
                hit.Click = e => { if (e == null || e.button == PointerEventData.InputButton.Left) click?.Invoke(); };
            }

            public void Bind(AvPopupEntry e, float width, float y, Action onClick)
            {
                entry = e;
                click = onClick;
                rect.gameObject.SetActive(true);
                AvLay.Place(rect, Inset, y, width - 2f * Inset, RowH - 2f);
                float w = width - 2f * Inset;
                bool hasDetail = !string.IsNullOrEmpty(e.Detail);
                AvLay.Place(text.rectTransform, 10f, 0f, hasDetail ? w * 0.55f - 10f : w - 20f, RowH - 2f);
                AvLay.Place(detail.rectTransform, w * 0.55f, 0f, w * 0.45f - 10f, RowH - 2f);
                text.text = e.Text ?? "";
                detail.text = e.Detail ?? "";
                hit.Interactable = e.Enabled;
                Paint();
            }

            public void Hide() => rect.gameObject.SetActive(false);

            private void Paint()
            {
                string state = !entry.Enabled ? "disabled" : entry.Selected ? "armed" : hover ? "hover" : null;
                AvStyle s = AvStyleHost.FuiStyle("row", state);
                frame.Paint(AvStyleHost.Resolve(s.Background, AvTheme.SurfaceInert),
                    s.Border.HasValue ? AvStyleHost.Resolve(s.Border, Color.clear) : Color.clear);
                text.color = !entry.Enabled ? AvTheme.Disabled : AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
                detail.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            }
        }
    }
}
