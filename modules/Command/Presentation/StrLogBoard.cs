using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
  internal sealed class StrLogBoard : AvPart
    {
        private const float PadX = 10f, StampW = 42f, MinH = 26f;
        private readonly Action<int> onClick;
        private readonly Line[] lines;
        private readonly int minRows;
        private readonly LogRules rules;
        private int count;
        private AvSlot lastSlot;
        private bool placed;

        private sealed class Line
        {
            public RectTransform Root;
            public AvFrame Frame;
            public Image Rail;
            public TMP_Text Stamp, Text;
            public AvHelpTip Tip;
            public AvHit Hit;
            public AvState State;
            public bool Hover, Live;
        }

        public StrLogBoard(RectTransform parent, int rows, Action<int> click, int minRows = 4)
        {
            Rect = AvLay.Child(parent, "LogBoard");
            var rulesGo = new GameObject("Rules", typeof(RectTransform), typeof(CanvasRenderer));
            rulesGo.transform.SetParent(Rect, false);
            rules = rulesGo.AddComponent<LogRules>();
            rules.raycastTarget = false;
            AvLay.Fill(rules.rectTransform);
            onClick = click;
            this.minRows = Mathf.Clamp(minRows, 1, rows);
            lines = new Line[rows];
            for (int i = 0; i < rows; i++)
            {
                int slot = i;
                var l = new Line { Root = AvLay.Child(Rect, "Entry " + i) };
                l.Frame = AvFrame.Add(l.Root, "Frame", default(AvChamfer));
                AvLay.Fill(l.Frame.rectTransform);
                l.Rail = AvLay.Solid(l.Root, "Rail", Color.clear);
                l.Stamp = StrPaint.Fit(l.Root, "Stamp", AvTextRole.DataSmall);
                l.Text = AvText.Make(l.Root, "Text", AvTextRole.ProseSmall, "", TextAlignmentOptions.MidlineLeft, true);
                l.Hit = AvHit.On(l.Frame);
                l.Hit.Hover = h => { l.Hover = h; Style(slot); };
                l.Hit.Click = e => { if (l.Live && onClick != null) onClick(slot); };
                l.Tip = AvHelpTip.Attach(l.Frame.gameObject, null);
                l.Root.gameObject.SetActive(false);
                lines[i] = l;
            }
            Restyle();
        }

        public int Capacity => lines.Length;

        public void Begin() => count = 0;

        /// <summary>Append one entry (ignored past capacity). <paramref name="clickable"/> opens the entry's post.</summary>
        public void Add(string stamp, string text, AvState state, bool clickable, string help)
        {
            if (count >= lines.Length) return;
            Line l = lines[count];
            l.Root.gameObject.SetActive(true);
            StrPaint.Put(l.Stamp, stamp);
            StrPaint.Put(l.Text, AvStates.Glyph(state) + (text ?? ""));
            l.State = state;
            l.Live = clickable;
            l.Hit.Interactable = clickable;
            l.Tip.Text = help;
            count++;
            Style(count - 1);
        }

        public void End()
        {
            for (int i = count; i < lines.Length; i++) lines[i].Root.gameObject.SetActive(false);
            if (placed) Place(lastSlot);   // the slot decides how many of the entries show
            Changed();
        }

        private float TextW(float width) => Mathf.Max(20f, width - PadX - StampW - PadX - 8f);

        private float RowH(int i, float width) => Mathf.Max(MinH, AvText.Height(lines[i].Text, TextW(width)) + 10f);

        public override float Measure(float width)
        {
            float h = 0f;
            int n = Mathf.Min(count, minRows);
            for (int i = 0; i < n; i++) h += RowH(i, width) + 2f;
            return Mathf.Max(0f, h - 2f);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            lastSlot = s; placed = true;
            float y = 0f;
            bool full = false;
            for (int i = 0; i < lines.Length; i++)
            {
                Line l = lines[i];
                if (i >= count || full) { if (l.Root.gameObject.activeSelf) l.Root.gameObject.SetActive(false); continue; }
                if (!l.Root.gameObject.activeSelf) l.Root.gameObject.SetActive(true);
                float h = RowH(i, s.W);
                if (i >= minRows && y + h > s.H + 0.5f)
                {
                    full = true;
                    l.Root.gameObject.SetActive(false);
                    continue;
                }
                AvLay.Place(l.Root, 0f, y, s.W, h);
                AvLay.Place(l.Rail.rectTransform, 0f, 0f, 2f, h);
                AvLay.Place(l.Stamp.rectTransform, PadX, 0f, StampW, h);
                AvLay.Place(l.Text.rectTransform, PadX + StampW + 8f, 5f, TextW(s.W), h - 10f);
                y += h + 2f;
            }
            rules.ContentEnd = y;
            rules.SetVerticesDirty();
        }

        private void Style(int i)
        {
            Line l = lines[i];
            l.Frame.Paint(l.Hover && l.Live ? StrPaint.Raised : StrPaint.Inert, l.Hover && l.Live ? StrPaint.Frame : Color.clear);
            l.Rail.color = l.State == AvState.Inert ? StrPaint.State(AvState.Inert) : StrPaint.State(l.State);
            l.Stamp.color = StrPaint.Muted;
            l.Text.color = l.State == AvState.Inert ? StrPaint.Dim : StrPaint.Ink;
        }

        public override void Restyle()
        {
            for (int i = 0; i < lines.Length; i++) Style(i);
            if (rules == null) return;
            rules.color = StrPaint.Hairline;
            rules.SetVerticesDirty();
        }

        /// <summary>Hairlines in the unused part of a grown log, so the spare height reads as the log well.</summary>
        private sealed class LogRules : MaskableGraphic
        {
            public float ContentEnd;
            public float Spacing = 28f;

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                Rect r = rectTransform.rect;
                float y = r.yMax - ContentEnd - Spacing * 0.5f;
                while (y > r.yMin + 4f)
                {
                    AvQuadGraphic.Emit(vh, r, 10f, r.yMax - (y + 1f), r.width - 20f, 1f, color, color, color, color);
                    y -= Spacing;
                }
            }
        }
    }
}
