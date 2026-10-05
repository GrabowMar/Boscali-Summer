using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.C2
{
    /// <summary>
    /// A hairline-framed box with a 20 px header (mono 10 px: key-colour title on the left, dim meta on the right).
    /// <see cref="Body"/> is the area under the header; set <see cref="BodyHeight"/> to size the box, then parent content to Body.
    /// </summary>
    internal sealed class C2Box : AvPart
    {
        public const float HeaderH = 20f;
        private const float Pad = 8f;
        private readonly AvFrame frame;
        private readonly Image headerBack, rule;
        private readonly TMP_Text titleText, metaText;
        private readonly RectTransform body;
        private string titleRaw, metaRaw = "";
        private float width = AvTokens.PanelWidth, bodyHeight = 40f;

        public C2Box(RectTransform parent, string title)
        {
            Rect = AvLay.Child(parent, "C2Box " + title);
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            headerBack = AvLay.Solid(Rect, "HeaderBack", Color.clear);
            rule = AvLay.Solid(Rect, "Rule", Color.clear);
            titleText = C2Kit.Mono(Rect, "Title", 10f, TextAlignmentOptions.MidlineLeft, true, 4f);
            metaText = C2Kit.Mono(Rect, "Meta", 10f, TextAlignmentOptions.MidlineRight);
            body = AvLay.Child(Rect, "Body");
            titleRaw = title ?? "";
            Restyle();
            Layout();
        }

        public RectTransform Body => body;

        /// <summary>Height of the area under the header; the box is <see cref="HeaderH"/> taller.</summary>
        public float BodyHeight
        {
            get => bodyHeight;
            set { if (Mathf.Approximately(value, bodyHeight)) return; bodyHeight = value; Layout(); Changed(); }
        }

        public void SetTitle(string title)
        {
            string t = title ?? "";
            if (t == titleRaw) return;
            titleRaw = t;
            Layout();
        }

        public void SetMeta(string meta)
        {
            string m = meta ?? "";
            if (m == metaRaw) return;
            metaRaw = m;
            Layout();
        }

        public override float Measure(float w) => HeaderH + bodyHeight;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            width = s.W;
            Layout();
        }

        private void Layout()
        {
            float w = width, h = HeaderH + bodyHeight;
            AvLay.Place(frame.rectTransform, 0f, 0f, w, h);
            AvLay.Place(headerBack.rectTransform, 1f, 1f, w - 2f, HeaderH - 1f);
            AvLay.Place(rule.rectTransform, 1f, HeaderH, w - 2f, 1f);
            string title = C2Kit.FitTo(titleText, titleRaw, w - 2f * Pad);
            float titleW = Mathf.Min(w - 2f * Pad, C2Kit.Width(titleText, title) + 2f);
            float metaW = Mathf.Max(0f, w - 2f * Pad - titleW - 12f);
            OpsText.Set(titleText, title);
            OpsText.Set(metaText, C2Kit.FitTo(metaText, metaRaw, metaW));
            C2Kit.Place(titleText, Pad, 0f, titleW, HeaderH);
            C2Kit.Place(metaText, w - Pad - metaW, 0f, metaW, HeaderH);
            AvLay.Place(body, 1f, HeaderH + 1f, w - 2f, Mathf.Max(0f, bodyHeight - 2f));
        }

        public override void Restyle()
        {
            frame.Paint(OpsInk.Inert, OpsInk.Hairline);
            headerBack.color = OpsInk.Sunken;
            rule.color = OpsInk.Hairline;
            titleText.color = OpsInk.Key;
            metaText.color = OpsInk.Dim;
        }
    }
}
