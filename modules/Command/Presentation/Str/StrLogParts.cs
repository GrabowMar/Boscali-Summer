using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// The staff's log as a teletype: up to eight numbered lines, newest first. The wire carries no time per line, so the numbers are row numbers
    /// and the section says so; nothing here invents a clock.
    /// </summary>
    internal sealed class StrTeletype : StrPart
    {
        private const float NumberW = 26f, RowGap = 4f;

        private readonly TMP_Text[] numbers = new TMP_Text[StrViewModel.MaxLog], lines = new TMP_Text[StrViewModel.MaxLog];
        private int count;

        internal StrTeletype(RectTransform parent, StrControls controls, IStrHub hub) : base(parent, "Teletype", controls, hub)
        {
            for (int i = 0; i < numbers.Length; i++)
            {
                numbers[i] = Text(Rect, "No " + i, AvTextRole.DataSmall, TextAlignmentOptions.TopLeft);
                lines[i] = Text(Rect, "Line " + i, AvTextRole.DataSmall, TextAlignmentOptions.TopLeft, true);
            }
            Restyle();
        }

        protected override void Apply(StrViewModel vm)
        {
            count = vm.LogCount;
            SetShown(count > 0);
            for (int i = 0; i < numbers.Length; i++)
            {
                bool on = i < count;
                numbers[i].gameObject.SetActive(on);
                lines[i].gameObject.SetActive(on);
                if (!on) continue;
                AvText.Set(numbers[i], (i + 1).ToString("00"));
                AvText.Set(lines[i], vm.Log[i]);
            }
            Changed();
        }

        public override float Measure(float width)
        {
            float y = 0f;
            for (int i = 0; i < count; i++) y += Mathf.Max(16f, AvText.Height(lines[i], width - NumberW - 6f)) + RowGap;
            return y;
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float y = 0f, w = s.W - NumberW - 6f;
            for (int i = 0; i < count; i++)
            {
                float h = Mathf.Max(16f, AvText.Height(lines[i], w));
                AvLay.Place(numbers[i].rectTransform, 0f, y, NumberW, 16f);
                AvLay.Place(lines[i].rectTransform, NumberW + 6f, y, w, h);
                y += h + RowGap;
            }
        }

        public override void Restyle()
        {
            for (int i = 0; i < numbers.Length; i++)
            {
                numbers[i].color = AvInk.Key;
                lines[i].color = AvInk.Ink;
            }
        }
    }
}
