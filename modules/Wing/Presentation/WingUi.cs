using NOAvionics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Wing.Presentation
{
    /// <summary>The wing's own canvases (the takeover offer, the flight HUD strip) drawn from kit v2 primitives. Rects are top-left with
    /// y growing down as a negative anchored y, as every WC builder.</summary>
    internal static class WingUi
    {
        public const float RowHeight = AvTokens.RowHeight;

        /// <summary>The game HUD's own font when a HUD panel has found one; null keeps the kit's face.</summary>
        public static TMP_FontAsset Font { get; set; }

        public static Color Green => AvTheme.Accent;
        public static Color Grey => Color.grey;
        public static Color Friendly => AvTheme.Friendly;
        public static Color Warning => AvTheme.Warning;
        public static Color Alert => AvTheme.Alert;
        public static Color Dim => AvTheme.Dim;
        public static Color FrameColor => AvTheme.Frame;
        public static Color TextPrimary => AvTheme.Unity(AvTokens.TextPrimary);

        public static void Stretch(RectTransform target) => AvLay.Fill(target);

        /// <summary>The panel's ground: the kit's dark fill and edge behind everything, taking clicks.</summary>
        public static AvFrame Backdrop(RectTransform panel)
        {
            AvFrame f = AvFrame.Add(panel, "Backdrop", default);
            AvLay.Fill(f.rectTransform);
            f.Paint(AvTheme.Ground, AvTheme.Unity(AvTokens.PanelEdge));
            f.raycastTarget = true;
            f.rectTransform.SetAsFirstSibling();
            return f;
        }

        public static TMP_Text Label(RectTransform parent, string text, Rect rect, Color color, float size, FontStyles style,
            TextAlignmentOptions align)
        {
            TMP_Text label = AvText.Make(parent, "Label", AvTextRole.ProseSmall, text ?? "", align);
            if (Font != null) label.font = Font;
            label.fontSize = size;
            label.fontStyle = style;
            label.characterSpacing = 0f;
            label.color = color;
            label.overflowMode = TextOverflowModes.Ellipsis;
            WmcDraw.Place(label.rectTransform, rect);
            return label;
        }

        /// <summary>A chamfered fill.</summary>
        public static AvFrame Panel(RectTransform parent, Rect rect, Color color)
        {
            AvFrame f = AvFrame.Add(parent, "Panel", AvChamfer.Diagonal(4f));
            WmcDraw.Place(f.rectTransform, rect);
            f.Paint(color, Color.clear);
            return f;
        }

        /// <summary>A thin filled line.</summary>
        public static Image Rule(RectTransform parent, Rect rect, Color color)
        {
            Image img = AvLay.Solid(parent, "Rule", color);
            WmcDraw.Place(img.rectTransform, rect);
            return img;
        }

        public static AvFrame Outline(RectTransform parent, Rect rect, Color color) => WmcDraw.Outline(parent, rect, color);

        /// <summary>A transparent click target over <paramref name="rect"/>.</summary>
        public static Image HitButton(RectTransform parent, Rect rect, Action onClick)
        {
            Image hit = AvLay.Solid(parent, "HitTarget", Color.clear);
            WmcDraw.Place(hit.rectTransform, rect);
            AvHit.On(hit).Click = e => { if (e.button == PointerEventData.InputButton.Left) onClick?.Invoke(); };
            return hit;
        }

        public static AvControl Button(RectTransform parent, string text, Rect rect, Action onClick)
        {
            AvControl b = AvControl.Make(parent, new AvControl.Spec(text, onClick));
            WmcDraw.Place(b.Rect, rect);
            return b;
        }
    }
}
