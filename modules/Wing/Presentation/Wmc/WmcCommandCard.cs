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
    /// grid of keys. Each key has a metadata line for its shortcut, active elements and required input above the command name.
    /// The left gutter labels each group and carries its colour rail. A key
    /// that cannot be pressed is greyed, its reason in its help (the per-key reason words were cut 2026-11-15). Kit gap: a key
    /// with corner marks is not a kit control, so the marks are text and plates drawn over a kit <see cref="AvControl"/>.</summary>
    internal sealed class WmcCommandCard : AvPart
    {
        public const int Count = OrderGrid.Rows * OrderGrid.Columns;
        private const float KeyH = 42f, Gap = 4f, LedW = 26f, Gutter = 62f;

        public readonly AvControl[] Cells = new AvControl[Count];
        private readonly Image[] rails = new Image[OrderGrid.Rows], leds = new Image[Count];
        private readonly TMP_Text[] keys = new TMP_Text[Count], marks = new TMP_Text[Count], ledText = new TMP_Text[Count];
        // The row's group in the left gutter (ATK, DEF...), in its rail colour: the spec's "six labelled rows".
        private readonly TMP_Text[] rowTags = new TMP_Text[OrderGrid.Rows];

        public WmcCommandCard(RectTransform parent, Action<int> press)
        {
            Rect = AvLay.Child(parent, "CommandCard");
            for (int r = 0; r < OrderGrid.Rows; r++)
            {
                rowTags[r] = Mark(Rect, "RowTag" + r, AvTextRole.Micro, TextAlignmentOptions.MidlineLeft);
                rowTags[r].text = OrderGrid.RowLabels[r];
                rails[r] = AvLay.Solid(Rect, "GroupRail" + r, Color.clear);
            }
            for (int k = 0; k < Count; k++)
            {
                int i = k;
                AvControl cell = Cells[k] = AvControl.Make(Rect, new AvControl.Spec("", () => press(i)));
                cell.SingleLine();
                cell.SetLabelInsets(5f, 17f, 5f, 3f);
                cell.Bracket = 0f;
                RectTransform r = cell.Rect;
                keys[k] = Mark(r, "Key", AvTextRole.DataSmall, TextAlignmentOptions.MidlineLeft);
                marks[k] = Mark(r, "Input", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
                leds[k] = AvLay.Solid(r, "Led", Color.clear);
                ledText[k] = Mark(r, "LedText", AvTextRole.Micro, TextAlignmentOptions.Center);
                leds[k].gameObject.SetActive(false);
                ledText[k].gameObject.SetActive(false);
            }
            Restyle();
        }

        private static TMP_Text Mark(RectTransform parent, string name, AvTextRole role, TextAlignmentOptions align) => WmcSheet.Text(parent, name, role, align);

        public override float Measure(float width) => Rows * KeyH + (Rows - 1) * Gap;

        private const int Rows = OrderGrid.Rows;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = AvFlowMath.ColumnWidth(s.W - Gutter, OrderGrid.Columns, Gap);
            for (int r = 0; r < OrderGrid.Rows; r++)
            {
                float y = r * (KeyH + Gap);
                AvLay.Place(rails[r].rectTransform, 0f, y + 5f, 2f, KeyH - 10f);
                AvLay.Place(rowTags[r].rectTransform, 5f, y, Gutter - 7f, KeyH);
            }
            for (int k = 0; k < Count; k++)
            {
                int r = k / OrderGrid.Columns, c = k % OrderGrid.Columns;
                float x = Gutter + c * (w + Gap), y = r * (KeyH + Gap);
                AvLay.Place(Cells[k].Rect, x, y, w, KeyH);
                AvLay.Place(keys[k].rectTransform, 6f, 2f, 15f, 15f);
                AvLay.Place(marks[k].rectTransform, w - 37f, 2f, 31f, 15f);
                AvLay.Place(leds[k].rectTransform, 23f, 3f, LedW, 13f);
                AvLay.Place(ledText[k].rectTransform, 23f, 2f, LedW, 15f);
            }
        }

        /// <summary>One key's static face: label, wing-key letter, group colour, input mark.</summary>
        public void SetFace(int k, string label, GridInput input)
        {
            Cells[k].Label = label;
            AvText.Set(keys[k], k < ChordResolver.OrderKeys.Length ? ChordResolver.OrderKeys[k].ToString() : "");
            AvText.Set(marks[k], input == GridInput.Point ? "PT" : input == GridInput.Target ? "TGT" : input == GridInput.Area ? "AREA" : "");
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
            if (on) AvText.Set(ledText[k], letters);
        }

        /// <summary>Hides key <paramref name="k"/>, leaving its place in the grid empty (LAND and CARGO while the wing has no
        /// helicopter). A key that is off for a moment stays shown, greyed, with the reason in its help.</summary>
        public void SetShown(int k, bool on)
        {
            if (Cells[k].gameObject.activeSelf != on) Cells[k].gameObject.SetActive(on);
        }

        public override void Restyle()
        {
            Color hint = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            Color ink = AvStyleHost.FuiColor("ground", AvTheme.TextInk);
            for (int k = 0; k < Count; k++)
            {
                Cells[k].Restyle();
                keys[k].color = hint;
                marks[k].color = hint;
                leds[k].color = AvStyleHost.FuiColor("select", AvTheme.Accent);
                ledText[k].color = ink;
            }
            for (int r = 0; r < OrderGrid.Rows; r++)
            {
                rails[r].color = WmcState.Color(OrderGrid.RowRail(r));
                rowTags[r].color = AvStyleHost.FuiInk("row-name", AvTheme.TextPrimary);
            }
        }
    }
}
