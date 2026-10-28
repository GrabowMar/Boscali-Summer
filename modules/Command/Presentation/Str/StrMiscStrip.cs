using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// The INFO tab's MISC lines as a lamp strip: a state-coloured lamp, the key and the value, four to a line. The long sentence of each line
    /// is its hover help, so the strip stays two lines tall.
    /// </summary>
    internal sealed class StrMiscStrip : StrPart
    {
        private const int Columns = 4;
        private const float RowH = 20f, Lamp = 6f, Gap = 6f;

        private readonly Image[] hits = new Image[StrViewModel.MiscCount], lamps = new Image[StrViewModel.MiscCount];
        private readonly TMP_Text[] keys = new TMP_Text[StrViewModel.MiscCount], values = new TMP_Text[StrViewModel.MiscCount];
        private readonly AvHelpTip[] tips = new AvHelpTip[StrViewModel.MiscCount];

        internal StrMiscStrip(RectTransform parent, StrControls controls, IStrHub hub) : base(parent, "Misc strip", controls, hub)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                hits[i] = AvLay.Solid(Rect, "Cell " + i, Color.clear);
                hits[i].raycastTarget = true;
                tips[i] = AvHelpTip.Attach(hits[i].gameObject, "");
                lamps[i] = AvLay.Solid(Rect, "Lamp " + i, Color.clear);
                lamps[i].raycastTarget = false;
                keys[i] = Text(Rect, "Key " + i, AvTextRole.Micro);
                values[i] = Text(Rect, "Value " + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight);
            }
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            for (int i = 0; i < hits.Length; i++)
            {
                StrMiscLine line = vm.Misc[i];
                AvText.Set(keys[i], line.Key ?? "");
                AvText.Set(values[i], line.Value ?? "");
                tips[i].Text = line.Text;
                lamps[i].color = AvInk.State(line.State);
                values[i].color = line.State == AvState.Info ? AvInk.Ink : AvInk.State(line.State);
            }
        }

        public override float Measure(float width) => (hits.Length + Columns - 1) / Columns * RowH;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = (s.W - Gap * (Columns - 1)) / Columns;
            for (int i = 0; i < hits.Length; i++)
            {
                float x = i % Columns * (w + Gap), y = i / Columns * RowH;
                AvLay.Place(hits[i].rectTransform, x, y, w, RowH);
                AvLay.Place(lamps[i].rectTransform, x, y + (RowH - Lamp) * 0.5f, Lamp, Lamp);
                float half = (w - Lamp - 4f) * 0.5f;
                AvLay.Place(keys[i].rectTransform, x + Lamp + 4f, y, half, RowH);
                AvLay.Place(values[i].rectTransform, x + Lamp + 4f + half, y, half, RowH);
            }
        }

        public override void Restyle()
        {
            for (int i = 0; i < keys.Length; i++) keys[i].color = AvInk.Key;
        }
    }
}
