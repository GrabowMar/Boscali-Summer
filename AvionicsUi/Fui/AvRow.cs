using System;
using System.Collections.Generic;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>List row: state rail · name / sub (wraps) · mono value · up to 3 trailing controls.</summary>
    public sealed class AvRow : AvPart
    {
        private const float PadX = 10f, PadY = 6f, ValueW = 88f, TrailW = 56f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text name, sub, value;
        private readonly List<AvControl> trailing = new List<AvControl>(3);
        private AvState state = AvState.Info;
        private bool hover, armed, interactable = true;

        public AvRow(RectTransform parent, Action onClick = null)
        {
            Rect = AvLay.Child(parent, "Row");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer)); AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            name = AvText.Make(Rect, "Name", AvTextRole.Label, "", TextAlignmentOptions.TopLeft, true);
            sub = AvText.Make(Rect, "Sub", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            value = AvText.Make(Rect, "Value", AvTextRole.DataStrong, "", TextAlignmentOptions.TopRight);
            if (onClick != null)
            {
                AvHit hit = AvHit.On(frame);
                hit.Hover = h => { hover = h; Restyle(); };
                hit.Click = e => onClick();
            }
            Restyle();
        }

        public bool Armed { get => armed; set { armed = value; Restyle(); } }
        public bool Interactable { get => interactable; set { interactable = value; Restyle(); } }

        public void Set(string n, string s, string v, AvState st = AvState.Info)
        {
            if (name.text != (n ?? "")) name.text = n ?? "";
            if (sub.text != (s ?? "")) sub.text = s ?? "";
            if (value.text != (v ?? "")) value.text = v ?? "";
            if (st != state) { state = st; Restyle(); }
        }

        public AvControl AddTrailing(AvControl.Spec spec)
        {
            if (trailing.Count >= 3) return null;
            AvControl c = AvControl.Make(Rect, spec);
            trailing.Add(c);
            return c;
        }

        private float TextWidth(float width) => width - 2f * PadX - 4f - (value.text.Length > 0 ? ValueW : 0f) - trailing.Count * (TrailW + 4f);

        public override float Measure(float width)
        {
            float w = TextWidth(width);
            float h = PadY + AvText.Height(name, w) + (sub.text.Length > 0 ? 2f + AvText.Height(sub, w) : 0f) + PadY;
            return Mathf.Max(AvGridTokens.Row, h);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = TextWidth(s.W), nh = AvText.Height(name, w);
            AvLay.Place(rail.rectTransform, 0f, 0f, 2f, s.H);
            AvLay.Place(name.rectTransform, PadX + 4f, PadY, w, nh);
            AvLay.Place(sub.rectTransform, PadX + 4f, PadY + nh + 2f, w, AvText.Height(sub, w));
            float x = s.W - PadX;
            for (int i = trailing.Count - 1; i >= 0; i--) { x -= TrailW; AvLay.Place(trailing[i].Rect, x, (s.H - 24f) * 0.5f, TrailW, 24f); x -= 4f; }
            if (value.text.Length > 0) AvLay.Place(value.rectTransform, x - ValueW, PadY, ValueW, 18f);
        }

        public override void Restyle()
        {
            string st = !interactable ? "disabled" : armed ? "armed" : hover ? "hover" : null;
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(state), st);
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            rail.color = AvStyleHost.Resolve(r.Rail, AvTheme.RailInfo);
            name.color = !interactable ? AvTheme.Disabled : AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
            sub.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            value.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-value " + AvStates.Class(state)).Color, AvTheme.TextPrimary);
            foreach (AvControl c in trailing) c.Restyle();
        }
    }
}
