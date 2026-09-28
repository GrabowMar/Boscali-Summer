using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>
    /// Small kit v2 building blocks shared by the STR console and its two floating windows.
    /// The kit has no plain wrapped-paragraph part and no fixed, unpaged pool of clickable rows
    /// (<see cref="AvList"/> is always paged); <see cref="ProseText"/> and <see cref="AvRowStack"/>
    /// fill those gaps locally, built from <see cref="AvPart"/>/<see cref="AvText"/>/<see cref="AvLay"/>
    /// primitives exactly as the kit's own parts are. Kit gap, reported alongside the slice.
    /// </summary>
    internal static class TabHelp
    {
        /// <summary>Hover help for a console's icon tabs, in tab order (null keeps a tab without help).</summary>
        public static void Apply(AvTabBar bar, params string[] hints)
        {
            if (bar == null) return;
            AvControl[] tabs = bar.Rect.GetComponentsInChildren<AvControl>(true);
            for (int i = 0; i < tabs.Length && i < hints.Length; i++)
                if (hints[i] != null) tabs[i].Help = hints[i];
        }
    }

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

    /// <summary>A fixed pool of clickable rows with no pager (roster/log boards bounded well under a page).</summary>
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
}
