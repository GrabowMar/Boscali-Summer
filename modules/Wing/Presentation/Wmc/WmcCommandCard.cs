using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Modules.Wing.Domain.Pure;
namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>ORDERS' command card (spec 2026-10-04 §4.2; mockup board/orders.html): the 24 orders of <see cref="OrderGrid"/> as a 4 × 6
    /// grid of keys. Each key is a kit button with the wing-key letter (<see cref="ChordResolver.OrderKeys"/>) in its top-left corner, a
    /// group colour rail across its top, the kind of input it needs ("PT" a map point, "TGT" an enemy, "AREA" an area) bottom right, an
    /// LED top right while an element is flying it (the element letters), and a short reason when it cannot be pressed. Kit gap: a key
    /// with corner marks is not a kit control, so the marks are text and plates drawn over a kit <see cref="AvControl"/>.</summary>
    internal sealed class WmcCommandCard : AvPart
    {
        public const int Count = OrderGrid.Rows * OrderGrid.Columns;
        private const float KeyH = 36f, Gap = 3f;

        public readonly AvControl[] Cells = new AvControl[Count];
        private readonly Image[] rails = new Image[Count], leds = new Image[Count];
        private readonly TMP_Text[] keys = new TMP_Text[Count], marks = new TMP_Text[Count], ledText = new TMP_Text[Count], whys = new TMP_Text[Count];

        public WmcCommandCard(RectTransform parent, Action<int> press)
        {
            Rect = AvLay.Child(parent, "CommandCard");
            for (int k = 0; k < Count; k++)
            {
                int i = k;
                AvControl cell = Cells[k] = AvControl.Make(Rect, new AvControl.Spec("", () => press(i)));
                cell.SingleLine();
                RectTransform r = cell.Rect;
                rails[k] = AvLay.Solid(r, "Rail", Color.clear);
                keys[k] = Mark(r, "Key", AvTextRole.DataSmall, TextAlignmentOptions.TopLeft);
                marks[k] = Mark(r, "Input", AvTextRole.Micro, TextAlignmentOptions.BottomRight);
                leds[k] = AvLay.Solid(r, "Led", Color.clear);
                ledText[k] = Mark(r, "LedText", AvTextRole.Micro, TextAlignmentOptions.Center);
                whys[k] = Mark(r, "Why", AvTextRole.Micro, TextAlignmentOptions.BottomLeft);
                leds[k].gameObject.SetActive(false);
                ledText[k].gameObject.SetActive(false);
            }
            Restyle();
        }

        private static TMP_Text Mark(RectTransform parent, string name, AvTextRole role, TextAlignmentOptions align)
        {
            TMP_Text t = AvText.Make(parent, name, role, "", align);
            AvText.Fit(t, false);
            return t;
        }

        public override float Measure(float width) => Rows * KeyH + (Rows - 1) * Gap;

        private const int Rows = OrderGrid.Rows;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = AvFlowMath.ColumnWidth(s.W, OrderGrid.Columns, Gap);
            for (int k = 0; k < Count; k++)
            {
                int r = k / OrderGrid.Columns, c = k % OrderGrid.Columns;
                float x = c * (w + Gap), y = r * (KeyH + Gap);
                AvLay.Place(Cells[k].Rect, x, y, w, KeyH);
                AvLay.Place(rails[k].rectTransform, 0f, 0f, w, 2f);
                AvLay.Place(keys[k].rectTransform, 5f, 1f, 18f, 18f);
                AvLay.Place(marks[k].rectTransform, w - 44f, KeyH - 19f, 40f, 18f);
                AvLay.Place(leds[k].rectTransform, w - 32f, 3f, 28f, 15f);
                AvLay.Place(ledText[k].rectTransform, w - 32f, 2f, 28f, 18f);
                AvLay.Place(whys[k].rectTransform, 5f, KeyH - 19f, w - 50f, 18f);
            }
        }

        /// <summary>One key's static face: label, wing-key letter, group colour, input mark.</summary>
        public void SetFace(int k, string label, GridInput input)
        {
            Cells[k].Label = label;
            WmcKit.Set(keys[k], k < ChordResolver.OrderKeys.Length ? ChordResolver.OrderKeys[k].ToString() : "");
            WmcKit.Set(marks[k], input == GridInput.Point ? "PT" : input == GridInput.Target ? "TGT" : input == GridInput.Area ? "AREA" : "");
            rails[k].color = WmcState.Color(OrderGrid.RowRail(k / OrderGrid.Columns));
        }

        /// <summary>The element letters flying this order now ("A", "AB"); empty for none.</summary>
        public void SetLed(int k, string letters)
        {
            bool on = !string.IsNullOrEmpty(letters);
            if (leds[k].gameObject.activeSelf != on)
            {
                leds[k].gameObject.SetActive(on);
                ledText[k].gameObject.SetActive(on);
            }
            if (on) WmcKit.Set(ledText[k], letters);
        }

        /// <summary>The reason a key is off, in a word or two ("HOST", "NO AC", "SOON"); empty when it is pressable.</summary>
        public void SetWhy(int k, string word) => WmcKit.Set(whys[k], word ?? "");

        public override void Restyle()
        {
            Color hint = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            Color key = AvStyleHost.FuiColor("info", AvTheme.RailInfo), caution = AvStyleHost.FuiColor("caution", AvTheme.RailCaution);
            Color ink = AvStyleHost.FuiColor("ground", AvTheme.TextInk);
            for (int k = 0; k < Count; k++)
            {
                Cells[k].Restyle();
                keys[k].color = key;
                marks[k].color = hint;
                whys[k].color = caution;
                leds[k].color = AvStyleHost.FuiColor("select", AvTheme.Accent);
                ledText[k].color = ink;
                rails[k].color = WmcState.Color(OrderGrid.RowRail(k / OrderGrid.Columns));
            }
        }
    }
}
