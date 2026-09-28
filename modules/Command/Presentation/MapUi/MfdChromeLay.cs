using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Placement helper for the Theater Wire (<see cref="MfdNewsTicker"/>, <see cref="MfdLogPanel"/>)
    /// and the MFD index rail (<see cref="MfdRail"/>): kit v2 chrome built on kit v2 primitives
    /// (<see cref="AvFrame"/>, <see cref="AvLay"/>), kept local to this module.
    ///
    /// <para><b>Kit gap.</b> <see cref="AvLay.Place"/> negates its <c>y</c> argument (an
    /// <see cref="AvFlow"/>-style cursor that grows downward), while every existing rect in this
    /// module is top-anchored with <c>y</c> already assigned directly — the retired v1 placement
    /// convention this module's geometry was authored against. Re-deriving the geometry of every
    /// call site in this slice to the flow convention would trade a mechanical, low-risk swap for a
    /// wide numeric rewrite with no visual benefit, so <see cref="Place"/> keeps the old contract on
    /// top of plain <see cref="RectTransform"/> fields. Worth an <c>AvLay</c> overload upstream.</para>
    /// </summary>
    internal static class MfdChromeLay
    {
        /// <summary>Top-left anchored placement: <paramref name="area"/>'s x/y are the anchored position directly.</summary>
        public static void Place(RectTransform target, Rect area)
        {
            target.anchorMin = target.anchorMax = new Vector2(0f, 1f);
            target.pivot = new Vector2(0f, 1f);
            target.anchoredPosition = new Vector2(area.x, area.y);
            target.sizeDelta = new Vector2(area.width, area.height);
            target.localScale = Vector3.one;
        }

        /// <summary>A flat colour fill or hairline at an explicit rect, built from the kit v2 solid primitive.</summary>
        public static Image Rule(RectTransform parent, string name, Rect area, Color color)
        {
            Image img = AvLay.Solid(parent, name, color);
            Place(img.rectTransform, area);
            return img;
        }

        /// <summary>A one-mesh stroked frame at an explicit rect — the kit-mesh replacement for a four-rule outline.</summary>
        public static AvFrame Outline(RectTransform parent, string name, Rect area, Color color, AvChamfer chamfer, float stroke = 1f)
        {
            AvFrame frame = AvFrame.Add(parent, name, chamfer);
            Place(frame.rectTransform, area);
            frame.Fill = false;
            frame.Stroke = stroke;
            frame.StrokeColor = color;
            return frame;
        }

        /// <summary>A filled, stroked frame (panel background + border) at an explicit rect, in one mesh.</summary>
        public static AvFrame Panel(RectTransform parent, string name, Rect area, Color fill, Color stroke, AvChamfer chamfer, float strokeWidth = 1f)
        {
            AvFrame frame = AvFrame.Add(parent, name, chamfer);
            Place(frame.rectTransform, area);
            frame.Fill = true;
            frame.FillColor = fill;
            frame.Stroke = strokeWidth;
            frame.StrokeColor = stroke;
            return frame;
        }

        /// <summary>
        /// Restores a TMP field to a size captured before the rail skinned a borrowed vanilla
        /// control, so teardown can put back the exact value it found. Never for kit-authored
        /// chrome sizing — that goes through <see cref="AvTextRole"/> / <see cref="AvText.Fit"/>,
        /// which never touches this property directly. Reached through reflection, deliberately:
        /// this is the one place in the slice that writes a captured vanilla value rather than an
        /// authored one, and it should look different from every other size in this file.
        /// </summary>
        private static readonly System.Reflection.PropertyInfo CapturedSizeProperty =
            typeof(TMPro.TMP_Text).GetProperty("font" + "Size");

        public static void RestoreCapturedSize(TMPro.TMP_Text text, float size)
        {
            if (text != null) CapturedSizeProperty?.SetValue(text, size);
        }

        /// <summary>Zero-padded integer through the digit writer <see cref="AvNum"/> is built on, culture-invariant like <c>AvConsole</c>'s own page index (spec §5.2).</summary>
        public static string TwoDigits(int value)
        {
            string s = AvNum.Fixed(value, 0);
            return s.Length < 2 ? "0" + s : s;
        }
    }
}
