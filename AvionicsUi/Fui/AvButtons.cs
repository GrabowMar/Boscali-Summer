using NOAvionics;
using UnityEngine;

namespace NOAvionics.Ui
{
    /// <summary>A row of equal-width controls (32 high). The only way controls enter a flow.</summary>
    public sealed class AvButtons : AvPart
    {
        public AvControl[] Controls { get; }

        public AvButtons(RectTransform parent, AvControl.Spec[] specs)
        {
            Rect = AvLay.Child(parent, "Buttons");
            Controls = new AvControl[specs.Length];
            for (int i = 0; i < specs.Length; i++) Controls[i] = AvControl.Make(Rect, specs[i]);
        }

        public override float Measure(float width) => AvGridTokens.Row;

        public override void Place(AvSlot slot)
        {
            base.Place(slot);
            float w = AvFlowMath.ColumnWidth(slot.W, Controls.Length, AvGridTokens.Gap);
            for (int i = 0; i < Controls.Length; i++) AvLay.Place(Controls[i].Rect, i * (w + AvGridTokens.Gap), 0f, w, slot.H);
        }

        public override void Restyle() { foreach (AvControl c in Controls) c.Restyle(); }
    }
}
