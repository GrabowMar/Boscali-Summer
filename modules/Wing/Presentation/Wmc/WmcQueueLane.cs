using NOAvionics;
using System;
using TMPro;
using UnityEngine;

using BoscaliSummer.Modules.Wing.Domain;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>ORDERS' queue lane (mockup board/orders.html .queue): what each element is flying now and the plan steps queued behind it,
    /// one line per element in use, with the QUEUE toggle (the same as holding shift on a map order: it adds the order to the scope's
    /// lane instead of replacing its task), RUN and CLEAR for the queued plan. Kit gap: no lane part, so it is a card of kit text and
    /// buttons.</summary>
    internal sealed class WmcQueueLane : AvPart
    {
        public const int Lines = ElementRoster.MaxElements;
        private const float Pad = 6f, HeadH = 24f, LineH = 17f;
        private readonly AvFrame frame;
        private readonly TMP_Text title, hint;
        private readonly TMP_Text[] lines = new TMP_Text[Lines];
        public readonly AvControl Toggle, Run, Clear;
        private int shownLines;

        public WmcQueueLane(RectTransform parent, Action toggle, Action run, Action clear)
        {
            Rect = AvLay.Child(parent, "QueueLane");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
            AvLay.Fill(frame.rectTransform);
            title = AvText.Make(Rect, "Title", AvTextRole.Micro, "QUEUE", TextAlignmentOptions.MidlineLeft);
            AvText.Fit(title, false);
            hint = AvText.Make(Rect, "Hint", AvTextRole.ProseSmall, "", TextAlignmentOptions.MidlineLeft);
            AvText.Fit(hint, false);
            Toggle = AvControl.Make(Rect, new AvControl.Spec("ADD", toggle, AvButtonStyle.Quiet));
            Run = AvControl.Make(Rect, new AvControl.Spec("RUN", run, AvButtonStyle.Quiet));
            Clear = AvControl.Make(Rect, new AvControl.Spec("CLEAR", clear, AvButtonStyle.Quiet));
            foreach (AvControl b in new[] { Toggle, Run, Clear }) b.SingleLine();
            for (int i = 0; i < Lines; i++)
            {
                lines[i] = AvText.Make(Rect, "Lane" + i, AvTextRole.ProseSmall, "", TextAlignmentOptions.MidlineLeft);
                lines[i].richText = true;
                AvText.Fit(lines[i], false);
                lines[i].gameObject.SetActive(false);
            }
            Restyle();
        }

        public override float Measure(float width) => Pad + HeadH + shownLines * LineH + Pad;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float y = Pad, bw = 50f;
            AvLay.Place(title.rectTransform, Pad + 2f, y, 52f, HeadH);
            AvLay.Place(Clear.Rect, s.W - Pad - 56f, y + 2f, 56f, HeadH - 4f);
            AvLay.Place(Run.Rect, s.W - Pad - 56f - 3f - bw, y + 2f, bw, HeadH - 4f);
            AvLay.Place(Toggle.Rect, s.W - Pad - 56f - 3f - bw - 3f - bw, y + 2f, bw, HeadH - 4f);
            AvLay.Place(hint.rectTransform, Pad + 58f, y, s.W - Pad - 56f - 3f - 2f * (bw + 3f) - Pad - 58f, HeadH);
            y += HeadH;
            for (int i = 0; i < Lines; i++)
            {
                AvLay.Place(lines[i].rectTransform, Pad + 2f, y, s.W - 2f * Pad - 4f, LineH);
                y += LineH;
            }
        }

        /// <summary>The hint beside the title ("SHIFT + MAP ORDER ADDS", the host-only note).</summary>
        public void SetHint(string text) => WmcKit.Set(hint, text);

        /// <summary>Line <paramref name="i"/> (rich text), or hidden when null; the card grows by the lines in use.</summary>
        public void SetLine(int i, string text)
        {
            bool on = text != null;
            if (lines[i].gameObject.activeSelf != on) lines[i].gameObject.SetActive(on);
            if (on) WmcKit.Set(lines[i], text);
        }

        public void SetCount(int n)
        {
            if (n == shownLines) return;
            shownLines = n;
            Changed();
        }

        public override void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("row");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.SurfaceInert), AvStyleHost.Resolve(c.Border, AvTheme.Hairline));
            title.color = AvStyleHost.FuiColor("info", AvTheme.RailInfo);
            hint.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            foreach (TMP_Text t in lines) t.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            Toggle.Restyle();
            Run.Restyle();
            Clear.Restyle();
        }
    }
}
